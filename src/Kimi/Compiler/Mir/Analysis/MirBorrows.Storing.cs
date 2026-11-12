// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's storing rule (PLAN G581-1; SPEC 5.2.1 Retained dependencies, 15.6.4 rule A; the owner's O-56 (a), O-58 (a), O-62 (a), O-72 (a),
// O-110 (a)): a body stores through raw storage reached from its inputs only values its own public contract lets the
// call retain there. It reads the direct stores of a safe Fn or Test body whose contract writes an untied storage, on two must-facts;
// where either is lost it says nothing (O-62 (a)).
// Input: the body's own CallRetention (TyCtxt.Retains over its declaration), its records (Own, the Reached entry of a storage's erased
// case Type, All), the exposed holders (MirBorrows.Loans' Exposed), the checks replay's chains (MirDataflow.Head) and the facts at a
// store's destination key (MirBorrows.PunKeys' RawInline and AnyStated, folded from TyCtxt.OwnLayers, the fold the pun rule reads).
// The value fact, M(h): by holder slot, the elements its whole value certainly holds, a MirLoanSets set in a namespace of its own (never
// joined with σ): Own(i, part), input i's own part, an address or borrow of the storage i's layer at `part` refers to (a parameter's own
// pointer is the layer at the root, part 0; a by-value input's layer is the part a Place names before its first layer Deref), or of
// storage an own step reaches from there (an owning handle's payload, SPEC 15.6.2: the layer's part encoded below -1, OwnStepPart);
// Past(i), a pointer into storage past i's referent. Chain-local: ∅ at every chain head but each input's Own(i, 0) (a parameter is
// let-like, SPEC 7); a whole store keeps the value's M (∅ into an exposed holder), any other change of a holder, a call result and a part
// read of a local give ∅. A Ref or raw address gives its landing's element; a read of a pointer stored at depth d of i's region gives
// Past(i); a read of a by-value input's pointer part gives Own(i, part).
// The landing fact (Redirects, Landing; O-72 (a)'s second must-fact): a Place rooted at an unexposed, non-receiver input i with d layer
// Derefs lands in the storage at depth d of i's region (depth 1: what i's layer at `part` refers to, or past it through an own step,
// which ends its storage, SPEC 5.2.1, an encoded part; deeper: past i's referent) only while no pointer layer it reads was
// redirected: Redirects, one flow-insensitive pass per body view, takes the least depth at which the body may write a pointer layer of
// i's region (a store into a Place of i's region whose Type may hold a pointer; a borrow or Reborrow of a Place of it, not shared, or a
// Move of a pointer read from it, which may write at that depth later), and depth 1 for every input where a call or a drop takes a value
// that may hold a pointer, a store or borrow goes through a raw Place, or an Opaque site rests on an error; a landing at depth d holds
// while that least depth is d or more. A local root lands only through its own one element and one Deref, past an own step where its
// element is past one or an own step follows the Deref.
// The rule (Storing, at Flow.Store in the checks replay): a store in safe code whose destination lands past input i (depth 2 or more),
// or at depth 1 in other storage than the value's own part, whose value's M holds Own(i, ·), where
// i's record for that storage (past: the Reached entry of its erased case Type, else All; depth 1: Own) is untied (Binding: a raw
// layer stepping Unstated or Static is written there, joined over the storage) and gives i neither Value nor a pair:
// UnretainedRawStore_Kd at the store, with no fact (MirBorrows.Locations; DECISIONS Q13: an Anchor reads as a Loan's, and an own part
// formed by `@raw` has none).
// Never where the value's own part is the destination's layer, reached through no own step (a self-link, O-56 (a), SPEC 5.2.1).
// The own step applies only at a destination whose own address holds raw layers none of which states an Origin (MirBorrows.PunKeys'
// RawInline and not AnyStated over TyCtxt.OwnLayers; `static` counts as stated). At any other destination (a stated, mixed or
// layerless one) a part reads as its layer's (LayerPart), the judgment before the own step (the owner's O-190 (b)): there a payload
// still reads as the layer's referent (an invalid store into a mixed destination's unstated layer is accepted, and a valid store into a
// stated layer beside an unstated one through another layer or past i is rejected, PLAN G581/G692).
// Storage a raw Field declares `during self` is reached only through a raw Place (its Deref a layer here): a store there lies in an
// `unsafe` scope, and an address of it gives Past(i).
// Not read (silence, named limits): a store in an `unsafe` scope (the author's obligation, SPEC 5), Closure, accessor, default-template
// and static-initializer bodies, a call's store (G581-2), a value or pointer whose fact a join, loop head, part, call result, read-back or
// exposure lost, a landing any redirect may have moved, and a record Binding joins over a storage where another position lets i by Value.
// Must not decide retention (TyCtxt.Retains), read a raw layer's Origin, or decide by the syntax of a Type.
internal sealed partial class MirBorrows
{
    private readonly List<(int Input, int Part)> ownKeys = []; // Own(i, part)'s keys: key k is element 2 (locals + k).
    private readonly Dictionary<(int Input, int Part), int> ownIndex = [];
    private int[] must = []; // By slot: M(h), in the checks replay (chain-local).
    private int[] redirected = []; // By local: the least depth at which the body may write a pointer layer of the input's region.
    private MirStep[] mustSteps = []; // Landing's and Redirects' step walk.
    private CallRetention? contract; // The body's own retention where the rule reads the body, else null.
    private int mustValue; // M of the value the location's Assign stores (Plan), read by the rule.
    private int mustKept; // What that store gives its whole destination: mustValue, or ∅ for an exposed one (Commit).
    private int overstored; // The input local whose own value the location's store puts where the contract refuses it, or 0 (Check).
    private bool replaying; // Whether Carry runs the checks replay.
    private bool owning; // Whether the body forms an input's own part without a Loan (a raw address, a by-value pointer part): Run's producer.

