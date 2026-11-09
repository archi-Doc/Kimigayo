// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's storing rule (PLAN G581-1; SPEC 5.2.1 Retained dependencies, 15.6.4 rule A; the owner's O-56 (a), O-58 (a), O-62 (a), O-72 (a),
// O-110 (a)): a body stores through raw storage reached from its inputs only values its own public contract lets the
// call retain there. It reads the direct stores of a safe Fn or Test body whose contract writes an untied storage, on two must-facts;
// where either is lost it says nothing (O-62 (a)).
// Input: the body's own CallRetention (TyCtxt.Retains over its declaration), its records (Own, the Reached entry of a storage's erased
// case Type, All), the exposed holders (MirBorrows.Loans' Exposed) and the checks replay's chains (MirDataflow.Head).
// The value fact, M(h): by holder slot, the elements its whole value certainly holds, a MirLoanSets set in a namespace of its own (never
// joined with σ): Own(i, part), input i's own part, an address or borrow of the storage i's layer at `part` refers to (a parameter's own
// pointer is the layer at the root, part 0; a by-value input's layer is the part a Place names before its first layer Deref); Past(i), a
// pointer into storage past i's referent. Chain-local: ∅ at every chain head but each input's Own(i, 0) (a parameter is let-like,
// SPEC 7); a whole store keeps the value's M (∅ into an exposed holder), any other change of a holder, a call result and a part read of a
// local give ∅. A Ref or raw address gives its landing's element; a read of a pointer stored at depth d of i's region gives Past(i); a read
// of a by-value input's pointer part gives Own(i, part).
// The landing fact (Redirects, Landing; O-72 (a)'s second must-fact): a Place rooted at an unexposed, non-receiver input i with d layer
// Derefs lands in the storage at depth d of i's region (depth 1: what i's layer at `part` refers to; deeper: past i's referent) only while
// no pointer layer it reads was redirected: Redirects, one pass per body view, records the sites that may write a pointer layer of i's
// region, each at the least depth it may write (a store into a Place of i's region whose Type may hold a pointer; a borrow or Reborrow
// of a Place of it, not shared, or a Move of a pointer read from it, which may write at that depth later), and depth 1 for every input
// where a call or a drop takes a value that may hold a pointer, a store or borrow goes through a raw Place, or an Opaque site rests on an
// error; at a location of the replay's chain the least depth counts every other chain's site and the chain's own sites before it (on a
// chain's first run no later site of it has run, and a store that breaks the contract then breaks it: PLAN G581-2);
// a landing at depth d holds while that least depth is d or more. A local root lands only through its own one element and one Deref.
// The rule (Storing, at Flow.Store in the checks replay): a store in safe code whose destination lands past input i (depth 2 or more),
// or at depth 1 in the storage of another layer of i than the value's own part, whose value's M holds Own(i, ·), where i's record for
// that storage (past: the Reached entry of its erased case Type, else All; depth 1: Own; a depth-1 part reached past the layer through
// an own step, an object payload, is other storage, SPEC 5.2.1) is untied (Binding: a raw layer stepping
// Unstated or Static is written there, joined over the storage) and gives i neither Value nor a pair: UnretainedRawStore_Kd at the
// store, with no fact (MirBorrows.Locations; DECISIONS Q13: an Anchor reads as a Loan's, and an own part formed by `@raw` has none).
// At a Call (CallStoring, PLAN G581-2), the same rule over the storage the callee's contract may write through an argument whose
// referent certainly lands in i's region (its Own record there, the Reached entries past it), for each argument it lets by Value.
// Never where the value's own part is the destination's layer (a self-link, O-56 (a)).
// Not read (silence, named limits): a store in an `unsafe` scope (the author's obligation, SPEC 5), Closure, accessor, default-template
// and static-initializer bodies, a Closure's call receiver, a value or pointer whose fact a join, loop head, part, call result, read-back or
// exposure lost (a must-store of a call, `exchange`'s, is no fact), a landing a redirect before it may have moved, and a record Binding
// joins over a storage where another position lets i by Value.
// Must not decide retention (TyCtxt.Retains), read a raw layer's Origin, or decide by the syntax of a Type.
internal sealed partial class MirBorrows
{
    private readonly List<(int Input, int Part)> ownKeys = []; // Own(i, part)'s keys: key k is element 2 (locals + k).
    private readonly Dictionary<(int Input, int Part), int> ownIndex = [];
    private readonly List<(int Block, int Index, int Input, int Depth)> sites = []; // Redirects' sites in block order; Input 0: every input.
    private int[] must = []; // By slot: M(h), in the checks replay (chain-local).
    private int[] redirected = []; // By local: the least depth at which a pointer layer of the input's region may have been written (replay).
    private int[] firstSite = []; // By block: its first site.
    private (int Least, int Head, int Other)[] outside = []; // By local: the least site depth, that site's chain head, the least of other chains'.
    private int inputs; // The body's parameters, locals 1 to inputs.
    private int siteCursor; // Redirected's next site.
    private MirStep[] mustSteps = []; // Landing's and Redirects' step walk.
    private CallRetention? contract; // The body's own retention where the rule reads the body, else null.
    private int mustValue; // M of the value the location's Assign stores (Plan), read by the rule.
    private int mustKept; // What that store gives its whole destination: mustValue, or ∅ for an exposed one (Commit).
    private int overstored; // The input local whose own value the location's store puts where the contract refuses it, or 0 (Check).
    private bool replaying; // Whether Carry runs the checks replay.
    private bool owning; // Whether the body forms an input's own part without a Loan (a raw address, a by-value pointer part): Run's producer.

