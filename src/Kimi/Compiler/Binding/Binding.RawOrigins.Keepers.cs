// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>A borrow a raw Origin belongs to (SPEC 5.7 Origins, Calls; DECISIONS BU-24): <see cref="Referent"/>, what it admits (a borrow
/// input's referent, a slot keeper's under the Type that binds the slot), found at scope Type <see cref="Input"/> (for one found by its
/// Origin's kind, Binding.AtomMembers, -1 or an index of the binder's inputs, which no reader reads), and whether it may keep an
/// exclusive Loan (a `uniq` or `objuniq` borrow, a pair admitting one, a slot whose Loan requirement is `uniq` in some case the Type
/// binding it selects).</summary>
internal readonly record struct RawMember(int Input, BoundType Referent, bool Exclusive);

// Raw Origins, member borrows and slot keepers (SPEC 5.7 Origins, Calls, 15.3.5; D15; DECISIONS BU-24; contract in Binding.RawOrigins.cs):
// the borrows a stated raw layer's Origin belongs to, at one arm by the layer's position: within a scope (RawMembers), which retention,
// StoredMembers, InvalidRawOrigin and LayerMembers read, or by the Origin's binder (AtomMembers), which retention's binder reading and
// StoredMembers read; and the borrows keeping a declared slot, collected by the requirement fixed point (Keep, Keepers).
public sealed partial class Binding
{
    // SPEC 5.7 (D15): the borrows keeping each declared Origin slot, per declaration and pass (Keepers); the (slot, owner Type, input,
    // mode) keys one RawMembers call has walked, and the slots on the walk's current chain (KeptReferents), reused.
    private readonly HashSet<(OriginParameter Slot, BoundType Owner, int Input, bool Exclusive)> keeperVisits = new();
    private readonly HashSet<OriginParameter> keeperPath = new(ReferenceEqualityComparer.Instance);

    // The running pass's part of the slots' keepers (PassPart.Keepers, EnterPass).
    private Dictionary<OriginParameter, KeeperList> keepers;

    // SPEC 5.7 Origins, Calls (D15; DECISIONS BU-24): the borrows `origin` belongs to within `scope` (a contract's or signature's input
    // Types, an instance), appended to `members` in scope order: each Type there whose outer Origin it is, by its referent and mode
    // (BorrowReferent: a borrow, a pair layer admitting only borrows, a Slice), then each slot it binds in a nominal Type there, by its
    // keepers' referents in the cases that Type selects, with the slot's Loan requirement there as the mode (SlotMembers). Each (slot,
    // owner Type, input, mode) is walked once, so the cost is linear in the keepers reached and nothing is lost: equal keys add equal
    // members. False when the walk is incomplete (KeptReferents: a keeper cycle that changes its Type arguments, or a keeper's Type that
    // Binding's storage instantiation does not complete), which retention reads as CallRetention.Limit. Allocates nothing beyond
    // `members` when warm.
    internal bool RawMembers(BoundOrigin origin, ReadOnlySpan<BoundType> scope, List<RawMember> members)
    {
        this.keeperVisits.Clear();
        var complete = true;
        for (var i = 0; i < scope.Length && complete; i++)
        {
            if (ReferenceEquals(this.OuterOrigin(scope[i]), origin) && this.BorrowReferent(scope[i], out _, out var exclusive) is { } referent)
            {
                members.Add(new(i, referent, exclusive));
            }

            complete = this.SlotMembers(i, scope[i], origin, members);
        }

        return complete;
    }

    // SPEC 5.2.1, 5.7's table (PLAN G338): RawMembers of the Origin a raw layer states within a contract's inputs; none for `self`, `static`
    // or an unstated layer. Reached through TyCtxt.LayerMembers.
    internal bool LayerMembers(BoundType layer, ReadOnlySpan<BoundType> inputs, List<RawMember> members)
        => OwnedOrigin(layer) is not { Kind: not (OriginKind.Static or OriginKind.Unstated) } origin || this.RawMembers(origin, inputs, members);

