// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's destructions (SPEC 15.6.4, 16.3.1–16.3.3, 16.4; DECISIONS BU-47; PLAN G264 U1): a Drop the view keeps retains as a call of each
// `drop` its destruction runs, whose `self` is a `uniq/Self` receiver referring to the destroyed part. The receiver's value is the
// destroyed root's plain address (MirBorrows.Addresses), formed in the pre-pass for each Drop whose destruction may run a drop (Dropping:
// Observe), so every reader of a direct Loan reads it as a reference to that root; it is never a conflict candidate and cannot be stored
// (BU-47: input 0 has no Value row). The drops are the destruction walk's (MirTypes.Destruction): a struct's declared drop, then its parts
// (16.3.2, MirTypes.Parts); a Sealed handle's payload, walked on (an rc or arc release may be final); an erased destruction where the walk
// cannot see the drop (an abstract part not proven Copy, a Function value, a handle whose payload is not proven Sealed;
// TyCtxt.RetainsErasedDestruction). Each drop's retention (TyCtxt.Retains over the drop and its declaring layer) stores through its
// output as a Call's does (MirBorrows.Calls' Keep, W from the receiver); a Limit retention is the depth limit at the location (R4-M153).
// A published operation's cleanup is the destruction its row lists (SPEC 4.9.6; PublishedRow.Destroys, DECISIONS BU-70), composed at the
// call after its own rows over the destroying argument's value (Destroying), which replaces C3's referent guard (R4-M143).
// Input: the view's kept Drops, A2's verdicts (Dropping), Binding's drop retention, the published rows and A4's Type below a parameter
// (MirEffects.Below).
// Output: by block, the Drop's receiver entry (-1: none); at the Drop's or the Call's plan, the drops' stores.
// Invariants: the retentions of a case Type are walked once per definition and program version (the destructions memo's key).
// Must not derive retention (TyCtxt), compute W (MirBorrows.Reach) or record a problem (MirBorrows.Locations).
internal sealed partial class MirBorrows
{
    private readonly Dictionary<BoundType, (int Start, int Count)> dropsOf = new(ReferenceEqualityComparer.Instance); // By case Type.
    private readonly List<CallRetention> dropRows = []; // The composed retentions, dropsOf's ranges.
    private (BindingSymbol? Definition, ulong Version) dropsFor; // The definition and program version dropsOf answers.
    private int[] receivers = []; // By block: its Drop's receiver entry, -1 for none.

    // The receivers of the view's Drops (pre-pass, after the addresses and before the holders): a Drop of a local whose destruction may run
    // a drop (Dropping: Observe) gets its root's plain address.
    private void Receivers()
    {
        MirDataflow.Grow(ref this.receivers, this.body.Blocks.Length);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            this.receivers[block] = data.Terminator is { Kind: MirTerminatorKind.Drop, Place: { Root: >= 0 } place } &&
                this.Dropping(block, place) == AccessKind.Observe
                ? this.Address(place.Root, -1, direct: true, this.Location(block, data.Count), []) : -1;
        }