    private static int Past(int input) => (2 * input) + 1;

    // A depth-1 part reached past the layer through an own step (an object payload; SPEC 5.2.1: other storage, never a self-link): a key
    // below -1, kept once marked.
    private static int OwnStep(int part, bool owned) => owned && part >= 0 ? -2 - part : part;

    // A layer Deref: through a borrow or a raw pointer, not an own step.
    private static bool Layer(in MirStep step)
        => step.Kind == MirProjectionKind.Deref && step.Class is MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw;

    // Whether a record refuses an input's own value: the storage is untied and neither Value nor a pair names the input.
    private static bool Refuses(StorageRetention record, int slot)
    {
        if (!record.Untied || (slot < record.Kinds.Length && (record.Kinds[slot] & RetentionKind.Value) != 0))
        {
            return false;
        }

        foreach (var pair in record.Pairs)
        {
            if (pair.Input == slot)
            {
                return false;
            }
        }

        return true;
    }

    // Whether a contract writes an untied storage past an input.
    private static bool WritesUntied(CallRetention retention)
    {
        foreach (var written in retention.Writes)
        {
            if (written.All.Untied || written.Own.Untied)
            {
                return true;
            }
        }

        return false;
    }

    // Own(i, part)'s element.
    private int Own(int input, int part)
    {
        if (!this.ownIndex.TryGetValue((input, part), out var key))
        {
            (key, this.ownIndex[(input, part)]) = (this.ownKeys.Count, this.ownKeys.Count);
            this.ownKeys.Add((input, part));
        }

        return 2 * (this.body.Locals.Length + key);
    }

    // The body's own contract, where the rule reads the body: a Fn or Test body whose signature bound, with an input whose case Type may hold
    // a raw layer, whose contract is no Limit and writes an untied storage; else none. Then the landing fact (Redirects).
    private void BeginStoring()
    {
        (this.contract, this.overstored, this.owning) = (null, 0, false);
        this.ownKeys.Clear();
        this.ownIndex.Clear();
        if (this.body.Key is not { Kind: MirBodyKind.Fn or MirBodyKind.Test, Decl: { } declaration } || !this.ReadsRaw(declaration) ||
            types.Retains(declaration, null, declaration) is not { IsLimit: false } retention || !WritesUntied(retention))
        {
            return;
        }

        this.contract = retention;
        this.Redirects();
    }

