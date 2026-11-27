// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Transfers (SPEC 14.5, 16.2.2): a transfer branches at run time to its target and, for the checks only, to the code after it (CheckTarget,
// SPEC 14.10.3, plan 5.6.3), its result secured where it is evaluated (Secure); the constructs a transfer may target, opened and closed
// around their bodies. SPEC 13.7.1, 14.10.3 (DECISIONS R4-M62): only a value is delivered into an overwrite, so a construct whose value
// an overwrite takes (the assignment's right side, a body's value or a transfer's operand delivered to such a construct, a short-circuit's
// operand there) joins the ends and deliveries that have no value at a block of its own (Target.Apart), which its consumer takes on
// (AssignApart, TakeLeft): they leave the scopes through that block's drop tree and go around the overwrite's Drop and Use. Every other
// construct joins them with its values (a first placement, whose destination no value's construct can touch: MirInit keeps its state).
internal sealed partial class MirBuilder
{
    private readonly List<Target> targets = []; // The constructs around the code being built that a transfer may target, innermost last.
    private readonly List<(int Block, SourceInfo Info)> ends = []; // The open constructs' non-value ends, innermost last, which Close ends.
    private MirPlace? apart; // The Place whose consumer takes its value-less paths apart (AssignApart), which a construct built into it reads (Wanted).
    private (int Block, bool Valueless) left = (-1, false); // What the form built last left for its consumer (Hand), which it takes (TakeLeft).

    // The Place of a derived jump's operand (Transfer): a Place its Adjust would acquire (bare: Copied, moved, Reborrowed or by case)
    // designated in place, a local's or a static's, any other operand built; none for a constant.
    private MirPlace? DerivedOperand(Koto operand)
    {
        var adjust = HirTables.AdjustOf(operand);
        if (adjust is { Adaptation: HirAdaptation.None, Acquire: HirAcquire.Copy or HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase })
        {
            return this.Place(operand);
        }

        var given = this.Operand(operand, expected: null);
        return given.Kind == MirOperandKind.Const ? null : given.Place;
    }