    // SPEC 5.7, 15.3.5 (D15; design-final §4; DECISIONS BU-44): during the requirement fixed point's visit of a declaration's Types
    // (AccumulateRequirements), a borrow layer whose Origin is a slot of `schema` keeps that slot with its referent `kept`, a pair layer
    // only in the borrow cases of its binder `binders`, and an Origin argument bound to such a slot keeps it with the nested Type `kept`'s
    // slot `via`, whose keepers count as the slot's, substituted by that Type (Keepers). Kept per pass; a null referent keeps nothing.
    private void Keep(DeclarationSchema schema, BoundOrigin origin, BoundType? kept, OriginParameter? via, BinderSet binders)
    {
        for (var i = 0; i < origin.Operands.Count && origin.Kind == OriginKind.Intersection; i++)
        {
            this.Keep(schema, origin.Operands[i], kept, via, binders);
        }

        for (var i = 0; i < schema.Origins.Count && kept is not null; i++)
        {
            if (!ReferenceEquals(schema.Origins[i].Origin, origin))
            {
                continue;
            }

            if (!this.keepers.TryGetValue(schema.Origins[i], out var list))
            {
                this.keepers.Add(schema.Origins[i], list = new());
            }

            if (!Current(list.Pass, this.originRequirementPass))
            {
                (list.Pass, list.Count) = (this.originRequirementPass, 0);
            }

            list.Add(new(kept, via, binders));
        }
    }

    // An Origin's own members where it is bound, one row per kind (Z6's binder arm, R5.1). It reads no mark: the marks are the one Marks
    // reader's (R6.2). A member found here is read by its Referent and mode only: its Input (-1, or an index of the binder's inputs) is no
    // index of the reader's scope.
    // - a Projection or Anchor atom: the Borrows Binding publishes with it as their outer Origin (Borrows); false where it has none (a
    //   call result, a Place only addressed);
    // - an Input or Parameter Origin: RawMembers over the declared inputs where it is bound (BinderMembers: a function's signature, a
    //   declaration's own slot's Self), whose declaration decides the mode (SPEC 5.7 Authority); false for any other binder;
    // - a local region, `let` or `var`: the borrow layer of its owner's declared Type that it is the outer Origin of, else the keepers of
    //   the slot it is the argument of there (RegionMembers);
    // - a meet: its operands' members, false where any operand's is (SPEC 5.7 Origins);
    // - an Origin whose step is not Exclusive (StepOf: none, `self`, `static`): none, since no reader reads its members;
    // - any other (an Inference atom that is no local region, a ConstraintSet): false.
    private bool AtomMembers(BoundOrigin origin, List<RawMember> members)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            var complete = true;
            for (var i = 0; i < origin.Operands.Count && complete; i++)
            {
                complete = this.AtomMembers(origin.Operands[i], members);
            }