    // Whether a function's signature bound (a failed one has no contract) and an input of the body may hold a raw layer.
    private bool ReadsRaw(BindingSymbol function)
    {
        var complete = TyCtxt.Signature(function, null, -1, out var count) is not null;
        for (var slot = 0; complete && slot < count; slot++)
        {
            complete = TyCtxt.Signature(function, null, slot, out _) is not null;
        }

        for (var i = 1; complete && i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (MirTypes.HoldsRaw(cases.Type(this.body.Locals[i].Type), this.seen))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a root is a parameter.
    private bool IsInput(int root) => root >= 1 && this.body.Locals[root].Kind == MirLocalKind.Arg;

    // Whether a local is an input the rule names: a parameter, not a destruction receiver, not exposed.
    private bool StoringInput(int local)
        => local >= 1 && this.body.Locals[local] is { Kind: MirLocalKind.Arg, Role: not MirLocalRole.Receiver } && !this.Exposed(local);

    // The landing fact's sites: per location, the least depth at which it may write a pointer layer of an input's region (depth 1 for
    // every input where it may write any). The replay counts, at a location of a chain, every site of the other chains (any of them may
    // have run before) and the chain's own sites before it: on the chain's first run nothing later in it has run, and a fact lost on a later
    // run is no fact the rule needs, since a store that breaks the contract on the first run breaks it (PLAN G581-2).
    private void Redirects()
    {
        this.sites.Clear();
        MirDataflow.Grow(ref this.firstSite, this.body.Blocks.Length);
        this.inputs = 0;
        while (this.inputs + 1 < this.body.Locals.Length && this.IsInput(this.inputs + 1))
        {
            this.inputs++;
        }
        if (!this.body.OpaqueRoots.IsEmpty)
        {
            this.sites.Add((-1, 0, 0, 1)); // An Opaque site: before every location.
        }

        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            this.firstSite[block] = this.sites.Count;
            for (var s = 0; s < data.Count; s++)
            {
                ref readonly var statement = ref this.body.Statements[data.Start + s];
                if (statement.Kind != MirStatementKind.Assign)
                {
                    continue;
                }

                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                var all = this.PointerType(this.body.TypeOf(statement.Place)) ? this.Redirect(block, s, statement.Place, 0) : this.ThroughRaw(statement.Place);
                if (this.Reborrowing(statement) is var mode and not MirRefKind.Raw)
                {
                    all |= mode != MirRefKind.Shared && this.Redirect(block, s, this.body.Operands[rvalue.Start].Place, 1);
                }
                else if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op is not (MirRefKind.Shared or MirRefKind.Raw))
                {
                    all |= this.Redirect(block, s, rvalue.Place, 0);
                }
                else if (rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage)
                {
                    this.owning |= this.StoringInput(rvalue.Place.Root) && this.Depth(rvalue.Place) > 0; // A raw address of an input's region.
                }

                for (var o = rvalue.Start; rvalue.Kind is not (MirRvalueKind.Ref or MirRvalueKind.Storage) && o < rvalue.Start + rvalue.Count; o++)
                {
                    if (this.body.Operands[o] is { Kind: not MirOperandKind.Const, Place: var read } && this.IsInput(read.Root) &&
                        this.PointerType(this.body.TypeOf(read)))
                    {
                        this.owning |= read.Projection != 0 && this.StoringInput(read.Root) && this.Depth(read) == 0; // A by-value input's pointer part.
                        all |= this.body.Operands[o].Kind == MirOperandKind.Move && this.Redirect(block, s, read, 1); // It may write where it points.
                    }
                }

                this.Every(block, s, all);
            }

            var terminator = data.Terminator;
            if (terminator.Kind == MirTerminatorKind.Call)
            {
                var all = this.PointerType(this.body.TypeOf(terminator.Place)) ? this.Redirect(block, data.Count, terminator.Place, 0) : this.ThroughRaw(terminator.Place);
                all |= terminator.Callee.Kind == MirCalleeKind.Value && terminator.Operand.Kind != MirOperandKind.Const;
                for (var o = terminator.Start; !all && o < terminator.Start + terminator.Count; o++)
                {
                    all = this.body.Operands[o] is { Kind: not MirOperandKind.Const, Place: var argument } && this.PointerType(this.body.TypeOf(argument));
                }

                this.Every(block, data.Count, all);
            }
            else if (terminator.Kind == MirTerminatorKind.Drop)
            {
                this.Every(block, data.Count, this.PointerType(this.body.TypeOf(terminator.Place)));
            }
        }

        // Per input, the least depth over every chain's sites, that site's chain head, and the least over the other chains' sites.
        MirDataflow.Grow(ref this.outside, this.body.Locals.Length);
        Array.Fill(this.outside, (int.MaxValue, -2, int.MaxValue), 0, this.body.Locals.Length);
        foreach (var (block, _, input, depth) in this.sites)
        {
            var head = block < 0 ? -1 : flow.Head(block); // An Opaque site lies in no chain.
            for (var i = input == 0 ? 1 : input; i <= (input == 0 ? this.inputs : input); i++)
            {
                ref var row = ref this.outside[i];
                if (depth < row.Least && head == row.Head)
                {
                    row.Least = depth; // A lower site of the same chain: the other chains' least stays.
                }
                else if (depth < row.Least)
                {
                    row = (depth, head, row.Least); // A lower site of another chain: the old least becomes the other chains'.
                }
                else if (head != row.Head)
                {
                    row.Other = Math.Min(row.Other, depth);
                }
            }
        }
    }