    // SPEC 14.5, 16.2.2, 15.6.7: a transfer evaluates and secures its operand, then branches: at run time it ends the borrows of the calls
    // and comparisons it abandons (its operands pending since its target began), places its result and goes on through its target's drop
    // tree to the target its plan row names (a function's return block, built at its first return); for the checks it goes on to the code
    // after it, where those calls continue with their reservations and the comparisons it abandons have ended their Inspection Loans (SPEC
    // 14.10.3, 13.4: the pending left operand of each opened since its target began). SPEC 14.10.3: an explicit source counts in
    // unreachable code, so an operand that does not complete leaves an assumed delivery, secured by none, through the drop tree to the
    // target's join (Join), which places the operand's value as Set places it; into a target that takes its value-less paths apart
    // (Target.Wants), a delivery of an operand with no value path (TakeLeft) places nothing and goes on at the target's Apart, and an
    // operand whose form left value-less paths (Splits, AssignApart) delivers them there too, through the drop tree, and they go on after
    // the transfer as its value paths do.
    private void Transfer(JumpKoto jump)
    {
        var plan = HirTables.PlanOf(jump).Plan;
        var operand = jump.Expression;
        if (plan is null)
        {
            // The published target row is null exactly where Binding rejected the jump (no valid target, or a #switch in its function part
            // selected no arm; DECISIONS R5-M31, R5-M42), so the jump is derived from that record (SPEC 23.1.2): its operand is built,
            // keeping its own records, and the jump is an Opaque value of what the operand gave or designates, located (none for a
            // constant), without a record. No consumer takes the operand, so a Place it designates is not acquired (an acquisition is
            // its consumer's, DECISIONS BU-27).
            var first = this.located.Count;
            if (operand is not null && this.DerivedOperand(operand) is { } designated)
            {
                this.located.Add((operand, designated));
            }

            this.Opaque(jump, this.Temp(jump));
            CollectionsMarshal.SetCount(this.located, first);
            return;
        }

        var index = this.targets.Count - 1;
        while (index >= 0 && !ReferenceEquals(this.targets[index].Node, plan))
        {
            index--;
        }

        if (index < 0)
        {
            // A published target no construct around the jump opened, though every target construct opens one: a broken HIR contract.
            this.Invariant(jump);
            return;
        }

        var continues = HirTables.Op(jump) == HirOp.Continue;
        MirOperand value = default;
        var rest = (Block: -1, Valueless: false); // The operand's value-less paths (SPEC 14.10.3), as its form left them (TakeLeft).
        if (!continues && operand is not null && this.targets[index].Wants && this.Splits(operand))
        {
            var held = this.Held(operand, this.body.TypeOf(this.targets[index].Destination), null);
            rest = this.AssignApart(held, operand);
            value = MirOperand.Move(this.Made(held));
        }
        else if (!continues)
        {
            value = operand is null ? this.UnitOperand() : this.Operand(operand, this.body.TypeOf(this.targets[index].Destination));
            rest = operand is null ? rest : this.TakeLeft(operand); // Built into a temp of its own, which wants nothing (Wanted).
        }

        var assumed = operand is not null && !Completes(operand);
        var valueless = assumed && this.targets[index].Wants && rest.Valueless; // A delivery that goes on at the target's Apart.
        var secures = !continues && !assumed; // Whether it has a result to secure (SPEC 16.2.2): an assumed delivery secures none.
        value = secures ? this.Secure(value, operand ?? jump) : value;
        var info = this.Info(jump);
        ref var target = ref this.Reach(index);
        target.Joined |= !continues && !valueless;
        var inner = info with { StatementStart = false };
        if (secures && !target.Delivers && this.Departs(target.At.Depth))
        {
            // SPEC 16.2.2: the secured result is delivered after the Scope Exit of the scopes left: the target's Destination becomes a
            // result temp in the target's scope, with no storage of its own, and its Break the block that delivers that temp to the old
            // Destination, which every later transfer to it takes, as they share its drop tree (DECISIONS R5-M50); delivered into _0, it
            // is the body's result in transit (MirLocalRole.Result, DECISIONS R4-M132). The temp and its delivery are the target's,
            // at its source, not the first transfer's. The temp has the Destination's Type: none for a failed result's _0 (PLAN G1073).
            var at = new SourceInfo(SourceRef.Of(target.Node), target.At.Scope, StatementStart: false);
            var role = target.Destination is { Root: 0, Projection: 0 } ? MirLocalRole.Result : MirLocalRole.None;
            var result = new MirPlace(this.body.AddLocal(new(this.body.TypeOf(target.Destination), MirLocalKind.Temp, at, Role: role)));
            var deliver = this.body.AddBlock();
            this.body.Push(deliver, MirStatement.Assign(target.Destination, MirRvalue.Use(this.body.AddOperand(MirOperand.Move(result))), at));
            this.body.Terminate(deliver, MirTerminator.Goto(target.Break, at));
            (target.Destination, target.Break) = (result, deliver);
        }

        var (place, join) = target.Delivery(completes: !assumed);
        if (valueless)
        {
            join = this.Aside(index); // A delivery of no value places nothing: it goes on apart from the deliveries of a value.
        }

        var mark = target.At.Mark;
        var entry = this.Exit(target.At.Depth, continues ? target.Continue : join);
        var runtime = this.Abandon(continues ? -1 : this.body.AddBlock(), mark, inner); // The runtime edge's first block, which places the result.

        if (!continues && assumed && !valueless)
        {
            // SPEC 14.10.3: an assumed delivery places its operand's value as Set places any: nothing of the Never value, and of a
            // construction holding a Never part its other parts (MirMoveData's Never parts), into the join's own Destination (Into),
            // before the Scope Exit, which no cleanup there can observe (DECISIONS R4-M175).
            (var at, this.current) = (this.current, runtime);
            this.Set(place, MirRvalue.Use(this.body.AddOperand(value)), jump);
            this.current = at;
        }
        else if (secures)
        {
            this.body.Push(runtime, MirStatement.Assign(place, MirRvalue.Use(this.body.AddOperand(value)), inner));
            if (index == 0 && this.constructed is not null)
            {
                this.RequestExit(runtime, inner); // A constructor's return.
            }
        }

        if (runtime >= 0)
        {
            this.body.Terminate(runtime, MirTerminator.Goto(entry, inner));
            entry = runtime;
        }

        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(entry, info, next));
        this.current = next;
        foreach (var at in this.inspections)
        {
            // The assumed continuation, which this transfer's check edges alone enter (the operand's value-less paths' too), begins after
            // the abandoned comparisons' Loans end: A5's flow clears each operand's root, so the comparison still built there reads it
            // carrying no Loan (SPEC 14.10.3, 13.4).
            if (at >= mark && this.Lent(this.arguments[at]))
            {
                this.body.Push(next, MirStatement.Of(MirStatementKind.EndBorrow, this.arguments[at].Place, inner));
            }
        }