    private static int Past(int input) => (2 * input) + 1;

    // A depth-1 part reached past its layer through an own step (an object payload; SPEC 5.2.1: other storage): the layer's part
    // encoded below -1, which no layer's part equals, so it never gives a self-link.
    private static int OwnStepPart(int part, bool owned) => owned && part >= 0 ? -2 - part : part;

    // A depth-1 part as its layer's: an own step's decoded, the reading where the own step is not applied (O-190 (b)).
    private static int LayerPart(int part) => part < -1 ? -2 - part : part;

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

    // The landing fact: per input, the least depth at which the body may write a pointer layer of its region (int.MaxValue: none).
    private void Redirects()
    {
        MirDataflow.Grow(ref this.redirected, this.body.Locals.Length);
        Array.Fill(this.redirected, int.MaxValue, 0, this.body.Locals.Length);
        var all = !this.body.OpaqueRoots.IsEmpty;
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var s = data.Start; s < data.Start + data.Count; s++)
            {
                ref readonly var statement = ref this.body.Statements[s];
                if (statement.Kind != MirStatementKind.Assign)
                {
                    continue;
                }

                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                all |= this.PointerType(statement.Place) ? this.Redirect(statement.Place, 0) : this.ThroughRaw(statement.Place);
                if (this.Reborrowing(statement) is var mode and not MirRefKind.Raw)
                {
                    all |= mode != MirRefKind.Shared && this.Redirect(this.body.Operands[rvalue.Start].Place, 1);
                }
                else if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op is not (MirRefKind.Shared or MirRefKind.Raw))
                {
                    all |= this.Redirect(rvalue.Place, 0);
                }
                else if (rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage)
                {
                    this.owning |= this.StoringInput(rvalue.Place.Root) && this.Depth(rvalue.Place) > 0; // A raw address of an input's region.
                }