    // A site at every input (depth 1) where a location may write any pointer layer.
    private void Every(int block, int index, bool all)
    {
        if (all)
        {
            this.sites.Add((block, index, 0, 1));
        }
    }

    // A write that may redirect a pointer layer of an input's region, at a Place `extra` Derefs short of the depth it writes: a site at the
    // input; true where it goes through a raw Place (ThroughRaw).
    private bool Redirect(int block, int index, MirPlace place, int extra)
    {
        if (this.ThroughRaw(place))
        {
            return true;
        }

        if (this.IsInput(place.Root))
        {
            this.sites.Add((block, index, place.Root, this.Depth(place) + extra));
        }

        return false;
    }

    // After a location of the checks replay, which visits a block's locations in order: its sites (Redirects), by a cursor over the
    // block's sites, which lie in location order.
    private void Redirected(int block, int index)
    {
        this.siteCursor = index == 0 ? this.firstSite[block] : this.siteCursor;
        for (; this.siteCursor < this.sites.Count && this.sites[this.siteCursor] is { Index: var at } site && site.Block == block && at == index; this.siteCursor++)
        {
            for (var i = site.Input == 0 ? 1 : site.Input; i <= (site.Input == 0 ? this.inputs : site.Input); i++)
            {
                this.redirected[i] = Math.Min(this.redirected[i], site.Depth);
            }
        }
    }

    // Whether a Place goes through a raw Place, where a pun may write any pointer's bytes (SPEC 5.2), so no input's region is told apart.
    private bool ThroughRaw(MirPlace place)
    {
        var count = place.Root < 0 ? 0 : this.placeSteps.Walk(this.body, place, reborrow: false, ref this.mustSteps);
        for (var i = 0; i < count; i++)
        {
            if (this.mustSteps[i].Class == MirStepClass.Raw)
            {
                return true;
            }
        }

        return false;
    }

    // Whether a value of a case Type may hold a pointer (MirTypes.HoldsLoans, a raw layer included).
    private bool PointerType(BoundType? type) => type is not null && this.Holds(cases.Type(type), out _);