        this.Work += this.body.Blocks.Length;
    }

    // A Drop's stores (SPEC 15.6.4): its destruction composed over the receiver, the destroyed root's address.
    private void Destroyed(ulong[] state, in MirTerminator drop, int block, bool checks)
    {
        if (this.receivers[block] >= 0)
        {
            var call = drop with { Operand = MirOperand.Copy(drop.Place) };
            this.Compose(state, call, this.sets.Add(0, this.loanBase + this.receivers[block]), cases.Type(this.body.TypeOf(drop.Place)!), checks);
        }
    }

    // A published operation's cleanup (SPEC 4.9.6, 15.6.4; DECISIONS BU-70): the destruction of the Type below its row's Destroys parameter,
    // composed at the call after its own rows over the argument's value as the receiver: the borrow of the destroyed target (`clear`,
    // `truncate`, `removeAll`, `replace`), or a by-value input's own value (`insertOrReplace`'s unused key).
    private void Destroying(ulong[] state, in MirTerminator call, bool checks)
    {
        if (call.Callee is { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: { } symbol } && types.Published(symbol) is { Destroys: >= 0 } row &&
            effects.Below(symbol, call.Callee.Subst, row.Destroys) is { } type)
        {
            var position = MirCallee.Position(symbol, row.Destroys);
            var input = call with { Count = 0, Operand = this.body.Operands[call.Start + position] };
            this.Compose(state, input, this.argValues[position], type, checks);
        }
    }

    // A destruction of a case Type over a receiver value: each drop it runs, as a call whose input 0 is the receiver (the view's operand
    // `call.Operand`, position 0 = call.Count), through each output (Keep); a Limit retention is the depth limit at the location.
    private void Compose(ulong[] state, in MirTerminator call, int receiver, BoundType type, bool checks)
    {
        var (start, count) = this.DropRows(type);
        MirDataflow.Grow(ref this.argValues, 1);
        MirDataflow.Grow(ref this.slotPositions, 1);
        (this.argValues[0], this.slotPositions[0], this.referentCount) = (receiver, 0, 0);
        for (var i = start; i < start + count; i++)
        {
            var retention = this.dropRows[i];
            this.deep |= checks && retention.IsLimit;
            foreach (var written in retention.IsLimit ? default : retention.Writes)
            {
                this.Keep(state, call, written, checks);
            }
        }
    }

    // The retentions of the drops destroying a value of a case Type runs, walked once per definition and program version.
    private (int Start, int Count) DropRows(BoundType type)
    {
        if (this.dropsFor != (this.body.Key.Decl, this.program.Version))
        {
            this.dropsFor = (this.body.Key.Decl, this.program.Version);
            this.dropsOf.Clear();
            this.dropRows.Clear();
        }

        ref var range = ref CollectionsMarshal.GetValueRefOrAddDefault(this.dropsOf, type, out var known);
        if (!known)
        {
            var start = this.dropRows.Count;
            this.entered.Clear();
            var composer = new Composer(this);
            MirTypes.Destruction(type, ref composer);
            range = (start, this.dropRows.Count - start);
        }

        return range;
    }

    // A part the destruction walk reports (SPEC 16.3): a declared drop over its layer, then the struct's parts (16.3.2); a Sealed handle's
    // payload walked on; an erased destruction where the walk cannot see the drop (an abstract part not proven Copy, a Function value, a
    // handle whose payload is not proven Sealed). A string's or a sequence's own release runs none; its elements are parts of their own.
    private bool Composing(ref Composer composer, BoundType type, int depth)
    {
        var definition = this.body.Key.Decl;
        BoundType? erased = null;
        if (type.IsAbstract || type.Kind == BoundTypeKind.Function)
        {
            erased = type.Kind == BoundTypeKind.Function || !types.Copy(definition, type) ? type : null;
        }
        else if (type.Kind == BoundTypeKind.Semantics)
        {
            var payload = type.Components[0];
            if (types.Sealed(definition, payload))
            {
                return MirTypes.Destruction(payload, ref composer, depth + 1);
            }

            erased = payload;
        }
        else if (AdtDef.DestructorSymbol(type) is { } destructor)
        {
            this.Work++;
            this.dropRows.Add(types.Retains(destructor, types.Synthesized(destructor, type, []), definition));
            return MirTypes.Parts(type, ref composer, depth);
        }

        if (erased is not null)
        {
            this.Work++;
            this.dropRows.Add(types.RetainsErasedDestruction(erased, definition));
        }

        return false;
    }

    // The destruction walk's visitor for the drops it runs (Composing).
    private struct Composer(MirBorrows owner) : IMirDestruction
    {
        public readonly bool First(BoundType type, int depth)
            => MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _), depth);

        public bool Visit(BoundType type, int depth) => owner.Composing(ref this, type, depth);

        public readonly bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _));
            return false;
        }
    }
}
