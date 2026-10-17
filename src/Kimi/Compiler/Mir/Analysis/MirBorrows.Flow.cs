// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's carrying (A3 5, Carrying and Calls, with E3 and E8): each holder's set forward over the dataflow's chains, joined by node union. A
// location's effect is planned on the state before it as entries, which the checks replay reads, then committed; at a destruction in place
// the Loans it ends are then removed from every holder (`step`: σ_out \ ended, so a call's transfer still reads its destroyed target).
internal sealed partial class MirBorrows
{
    private const byte Cleared = 0;
    private const byte Replaced = 1;
    private const byte Joined = 2;

    private readonly List<int> targets = []; // The holders a reference's value refers to (refs).
    private Entry[] entries = [];
    private int[] argValues = [];
    private int[] argOwns = [];
    private int entryCount;
    private int chainHead = -1; // The checks replay's chain: its head and first block.
    private int chainFirst;

    // σ at the entry: a Loan-capable argument refers to storage the body does not own (Φ_E).
    private void Initial(ulong[] state)
    {
        state.AsSpan(0, this.holderCount).Clear();
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (this.slots[i] >= 0)
            {
                state[this.slots[i]] = (ulong)this.escapedSet;
            }
        }
    }

    // A block's locations: in the checks replay, the live holders after each, its plan on the state before it and its checks first.
    private void Carry(ulong[] state, int block, bool checks)
    {
        if (checks && flow.Head(block) != this.chainHead)
        {
            (this.chainHead, this.chainFirst) = (flow.Head(block), block);
            this.Enter(this.chainHead);
        }

        for (var i = 0; i <= this.body.Blocks[block].Count; i++)
        {
            var location = this.Location(block, i);
            this.Work++;
            this.Plan(state, block, i);
            if (checks)
            {
                this.Advance(location);
                this.Check(state, block, i, location);
            }

            this.Commit(state);
            if (this.ended[location] != 0)
            {
                for (var s = 0; s < this.holderCount; s++)
                {
                    state[s] = (ulong)this.sets.Remove((int)state[s], this.ended[location]);
                }

                this.Work += this.holderCount;
            }
        }
    }

    // The node-union join (R4-M34).
    private bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming)
    {
        var grew = false;
        for (var s = 0; s < target.Length; s++)
        {
            var union = (ulong)this.sets.Union((int)target[s], (int)incoming[s]);
            grew |= union != target[s];
            target[s] = union;
        }

        this.Work += target.Length;
        return grew;
    }

    // The entries of a location on the state: a statement's storage end, or its rvalue's value stored into its Place after the Moves it
    // makes; a Call's.
    private void Plan(ulong[] state, int block, int index)
    {
        this.entryCount = 0;
        ref readonly var data = ref this.body.Blocks[block];
        if (index == data.Count)
        {
            if (data.Terminator.Kind == MirTerminatorKind.Call)
            {
                this.Call(state, data.Terminator);
            }

            return;
        }

        ref readonly var statement = ref this.body.Statements[data.Start + index];
        if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead or MirStatementKind.EndBorrow)
        {
            this.Add(statement.Place.Root, 0, 0, Cleared, issue: false);
        }
        else if (statement.Kind == MirStatementKind.Assign)
        {
            var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
            var (value, own) = this.Value(state, rvalue, data.Start + index);
            if (statement.Place is { Projection: 0, Root: >= 0 } into && this.body.Locals[into.Root].Role == MirLocalRole.GuardCandidate)
            {
                own = this.sets.Add(own, this.guardBase + into.Root); // E7: a value issued into a guard candidate H carries γ_H.
            }

            this.Moves(rvalue.Start, rvalue.Count);
            var issue = rvalue.Kind == MirRvalueKind.Ref ||
                (rvalue.Kind == MirRvalueKind.Use && this.body.Operands[rvalue.Start] is { Kind: MirOperandKind.PairAcquire } acquire &&
                    cases.Reborrows(acquire));
            this.Store(state, statement.Place, value, own, issue);
        }
    }

    private void Commit(ulong[] state)
    {
        for (var e = 0; e < this.entryCount; e++)
        {
            var (slot, value, own, op, _) = this.entries[e];
            value = own != 0 ? this.sets.Union(value, own) : value;
            state[slot] = op switch
            {
                Cleared => 0,
                Replaced => (ulong)value,
                _ => (ulong)this.sets.Union((int)state[slot], value),
            };
        }
    }

    // A local's entry (none for a local that holds no Loan) or a slot's.
    private void Add(int local, int value, int own, byte op, bool issue)
    {
        if (local >= 0 && this.slots[local] >= 0)
        {
            this.AddSlot(this.slots[local], value, own, op, issue);
        }
    }

    private void AddSlot(int slot, int value, int own, byte op, bool issue)
    {
        MirDataflow.Grow(ref this.entries, this.entryCount + 1);
        this.entries[this.entryCount++] = new(slot, value, own, op, issue);
    }

    // val(rv) (A3 5) and the Loans it issues: an operand's, a Ref's, the union of an aggregate's or a value-keeping Cast's operands, an Opaque
    // site's roots with τ_s and Φ_E; under the view an Adapt is the rvalue its case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
            case MirRvalueKind.Adapt:
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast or MirCastKind.Pointer:
                return this.Operand(state, rvalue.Start);
            case MirRvalueKind.Ref when (MirRefKind)rvalue.Op != MirRefKind.Raw:
                return this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
            case MirRvalueKind.Aggregate:
                var (value, own) = (0, 0);
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    var operand = this.Operand(state, o);
                    (value, own) = (this.sets.Union(value, operand.Value), this.sets.Union(own, operand.Own));
                }

                return (value, own);
            case MirRvalueKind.Opaque:
                var site = this.body.OpaqueSites[rvalue.Arg];
                var opaque = this.sets.Add(this.escapedSet, 1 + rvalue.Arg);
                foreach (var root in this.body.OpaqueRoots.Slice(site.Start, site.Count))
                {
                    opaque = this.sets.Union(opaque, this.Root(state, root));
                }

                return (opaque, 0);
            default:
                return (0, 0);
        }
    }

    // An operand's value: a constant's none, a Place's read, a Reborrow's Loan and parents.
    private (int Value, int Own) Operand(ulong[] state, int index)
    {
        var operand = this.body.Operands[index];
        if (operand.Kind == MirOperandKind.Const)
        {
            return (0, 0);
        }

        return operand.Kind == MirOperandKind.PairAcquire && cases.Reborrows(operand)
            ? this.Issued(state, operand.Place, reborrow: true, this.loanOfOperand[index])
            : (this.Read(state, operand.Place), 0);
    }

    // A root's state: a holder's, a static's {Φ_E}, none for another local.
    private int Root(ulong[] state, int root)
    {
        if (root < 0)
        {
            return this.escapedSet;
        }

        return this.slots[root] >= 0 ? (int)state[this.slots[root]] : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k borrow Derefs; a raw pointer's referent is {Φ_E}.
    private int Read(ulong[] state, MirPlace place)
    {
        var value = this.Root(state, place.Root);
        var count = this.Walk(place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            value = this.walk[i].Class switch
            {
                StepClass.Shared or StepClass.Uniq => this.Content(state, value),
                StepClass.Raw => this.escapedSet,
                _ => value,
            };
        }

        return value;
    }

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on.
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var (layer, parents) = (this.Root(state, place.Root), 0);
        var count = this.Walk(place, reborrow, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            switch (this.walk[i].Class)
            {
                case StepClass.Shared:
                    (parents, layer) = (layer, this.Content(state, layer));
                    break;
                case StepClass.Uniq:
                    (parents, layer) = (this.sets.Union(parents, layer), this.Content(state, layer));
                    break;
                case StepClass.Raw:
                    (parents, layer) = (this.sets.Union(parents, this.escapedSet), this.escapedSet);
                    break;
            }
        }

        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }

    // content(S) (A3 5): the states of the holders its direct Loans name, and E's where it carries Φ_E or no direct Loan.
    private int Content(ulong[] state, int set)
    {
        var (content, direct, escaped) = (0, false, false);
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                content = loan.Named >= 0 ? this.sets.Union(content, (int)state[loan.Named]) : content;
            }
        }

        return escaped || !direct ? this.sets.Union(content, (int)state[Escaped]) : content;
    }

    // refs(S) (A3 5) into the targets: the holders its direct Loans name, and E where it carries Φ_E or no direct Loan.
    private void Refs(int set)
    {
        var (direct, escaped) = (false, false);
        this.targets.Clear();
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan.Named >= 0)
                {
                    this.targets.Add(loan.Named);
                }
            }
        }

        if (escaped || !direct)
        {
            this.targets.Add(Escaped);
        }
    }

    // assign D := v (A3 5): a whole root replaced, a direct part joined (P1), through k borrow Derefs every holder refs^k names joined
    // (P10), through a raw pointer or into a static E joined.
    private void Store(ulong[] state, MirPlace place, int value, int own, bool issue)
    {
        var (count, last) = (this.Walk(place, reborrow: false, ref this.walk), -1);
        for (var i = 0; i < count; i++)
        {
            last = this.walk[i].Class is StepClass.Shared or StepClass.Uniq or StepClass.Raw ? i : last;
        }

        if (last < 0)
        {
            if (place.Root < 0)
            {
                this.AddSlot(Escaped, value, own, Joined, issue);
            }

            this.Add(place.Root, value, own, count == 0 ? Replaced : Joined, issue);
            return;
        }

        var (layer, raw) = (this.Root(state, place.Root), this.walk[last].Class == StepClass.Raw);
        for (var i = 0; i < last; i++)
        {
            layer = this.walk[i].Class switch
            {
                StepClass.Shared or StepClass.Uniq => this.Content(state, layer),
                StepClass.Raw => this.escapedSet,
                _ => layer,
            };
        }

        this.Refs(raw ? this.escapedSet : layer);
        foreach (var target in this.targets)
        {
            this.AddSlot(target, value, own, Joined, issue);
        }
    }

    // A Move of a whole root empties it.
    private void Moves(int start, int count)
    {
        for (var o = start; o < start + count; o++)
        {
            if (this.body.Operands[o] is { Kind: MirOperandKind.Move, Place: { Projection: 0, Root: >= 0 } moved })
            {
                this.Add(moved.Root, 0, 0, Cleared, issue: false);
            }
        }
    }

    // A Call (A3 5, Calls): its arguments' Moves, then each row of its retention (Retaining) into the result or through an argument's
    // referents, then the result into the destination; Φ_E for an untied result. An intrinsic's operands all reach its result; a default
    // template's call is an unknown callee: every argument reaches the result and every exclusive argument's referents, and the result is
    // untied. A Value callee's environment, its value or what a reference to it refers to, reaches the result too (its captures; temporary
    // until F7 reads TyCtxt.Closure, unit 2c).
    private void Call(ulong[] state, in MirTerminator call)
    {
        MirDataflow.Grow(ref this.argValues, call.Count);
        MirDataflow.Grow(ref this.argOwns, call.Count);
        var (all, allOwn) = (0, 0);
        for (var i = 0; i < call.Count; i++)
        {
            (this.argValues[i], this.argOwns[i]) = this.Operand(state, call.Start + i);
            (all, allOwn) = (this.sets.Union(all, this.argValues[i]), this.sets.Union(allOwn, this.argOwns[i]));
        }

        var callee = call.Callee;
        var result = callee.Kind == MirCalleeKind.Value && call.Operand.Kind != MirOperandKind.Const ? this.Read(state, call.Operand.Place) : 0;
        (result, var resultOwn) = (result != 0 && MirTypes.IsReference(this.TypeOf(call.Operand)) ? this.Content(state, result) : result, 0);
        this.Moves(call.Start, call.Count);
        if (callee.Kind == MirCalleeKind.Value && call.Operand is { Kind: MirOperandKind.Move, Place: { Projection: 0 } moved })
        {
            this.Add(moved.Root, 0, 0, Cleared, issue: false);
        }

        if (callee.Kind == MirCalleeKind.Intrinsic)
        {
            (result, resultOwn) = (all, allOwn);
        }
        else if (callee.Kind == MirCalleeKind.Fn && callee.Arg > 0)
        {
            (result, resultOwn) = (this.sets.Union(all, this.escapedSet), allOwn);
            for (var j = 0; j < call.Count; j++)
            {
                if (MirTypes.IsExclusive(this.TypeOf(call.Start + j)))
                {
                    this.Refs(this.sets.Union(this.argValues[j], this.argOwns[j]));
                    foreach (var target in this.targets)
                    {
                        this.AddSlot(target, all, allOwn, Joined, issue: false);
                    }
                }
            }
        }
        else if (this.Retaining(call) is { IsLimit: false } retention)
        {
            foreach (var row in retention.Rows)
            {
                var input = MirCallee.Position(callee.Symbol, row.Input);
                var output = row.Output == Retention.Result ? -1 : MirCallee.Position(callee.Symbol, row.Output);
                if (input >= call.Count || output >= call.Count)
                {
                    continue;
                }

                var (value, own) = this.Retained(state, call.Start + input, input, row.Kind);
                if (output < 0)
                {
                    (result, resultOwn) = (this.sets.Union(result, value), this.sets.Union(resultOwn, own));
                    continue;
                }

                this.Refs(this.sets.Union(this.argValues[output], this.argOwns[output]));
                foreach (var target in this.targets)
                {
                    this.AddSlot(target, value, own, Joined, issue: false);
                }
            }

            result = retention.Untied ? this.sets.Union(result, this.escapedSet) : result;
        }

        if (call.Target >= 0)
        {
            this.Store(state, call.Place, result, resultOwn, issue: false);
        }
    }

    // The retention a Call's contract gives (TyCtxt.Retains over the callee and Subst, or a Value callee's declared signature); none for
    // another terminator, an intrinsic or a default template's call. Its Limit, a contract Binding does not instantiate, retains nothing the
    // stratum follows: the checks record a located Unsupported_Kd at the call (R4-M51).
    private CallRetention? Retaining(in MirTerminator call)
    {
        var callee = call.Callee;
        if (call.Kind != MirTerminatorKind.Call || callee.Kind == MirCalleeKind.Intrinsic || (callee.Kind == MirCalleeKind.Fn && callee.Arg > 0))
        {
            return null;
        }

        return callee.Kind == MirCalleeKind.Value
            ? types.Retains(this.TypeOf(call.Operand)!, this.body.Consts[callee.Arg].Type, this.body.Key.Decl)
            : types.Retains(callee.Symbol!, callee.Subst, this.body.Key.Decl);
    }

    // ret(i, κ) (A3 5): Value, the argument's value and, for a borrow (a reference or a view handle, Borrows), a read through it; Content,
    // a read through it only.
    private (int Value, int Own) Retained(ulong[] state, int operand, int position, RetentionKind kind)
    {
        var (value, own) = (this.argValues[position], this.argOwns[position]);
        var through = this.Content(state, this.sets.Union(value, own));
        if (kind == RetentionKind.Content)
        {
            return (through, 0);
        }

        return Borrows(this.TypeOf(operand)) ? (this.sets.Union(value, through), own) : (value, own);
    }

    // An operand's case Type: a Reborrow's is its Place's reference.
    private BoundType? TypeOf(int operand) => this.TypeOf(this.body.Operands[operand]);

    private BoundType? TypeOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : cases.Type(this.body.TypeOf(operand.Place)!);

    // Whether a Place passes a borrow or raw Deref, so writing it uses its root.
    private bool Through(MirPlace place)
    {
        var count = this.Walk(place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class is StepClass.Shared or StepClass.Uniq or StepClass.Raw)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A planned change of a holder's state: cleared, replaced or joined with a value and the Loans issued with it (born at the
    /// location, so in no state before it), from a Ref (Issue) or a flow.</summary>
    private readonly record struct Entry(int Slot, int Value, int Own, byte Op, bool Issue);

    // The forward problem over holder slots: E and the flow holders stored at heads, each slot a set.
    private struct Carrying(MirBorrows owner, ulong[] state) : IMirFlow
    {
        internal bool Checks { get; set; }

        public readonly void Entry() => owner.Initial(state);

        public readonly void Block(int block) => owner.Carry(state, block, this.Checks);

        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => owner.Join(target, incoming);
    }
}