        if (rest.Block >= 0)
        {
            // The operand's value-less paths: an assumed delivery of nothing, which abandons the same calls, through the drop tree to the
            // target's Apart, and on after the transfer.
            var aside = this.Exit(target.At.Depth, this.Aside(index));
            var abandoned = this.Abandon(-1, mark, inner);
            if (abandoned >= 0)
            {
                this.body.Terminate(abandoned, MirTerminator.Goto(aside, inner));
                aside = abandoned;
            }

            this.body.Terminate(rest.Block, MirTerminator.Goto(aside, inner, next));
        }

        if (secures && value.Kind == MirOperandKind.Move && this.body.Locals[value.Place.Root] is var secured &&
            this.MayNeedDrop(secured.Type))
        {
            // SPEC 14.10.3, 16.2.2: on the assumed path the transfer completes after its operand is evaluated and before any cleanup, so
            // the secured operand is delivered there too, not destroyed: where its Type may need drop (Created's rule), moved into a sink
            // that no cleanup destroys, with no storage of its own (as a result temp, DECISIONS R5-M50) and the operand's source, never
            // into the target's Destination, which the continuation would then hold initialized.
            var sink = new MirPlace(this.body.AddLocal(new(secured.Type, MirLocalKind.Temp, secured.Source)));
            this.body.Push(this.current, MirStatement.Assign(sink, MirRvalue.Use(this.body.AddOperand(value)), inner));
        }
    }

    // SPEC 16.2.2, 12.2: a value secured where it is evaluated, a transfer's result before it branches and an operand before a later
    // operand of its operation builds code (Builds): a constant or a temp as it is, else acquired into a temp of its Type (none for a value
    // of no Type, PLAN G1073).
    private MirOperand Secure(in MirOperand value, Koto node)
    {
        if (value.Kind == MirOperandKind.Const || (value.Kind == MirOperandKind.Move && this.IsTemp(value.Place)))
        {
            return value;
        }

        var temp = this.Temp(this.TypeOf(value), node);
        this.Use(temp, value, node);
        return MirOperand.Move(temp);
    }

    // SPEC 16.2.2, 15.6.7: on its way to its target (its runtime edge) a transfer ends the borrows of the calls and comparisons it
    // abandons, the reference temps among the operands pending since its target began (mark), in a block, which is built at the first of
    // them where none is given (-1).
    private int Abandon(int block, int mark, in SourceInfo info)
    {
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.Lent(this.arguments[i]))
            {
                block = block < 0 ? this.body.AddBlock() : block;
                this.body.Push(block, MirStatement.Of(MirStatementKind.EndBorrow, this.arguments[i].Place, info));
            }
        }

        return block;
    }

    // A pending operand whose Loans a transfer ends: a reference temp the invocation takes (MirValidator's EndBorrowLocal).
    private bool Lent(in MirOperand pending)
        => pending.Kind == MirOperandKind.Move && this.IsTemp(pending.Place) && MirTypes.IsReference(this.body.TypeOf(pending.Place));

    private bool IsTemp(MirPlace place) => place is { Projection: 0, Root: >= 0 } && this.body.Locals[place.Root].Kind == MirLocalKind.Temp;

    // The target at an index, its join built: a function's return block is built where a transfer first reaches it, at the declaration.
    private ref Target Reach(int index)
    {
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[index];
        if (target.Break < 0)
        {
            this.body.Terminate(
                target.Break = target.Join = this.body.AddBlock(),
                MirTerminator.Return(new(SourceRef.Of(this.declaration), 0, StatementStart: false)));
        }

        return ref target;
    }

    // Opens a construct a transfer may target, with the block after it, where its exits go, and for the checks only its non-value ends
    // where it is a Never form (SPEC 14.10.3).
    private int Open(Koto node, MirPlace destination, int next = -1)
    {
        var after = this.body.AddBlock();
        var wants = !IsNeverForm(node) && this.Wanted(destination);
        this.targets.Add(new(
            node, destination, after, next, IsNeverForm(node) ? after : -1, new(this.arguments.Count, this.scopes.Count, this.scope, this.ends.Count)) { Wants = wants });
        return after;
    }

    // The current block's edge into the innermost construct's join, which it marks Joined: a completing or value end, an implicit
    // completion on a runtime path or a for's exhaustion, as a delivery is (Transfer).
    private void Joins(in MirTerminator edge)
    {
        this.body.Terminate(this.current, edge);
        CollectionsMarshal.AsSpan(this.targets)[^1].Joined = true;
    }

    // Closes the innermost construct, building on after it, its value-less paths handed to its consumer (Hand). SPEC 14.10.3: an
    // assumed path adds no normal completion, so its non-value ends go on to its Check; that check target, -1 for none, a loop's head
    // takes too. A construct no value joins is a Never form (SPEC 14.9.2), which Binding publishes so (BU-101), or one whose every
    // value-less end and delivery went on at its Apart: one that is neither breaks the HIR contract, and its ends join, so the body
    // stays valid and the record is the only one.
    private int Close(int after)
    {
        var target = this.targets[^1];
        var check = target.Check;
        if (!target.Joined && check < 0 && target.Apart < 0)
        {
            this.Record(target.Node, DiagnosticCode.InternalInvariant_Kd);
            check = after;
        }

        for (var i = target.At.Ends; i < this.ends.Count; i++)
        {
            this.body.Terminate(this.ends[i].Block, MirTerminator.Unreachable(this.ends[i].Info, check));
        }

        this.ends.RemoveRange(target.At.Ends, this.ends.Count - target.At.Ends);
        this.targets.RemoveAt(this.targets.Count - 1);
        this.Hand(after, target.Apart, target.Joined, this.Info(target.Node));
        return check;
    }

    // SPEC 14.10.3: a form closes at its join, where building goes on, and hands the block its value-less paths were left at (-1 for
    // none) to its consumer, which takes it right after it (TakeLeft). SPEC 14.9.2 gives a form a Type also where no path holds its value
    // (an `and` of two Never operands is a bool): where no value reaches the join (valued), those paths are its whole continuation,
    // which goes on at the join, only the checks reaching it, and the form is value-less, which its consumer takes as a Never form.
    private void Hand(int join, int aside, bool valued, in SourceInfo info)
    {
        if (!valued && aside >= 0)
        {
            this.body.Terminate(aside, MirTerminator.Goto(join, info));
        }

        (this.current, this.left) = (join, valued || aside < 0 ? (aside, false) : (-1, true));
    }

    // Whether a Place's consumer takes its value-less paths apart: the Place AssignApart builds into (an overwrite's value, a wanted
    // short-circuit's left operand or a transfer's operand into a wanting target), or the delivery place of the innermost target
    // where that wants (a body's value in tail position).
    private bool Wanted(MirPlace destination)
        => destination == this.apart || (this.targets.Count > 0 && this.targets[^1].Wants && destination == this.targets[^1].Ending.Place);

    // SPEC 14.10.3, 13.7.1 (DECISIONS R4-M62): a value built into a Place whose consumer takes the value's value-less paths apart, as
    // Assign builds it, and what its form left (TakeLeft). The hand-off's one set-up and read-back: a construct built into the Place
    // wants (Wanted, Open) and leaves those paths at its Apart where it closes (Close, Hand).
    private (int Block, bool Valueless) AssignApart(MirPlace place, Koto value)
    {
        var outer = this.apart;
        (this.apart, this.left) = (place, (-1, false));
        this.Assign(place, value);
        this.apart = outer;
        return this.TakeLeft(value);
    }

    // What the form built last left for its consumer, which takes it once, right after it: the block its value-less paths go on at
    // (Hand), -1 for none, and whether the form has no value path: a Never form, or one no value reaches the join of (Hand).
    private (int Block, bool Valueless) TakeLeft(Koto form)
    {
        var rest = this.left;
        this.left = (-1, false);
        return (rest.Block, rest.Valueless || IsNeverForm(form));
    }

    // Whether a transfer's operand is a form that delivers its value as it is built (InPlace: no literal, no Place an expansion located),
    // so its value-less paths can be told from the paths that hold the value (AssignApart); a Never form has no value path at all.
    private bool Splits(Koto operand)
        => !IsNeverForm(operand) && InPlace(HirTables.AdjustOf(operand)) && HirTables.Op(operand) != HirOp.Literal && this.Located(operand) is null;

    // The block a target's value-less ends and deliveries go on at, apart from its join, built at the first of them. By index, as
    // building a cleanup may grow the targets.
    private int Aside(int index)
    {
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[index];
        return target.Apart >= 0 ? target.Apart : target.Apart = this.body.AddBlock();
    }

    // The innermost target's index where a node is the body its owner is building (Target.Body), else -1: while a body is built, its
    // target is the innermost, as every owner opens its target before its bodies and a nested construct closes its own before they end.
    private int EndingTarget(Koto node) => this.targets.Count > 0 && ReferenceEquals(this.targets[^1].Body, node) ? this.targets.Count - 1 : -1;

    // Whether leaving the scopes from a depth cleans anything up: a departing scope registered a cleanup.
    private bool Departs(int depth)
    {
        for (var index = depth; index < this.scopes.Count; index++)
        {
            if (this.scopes[index].Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // A construct a transfer may target (SPEC 14.5.2): where its result goes, its join, the block after it (-1: the function's return
    // block, not built yet, Reach), its next iteration's block, where a non-value end of its body goes on for the checks only (Check: a
    // construct's join where it is a Never form, SPEC 14.10.3, a defer body's rest of its Scope Exit; -1 for none) and the builder's
    // position where it opened; whether an edge joins it.
    private record struct Target(Koto Node, MirPlace Destination, int Join, int Continue, int Check, Opening At)
    {
        // The block a delivery goes to: its Join, or the block delivering a result temp to the Destination after a Scope Exit (Delivers).
        internal int Break { get; set; } = Join;

        // Whether its Destination and Break are a result temp and the block delivering it after a Scope Exit (SPEC 16.2.2).
        internal readonly bool Delivers => this.Break != this.Join;

        // Whether a value reaches its join: a delivery of one, assumed too, or another edge Joins builds; a delivery or an end without a
        // value goes on at its Apart instead.
        internal bool Joined { get; set; }

        // Whether its consumer takes its value-less paths apart (an overwrite's value, a body's value or a transfer's operand delivered to
        // such a target), and the block they go on at, -1 until one is built (Aside).
        internal bool Wants { get; init; }

        internal int Apart { get; set; } = -1;

        // Its own Destination, which an assumed delivery places into: Destination before a delivery made it a result temp (Delivers).
        internal MirPlace Into { get; } = Destination;

        // The body of it its owner is building now, which TargetBody and Iterate set (SetBody) and the routes to a block read
        // (EndingTarget); none between its bodies, and none for a defer body, always a block, which Deferred builds as its target's
        // directly (TargetBlock).
        internal Koto? Body { get; set; }

        // Where its body's end places its value and goes on (End): the delivery its builder took for the body's last item, before
        // building it (TargetBody, TargetBlock), so a departing exit inside that item, which makes the Destination a result temp, does
        // not split it.
        internal (MirPlace Place, int Continuation) Ending { get; set; } = (Destination, Join);

        // SPEC 16.2.2, 14.10.3 (DECISIONS R4-M175): a delivery's place and continuation: one that completes goes into the Destination,
        // a result temp after a departing exit, and on at its Break; an assumed one goes into Into and on at the Join.
        internal readonly (MirPlace Place, int Continuation) Delivery(bool completes) => completes ? (this.Destination, this.Break) : (this.Into, this.Join);
    }

    // The builder's position where a construct opened: the pending operands below its transfers (Mark), the open scopes around it (Depth),
    // the MIR scope it opened in, which its delivered result temp takes (SPEC 16.2.2), and its first non-value end (Ends).
    private readonly record struct Opening(int Mark, int Depth, int Scope, int Ends);
}