                for (var o = rvalue.Start; rvalue.Kind is not (MirRvalueKind.Ref or MirRvalueKind.Storage) && o < rvalue.Start + rvalue.Count; o++)
                {
                    if (this.body.Operands[o] is { Kind: not MirOperandKind.Const, Place: var read } && this.IsInput(read.Root) &&
                        this.PointerType(read))
                    {
                        this.owning |= read.Projection != 0 && this.StoringInput(read.Root) && this.Depth(read) == 0; // A by-value input's pointer part.
                        all |= this.body.Operands[o].Kind == MirOperandKind.Move && this.Redirect(read, 1); // It may write where it points.
                    }
                }
            }

            var terminator = data.Terminator;
            if (terminator.Kind == MirTerminatorKind.Call)
            {
                all |= this.PointerType(terminator.Place) ? this.Redirect(terminator.Place, 0) : this.ThroughRaw(terminator.Place);
                all |= terminator.Callee.Kind == MirCalleeKind.Value && terminator.Operand.Kind != MirOperandKind.Const;
                for (var o = terminator.Start; !all && o < terminator.Start + terminator.Count; o++)
                {
                    all = this.body.Operands[o] is { Kind: not MirOperandKind.Const, Place: var argument } && this.PointerType(argument);
                }
            }
            else if (terminator.Kind == MirTerminatorKind.Drop)
            {
                all |= this.PointerType(terminator.Place);
            }
        }

        for (var i = 1; all && i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            this.redirected[i] = Math.Min(this.redirected[i], 1);
        }
    }

    // A write that may redirect a pointer layer of an input's region, at a Place `extra` Derefs short of the depth it writes: the input's
    // least depth; true where it goes through a raw Place (ThroughRaw).
    private bool Redirect(MirPlace place, int extra)
    {
        if (this.ThroughRaw(place))
        {
            return true;
        }

        if (this.IsInput(place.Root))
        {
            this.redirected[place.Root] = Math.Min(this.redirected[place.Root], this.Depth(place) + extra);
        }

        return false;
    }

    // Whether a Place goes through a raw Place, where a pun may write any pointer's bytes (SPEC 5.2), so no input's region is told apart.
    private bool ThroughRaw(MirPlace place)
    {
        var count = place.Root < 0 ? 0 : this.placeSteps.Walk(place, reborrow: false, ref this.mustSteps);
        for (var i = 0; i < count; i++)
        {
            if (this.mustSteps[i].Class == MirStepClass.Raw)
            {
                return true;
            }
        }

        return false;
    }

    // Whether a value of a Place's case Type may hold a pointer (MirTypes.HoldsLoans, a raw layer included).
    private bool PointerType(MirPlace place) => this.Holds(cases.TypeOf(place), out _);

    // A chain head of the checks replay: every M is ∅ but each input's, Own(i, 0).
    private void EnterStoring()
    {
        MirDataflow.Grow(ref this.must, this.holderCount);
        Array.Clear(this.must, 0, this.holderCount);
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (this.slots[i] >= 0 && this.StoringInput(i))
            {
                this.must[this.slots[i]] = this.sets.Add(0, this.Own(i, 0));
            }
        }
    }

    // M of an rvalue: a value-keeping rvalue's operand's (Keeps), a Ref's or Storage's landing (Own at depth 1, Past deeper), an aggregate's
    // operands' union; none for any other.
    private int MustOf(in MirRvalue rvalue)
    {
        if (this.Keeps(rvalue))
        {
            return this.MustOperand(rvalue.Start);
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

        if (this.StoringInput(place.Root) && cases.TypeOf(this.body.Operands[index]) is var type &&
            (MirTypes.IsReference(type) || type.Semantics == Parsing.SemanticsKind.Raw) && this.Depth(place) == 0)
        {
            return this.sets.Add(0, this.Own(place.Root, place.Projection));
        }

        var (input, _, depth) = this.Landing(place);
        return input > 0 && this.redirected[input] > depth ? this.sets.Add(0, Past(input)) : 0;
    }

    // A Place's layer Derefs.
    private int Depth(MirPlace place)
    {
        var (count, layers) = (this.placeSteps.Walk(place, reborrow: false, ref this.mustSteps), 0);
        for (var i = 0; i < count; i++)
        {
            layers += Layer(this.mustSteps[i]) ? 1 : 0;
        }

        return layers;
    }

    // Where a Place certainly lands (Input 0: unknown): at depth 1 of input i's region, in the storage its layer at `part` refers to or,
    // past that layer through an own step, in other storage (OwnStepPart), or past i's referent (depth 2 or more, Part -1), while no
    // redirect reaches a layer it reads (Redirects).
    private (int Input, int Part, int Depth) Landing(MirPlace place)
    {
        if (place.Root < 0)
        {
            return default;
        }

        var count = this.placeSteps.Walk(place, reborrow: false, ref this.mustSteps);
        var (first, layers, owned) = (-1, 0, false);
        for (var i = 0; i < count; i++)
        {
            first = first < 0 && Layer(this.mustSteps[i]) ? i : first;
            layers += Layer(this.mustSteps[i]) ? 1 : 0;
            owned |= first >= 0 && this.mustSteps[i].Class == MirStepClass.Own;
        }

        if (this.StoringInput(place.Root))
        {
            return layers > 0 && this.redirected[place.Root] >= layers
                ? (place.Root, layers > 1 ? -1 : OwnStepPart(this.mustSteps[first].Prefix, owned), layers) : default;
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
        return (input, OwnStepPart(part, owned), 1);
    }

    // The rule at a store into a Place (Flow.Store), in the checks replay: Own(i, ·) stored past input i, or at depth 1 in other storage
    // than the value's own part (an own step's storage only where the destination's raw layers all state no Origin), where i's record
    // for that storage refuses it.
    private void Storing(MirPlace place)
    {
        if (!this.replaying || this.contract is null || this.mustValue == 0 || this.Landing(place) is not (> 0 and var input, var part, var depth))
        {
            return;
        }

        // The own step applies only where the destination holds raw layers and none states an Origin; elsewhere a part reads as its
        // layer's (the owner's O-190 (b)).
        var count = this.placeSteps.Walk(place, reborrow: false, ref this.mustSteps);
        var stepped =this.punKeys[this.Key(this.KeyOf(place.Root, this.mustSteps.AsSpan(0, count)))] is { RawInline: true, AnyStated: false };
        var at = stepped ? part : LayerPart(part);
        var own = false;
        foreach (var element in this.sets.Elements(this.mustValue))
        {
            var key = (element & 1) == 0 ? this.ownKeys[(element >> 1) - this.body.Locals.Length] : (Input: 0, Part: 0);
            own |= key.Input == input && (at < 0 || (stepped ? key.Part : LayerPart(key.Part)) != at);
        }

        if (!own || this.Unsafe())
        {
            return;
        }

        var last = -1;
        for (var i = 0; i < count; i++)
        {
            last = Layer(this.mustSteps[i]) ? i : last;
        }

        var storage = last + 1 < count ? place with { Projection = this.mustSteps[last + 1].Prefix } : place;
        var type = types.Erase(cases.TypeOf(storage));
        var slot = MirCallee.Slot(this.body.Key.Decl!, input - 1);
        foreach (var written in this.contract.Writes)
        {
            if (written.Output == slot && Refuses(depth > 1 ? this.ReachedEntry(written, type) ?? written.All : written.Own, slot))
            {
                this.overstored = input;
            }
        }
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