    // A chain head of the checks replay: every M is ∅ but each input's, Own(i, 0); each input's landing takes the other chains' sites.
    private void EnterStoring(int head)
    {
        MirDataflow.Grow(ref this.must, this.holderCount);
        MirDataflow.Grow(ref this.redirected, this.body.Locals.Length);
        Array.Clear(this.must, 0, this.holderCount);
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            this.redirected[i] = this.outside[i].Head == head ? this.outside[i].Other : this.outside[i].Least;
            if (this.slots[i] >= 0 && this.StoringInput(i))
            {
                this.must[this.slots[i]] = this.sets.Add(0, this.Own(i, 0));
            }
        }
    }

    // M of an rvalue: a value-keeping rvalue's operand's (Keeps; a pointer cast to another Type keeps Own but not Past, whose storage the
    // rule reads by the pointer's pointee Type), a Ref's or Storage's landing (Own at depth 1, Past deeper), an aggregate's operands' union;
    // none for any other.
    private int MustOf(in MirRvalue rvalue)
    {
        if (this.Keeps(rvalue))
        {
            var kept = this.MustOperand(rvalue.Start);
            if (rvalue is not { Kind: MirRvalueKind.Cast, Type: { } to } || ReferenceEquals(types.Erase(cases.Type(to)), types.Erase(this.TypeOf(rvalue.Start)!)))
            {
                return kept;
            }

            var owns = 0;
            foreach (var element in this.sets.Elements(kept))
            {
                owns = (element & 1) == 0 ? this.sets.Add(owns, element) : owns;
            }

            return owns;
        }

        switch (rvalue.Kind)
        {
            case MirRvalueKind.Ref or MirRvalueKind.Storage:
                var (input, part, depth) = this.Landing(rvalue.Place);
                return input == 0 ? 0 : this.sets.Add(0, depth == 1 ? this.Own(input, part) : Past(input));
            case MirRvalueKind.Aggregate:
                var value = 0;
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    value = this.sets.Union(value, this.MustOperand(o));
                }

                return value;
            default:
                return 0;
        }
    }

    // M of an operand: a constant's none; a whole local's M; a by-value input's pointer part Own(i, part); a pointer read at depth d of an
    // input's region, Past(i) while depth d itself is intact; a part of a local none.
    private int MustOperand(int index)
    {
        if (this.body.Operands[index] is not { Kind: not MirOperandKind.Const, Place: var place })
        {
            return 0;
        }

        if (place is { Projection: 0, Root: >= 0 })
        {
            return this.slots[place.Root] >= 0 ? this.must[this.slots[place.Root]] : 0;
        }

        if (this.StoringInput(place.Root) && this.TypeOf(index) is var type &&
            (MirTypes.IsReference(type) || type?.Semantics == Parsing.SemanticsKind.Raw) && this.Depth(place) == 0)
        {
            return this.sets.Add(0, this.Own(place.Root, place.Projection));
        }

        var (input, _, depth) = this.Landing(place);
        return input > 0 && this.redirected[input] > depth ? this.sets.Add(0, Past(input)) : 0;
    }

    // A Place's layer Derefs.
    private int Depth(MirPlace place)
    {
        var (count, layers) = (this.placeSteps.Walk(this.body, place, reborrow: false, ref this.mustSteps), 0);
        for (var i = 0; i < count; i++)
        {
            layers += Layer(this.mustSteps[i]) ? 1 : 0;
        }

        return layers;
    }

    // Where a Place certainly lands (Input 0: unknown): at depth 1 of input i's region, in the storage its layer at `part` refers to, or
    // past i's referent (depth 2 or more, Part -1), while no redirect reaches a layer it reads (Redirects).
    private (int Input, int Part, int Depth) Landing(MirPlace place)
    {
        if (place.Root < 0)
        {
            return default;
        }

        var count = this.placeSteps.Walk(this.body, place, reborrow: false, ref this.mustSteps);
        var (first, layers, owned) = (-1, 0, false);
        for (var i = 0; i < count; i++)
        {
            first = first < 0 && Layer(this.mustSteps[i]) ? i : first;
            layers += Layer(this.mustSteps[i]) ? 1 : 0;
            owned |= first >= 0 && this.mustSteps[i] is { Kind: MirProjectionKind.Deref, Class: MirStepClass.Own };
        }

        if (this.StoringInput(place.Root))
        {
            return layers > 0 && this.redirected[place.Root] >= layers ? (place.Root, layers == 1 ? OwnStep(this.mustSteps[first].Prefix, owned) : -1, layers) : default;
        }

        if (first != 0 || layers != 1 || this.slots[place.Root] < 0 || this.sets.Elements(this.must[this.slots[place.Root]]) is not [var element])
        {
            return default;
        }

        if ((element & 1) != 0)
        {
            return (element >> 1, -1, int.MaxValue); // Past at an unknown depth: no read through it is certain (MustOperand).
        }

        var (input, part) = this.ownKeys[(element >> 1) - this.body.Locals.Length];
        return (input, OwnStep(part, owned), 1);
    }

    // The rule at a store into a Place (Flow.Store), in the checks replay: the value stored where the Place certainly lands in input i's
    // region, at the storage past its last layer Deref (its erased case Type past i's referent).
    private void Storing(MirPlace place)
    {
        if (!this.replaying || this.contract is null || this.mustValue == 0 || this.Landing(place) is not (> 0 and var input, var part, var depth) ||
            this.Unsafe())
        {
            return;
        }

        var (count, last) = (this.placeSteps.Walk(this.body, place, reborrow: false, ref this.mustSteps), -1);
        for (var i = 0; depth > 1 && i < count; i++)
        {
            last = Layer(this.mustSteps[i]) ? i : last;
        }

        var storage = last + 1 < count ? place with { Projection = this.mustSteps[last + 1].Prefix } : place;
        this.overstored = this.Overstores(this.mustValue, input, part, depth > 1 ? types.Erase(cases.Type(this.body.TypeOf(storage)!)) : null)
            ? input : this.overstored;
    }

    // The rule at a Call (PLAN G581-2), in the checks replay, on the arguments' values before it: through each argument that is one layer
    // (a borrow or a raw pointer, whose record is that of the one storage it refers to; Binding joins a by-value argument's layers into one
    // record, so any other argument is silent), whose referent certainly lands in input i's region (M: Own(i, part) at depth 1, Past(i)
    // past i's referent), the callee's Own record there and, while the landing fact proves every depth of i intact and the store is no
    // referent store, its Reached entries past it (a Reached entry has no depth, so any redirect site of i silences them): each callee
    // input it lets by Value (a pair is not read) is the value of the rule at that storage (Overstores). A Closure's call receiver is not read.
    private void CallStoring(in MirTerminator call)
    {
        if (this.planned is not { } callee || this.Unsafe())
        {
            return;
        }

        foreach (var written in callee.Writes)
        {
            var position = this.slotPositions[written.Output];
            if (position >= call.Count || this.TypeOf(call.Start + position) is not { Kind: BoundTypeKind.Semantics, Components: [var pointee, ..] } layer ||
                !(MirTypes.IsReference(layer) || layer.Semantics == Parsing.SemanticsKind.Raw))
            {
                continue;
            }

            var past = !this.referentStores.AsSpan(0, this.referentCount).Contains(position);
            foreach (var element in this.sets.Elements(this.MustOperand(call.Start + position)))
            {
                var (input, part) = (element & 1) != 0 ? (element >> 1, -1) : this.ownKeys[(element >> 1) - this.body.Locals.Length];
                this.CallStored(call, written.Own, input, part, part == -1 ? types.Erase(cases.Type(pointee)) : null);
                foreach (var (type, record) in past && this.redirected[input] == int.MaxValue ? written.Reached : [])
                {
                    this.CallStored(call, record, input, -1, type is null ? null : types.Erase(cases.Type(type)));
                }
            }
        }
    }

    // One storage a callee may write in input i's region: the rule (Overstores) for each callee input its record lets by Value.
    private void CallStored(in MirTerminator call, StorageRetention record, int input, int part, BoundType? type)
    {
        for (var slot = 0; slot < record.Kinds.Length; slot++)
        {
            var position = this.slotPositions[slot];
            if ((record.Kinds[slot] & RetentionKind.Value) != 0 && position < call.Count &&
                this.Overstores(this.MustOperand(call.Start + position), input, part, type))
            {
                this.overstored = input;
            }
        }
    }

    // The rule at one storage of input i's region (part: the layer whose referent it is at depth 1, below -1 for one reached past that
    // layer through an own step, -1 past i's referent; type: its erased case Type past i's referent, null where none is known): whether a
    // value certainly holding Own(i, ·), no self-link there (its part is that layer's, reached through no own step), goes where i's own
    // record for that storage (Own at depth 1; past i's referent the Reached entry of its Type, else All) refuses it.
    private bool Overstores(int value, int input, int part, BoundType? type)
    {
        var own = false;
        foreach (var element in this.sets.Elements(value))
        {
            var key = (element & 1) == 0 ? this.ownKeys[(element >> 1) - this.body.Locals.Length] : (Input: 0, Part: 0);
            own |= key.Input == input && (part < 0 || key.Part != part);
        }

        var slot = MirCallee.Slot(this.body.Key.Decl!, input - 1);
        foreach (var written in own ? this.contract!.Writes : [])
        {
            if (written.Output == slot && Refuses(part != -1 ? written.Own : (type is null ? null : this.ReachedEntry(written, type)) ?? written.All, slot))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the location being planned lies in an `unsafe` scope.
    private bool Unsafe()
    {
        for (int scope = this.Source(this.planLocation).Scope; scope >= 0; scope = this.body.Scopes[scope].Outer)
        {
            if (this.body.Scopes[scope].Kind == MirScopeKind.Unsafe)
            {
                return true;
            }
        }

        return false;
    }
}