            return complete;
        }

        var published = this.Borrows(origin, members);
        return origin.Kind switch
        {
            OriginKind.Projection or OriginKind.Anchor => published,
            OriginKind.Input or OriginKind.Parameter => this.BinderMembers(origin, members),
            OriginKind.Inference when IsLocalRegion(origin) => this.RegionMembers(origin, members),
            _ => StepOf(origin) != HoldStep.Exclusive,
        };
    }

    // Ruling (a) of Z6 (SPEC 15.4.4, 15.6.5, 5.7 Origins): a local region's members, `let` and `var` alike: BorrowReferent of the layer of
    // its owner's declared Type that it is the outer Origin of, formed at the declaration and complete for every assignment, since every
    // value the local holds has that Type; where no borrow layer, pair layer admitting only borrows or Slice has it as its outer Origin,
    // the keepers of the slot of a nominal Type there that it is the argument of (`WriterS<ρ_x>`: Z6's R5.3). False where neither states
    // it (a raw layer's region slot: a named limit, R7 plan Z6b).
    private bool RegionMembers(BoundOrigin region, List<RawMember> members)
    {
        if (!(region.Binder is { } binder && this.symbols.TryGetValue(binder, out var owner) && owner.Type is { } type))
        {
            return false;
        }

        if (this.RegionLayer(type, region, members))
        {
            return true;
        }

        var first = members.Count;
        this.keeperVisits.Clear();
        return this.SlotMembers(-1, type, region, members) && members.Count > first;
    }

    // The first part of `type` (a Function's or Function Item's excepted: a contract keeps its own Origins) whose outer Origin is `region`,
    // as RegionMembers reads it. Invariant (R6.3): a local's declared Type states each of its region slots once, so the first such part
    // is the only one; the region's slot-argument keepers are read only where none exists.
    private bool RegionLayer(BoundType type, BoundOrigin region, List<RawMember> members)
    {
        if (this.BorrowReferent(type, out var outer, out var exclusive) is { } referent && ReferenceEquals(outer, region))
        {
            members.Add(new(-1, referent, exclusive));
            return true;
        }

        for (var i = 0; type.Kind is not (BoundTypeKind.Function or BoundTypeKind.FunctionItem) && i < type.Components.Count; i++)
        {
            if (this.RegionLayer(type.Components[i], region, members))
            {
                return true;
            }
        }

        return false;
    }

    // The keepers of a declared slot (Keep): a borrow's referent, or a nested Type with the nested slot keeping it. The slot's row is
    // found by the parts' one find (PassPart.Find: the library layer's first) and is current by the stamp of the part holding it.
    private ReadOnlySpan<Keeper> Keepers(OriginParameter slot)
    {
        var layer = this.Library.Layer.Part;
        var found = PassPart.Find(PassPart.Parts(layer.Keepers, this.keepers), slot, out var list);
        var call = ReferenceEquals(found, layer.Keepers) ? layer.RequirementCall : this.pass.Part.RequirementCall;
        return found is not null && Current(list!.Pass, call) ? list.Items : default;
    }

    // PLAN G949 S1-1's freeze of the layer's keepers (FreezeLibrary): each list current at the library pass's latest call, and the call
    // itself, Frozen32, so the lists read current in every later pass whatever the requirement counter.
    private void FreezeKeepers()
    {
        var part = this.pass.Part;
        foreach (var list in part.Keepers.Values)
        {
            if (list.Pass == part.RequirementCall)
            {
                list.Pass = Frozen32;
            }
        }

        part.RequirementCall = Frozen32;
    }

    // The member borrows of each slot of a nominal Type within `type` (scope Type `input`) that `origin` binds: the slot's keepers'
    // referents under that Type (KeptReferents), exclusive where the slot is `uniq` in some case the Type selects (RequirementAt). False
    // when a walk is incomplete.
    private bool SlotMembers(int input, BoundType type, BoundOrigin origin, List<RawMember> members)
    {
        var complete = true;
        for (var i = 0; i < type.Components.Count && complete; i++)
        {
            complete = this.SlotMembers(input, type.Components[i], origin, members);
        }

        for (var i = 0; complete && type.Symbol?.Schema is { } schema && i < type.OriginArguments.Count && i < schema.Origins.Count; i++)
        {
            if (ReferenceEquals(type.OriginArguments[i], origin))
            {
                var exclusive = this.RequirementAt(schema.Origins[i], type).Strongest == LoanRequirement.Uniq;
                complete = this.KeptReferents(schema.Origins[i], type, input, exclusive, members);
            }
        }

        return complete;
    }

    // A slot's keepers in `owner`: each kept referent, or a nested Type's slot's keepers in that Type, substituted by `owner`; a pair
    // layer's only where `owner` selects a borrow case of its binder (SPEC 15.3.5, DECISIONS BU-44). A key already walked adds nothing new
    // (its walk is done or in progress on the current chain). A slot met again on the current chain with another owner Type is a keeper
    // cycle that changes its Type arguments, whose walk need not end, and a keeper's Type that StoredType does not instantiate under
    // `owner` reaches storage Binding cannot name: either makes the walk incomplete (false), never a silent stop.
    private bool KeptReferents(OriginParameter slot, BoundType owner, int input, bool exclusive, List<RawMember> members)
    {
        if (this.keeperVisits.Contains((slot, owner, input, exclusive)))
        {
            return true;
        }

        if (!this.keeperPath.Add(slot))
        {
            return false;
        }

        this.keeperVisits.Add((slot, owner, input, exclusive));
        var complete = true;
        foreach (var keeper in this.Keepers(slot))
        {
            if (!keeper.Binders.IsEmpty &&
                this.RequirementOf(new(LoanRequirement.None, keeper.Binders), owner.Symbol!.Schema!, owner).Strongest == LoanRequirement.None)
            {
                continue;
            }

            if (this.PartType(keeper.Type, owner) is not { } kept)
            {
                complete = false;
            }
            else if (keeper.Via is null)
            {
                members.Add(new(input, kept, exclusive));
            }
            else
            {
                complete = this.KeptReferents(keeper.Via, kept, input, exclusive, members);
            }

            if (!complete)
            {
                break;
            }
        }

        this.keeperPath.Remove(slot);
        return complete;
    }

    // A slot's keeper: the referent of a borrow keeping it, or (Via) a nested Type whose slot Via keeps it; a pair layer's referent keeps
    // it only in its binder's borrow cases (Binders, of the slot's schema).
    internal readonly record struct Keeper(BoundType Type, OriginParameter? Via, BinderSet Binders);

    // A slot's keepers in one pass, without duplicates; reused across passes (a warm pass allocates nothing).
    internal sealed class KeeperList
    {
        private Keeper[] items = new Keeper[2];

        internal uint Pass { get; set; }

        internal int Count { get; set; }

        internal ReadOnlySpan<Keeper> Items => this.items.AsSpan(0, this.Count);

        internal void Add(Keeper keeper)
        {
            if (this.Items.Contains(keeper))
            {
                return;
            }

            if (this.Count == this.items.Length)
            {
                Array.Resize(ref this.items, this.Count * 2);
            }

            this.items[this.Count++] = keeper;
        }
    }
}
