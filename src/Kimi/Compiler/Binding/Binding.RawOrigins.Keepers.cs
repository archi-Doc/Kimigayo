// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>A borrow a raw Origin belongs to (SPEC 5.7 Origins, Calls; DECISIONS BU-24): <see cref="Referent"/>, what it admits (a borrow
/// input's referent, a slot keeper's under the Type that binds the slot), found at scope Type <see cref="Input"/>, and whether it may keep an
/// exclusive Loan (a `uniq` or `objuniq` borrow, a pair admitting one, a slot whose Loan requirement is `uniq`).</summary>
internal readonly record struct RawMember(int Input, BoundType Referent, bool Exclusive);

// Raw Origins, member borrows and slot keepers (SPEC 5.7 Origins, Calls, 15.3.5; D15; DECISIONS BU-24; contract in Binding.RawOrigins.cs):
// the borrows a raw Origin belongs to within a scope (RawMembers), which retention, the signature step and A5 read; the borrows keeping
// a declared slot, collected by the requirement fixed point (Keep, Keepers); and the limit on keeper cycles that change Type arguments
// (RejectPolymorphicKeeperCycles, KeeperCycles, ClosingField).
public sealed partial class Binding
{
    // SPEC 5.7 (D15): the borrows keeping each declared Origin slot, per declaration and pass (Keepers), and the slots whose keepers a
    // walk is following, outermost first (KeptReferents, KeeperCycles), reused.
    private readonly Dictionary<OriginParameter, KeeperList> keepers = new(ReferenceEqualityComparer.Instance);
    private readonly List<OriginParameter> keeperChain = new();

    // SPEC 5.7 Origins, Calls (D15; DECISIONS BU-24): the borrows `origin` belongs to within `scope` (a contract's or signature's input
    // Types, an instance), appended to `members` in scope order: each Type there whose outer Origin it is, by its referent and mode
    // (BorrowReferent: a borrow, a pair layer admitting only borrows, a Slice), then each slot it binds in a nominal Type there, by its
    // keepers' referents under that Type with the slot's Loan requirement as the mode (SlotMembers). Total; one walk of the scope's Types
    // and the bound slots' keeper lists, allocating nothing beyond `members`.
    internal void RawMembers(BoundOrigin origin, ReadOnlySpan<BoundType> scope, List<RawMember> members)
    {
        for (var i = 0; i < scope.Length; i++)
        {
            if (ReferenceEquals(this.OuterOrigin(scope[i]), origin) && this.BorrowReferent(scope[i], out _, out var exclusive) is { } referent)
            {
                members.Add(new(i, referent, exclusive));
            }

            this.SlotMembers(i, scope[i], origin, members);
        }
    }

    // The written Type of the Field of a slot's declaration that holds `kept`, a keeper's nested Type.
    private static Koto? ClosingField(OriginParameter slot, BoundType kept)
    {
        foreach (var field in slot.Origin.Binder?.BoundSymbol?.Adt?.Fields ?? [])
        {
            if (Holds(field.BoundSymbol?.Type))
            {
                return field.TypeKoto ?? field;
            }
        }

        return null;

        bool Holds(BoundType? type)
        {
            if (ReferenceEquals(type, kept))
            {
                return true;
            }

            for (var i = 0; type is not null && i < type.Components.Count; i++)
            {
                if (Holds(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SPEC 5.7, 15.3.5 (D15; design-final §4): during the requirement fixed point's visit of a declaration's Types (AccumulateRequirements),
    // a borrow layer `type` whose Origin is a slot of `schema` keeps that slot with its referent, and an Origin argument bound to such a
    // slot keeps it with the nested Type's slot `via`, whose keepers count as the slot's, substituted by `type` (Keepers). Kept per pass.
    private void Keep(DeclarationSchema schema, BoundOrigin origin, BoundType type, OriginParameter? via)
    {
        for (var i = 0; i < origin.Operands.Count && origin.Kind == OriginKind.Intersection; i++)
        {
            this.Keep(schema, origin.Operands[i], type, via);
        }

        var kept = via is null ? this.BorrowReferent(type, out _, out _) : type;
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

            if (list.Pass != this.originRequirementPass)
            {
                (list.Pass, list.Count) = (this.originRequirementPass, 0);
            }

            list.Add(new(kept, via));
        }
    }

    // The keepers of a declared slot in this pass (Keep): a borrow's referent, or a nested Type with the nested slot keeping it.
    private ReadOnlySpan<Keeper> Keepers(OriginParameter slot)
        => this.keepers.TryGetValue(slot, out var list) && list.Pass == this.originRequirementPass ? list.Items : default;

    // The member borrows of each slot of a nominal Type within `type` (scope Type `input`) that `origin` binds: the slot's keepers'
    // referents under that Type (KeptReferents), with its Loan requirement as their mode.
    private void SlotMembers(int input, BoundType type, BoundOrigin origin, List<RawMember> members)
    {
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.SlotMembers(input, type.Components[i], origin, members);
        }

        for (var i = 0; type.Symbol?.Schema is { } schema && i < type.OriginArguments.Count && i < schema.Origins.Count; i++)
        {
            if (ReferenceEquals(type.OriginArguments[i], origin))
            {
                var exclusive = schema.Origins[i].LoanRequirement == LoanRequirement.Uniq;
                this.KeptReferents(schema.Origins[i], type, input, exclusive, members, this.keeperChain.Count);
            }
        }
    }

    // A slot's keepers in `owner`: each kept referent, or a nested Type's slot's keepers in that Type, substituted by `owner`. The chain
    // (keeperChain from `chain` on) ends at a slot already on it: its cycle returns the owner it started from, since one that does not
    // is rejected at its declaration (RejectPolymorphicKeeperCycles), so nothing is lost.
    private void KeptReferents(OriginParameter slot, BoundType owner, int input, bool exclusive, List<RawMember> members, int chain)
    {
        if (this.keeperChain.IndexOf(slot, chain) >= 0)
        {
            return;
        }

        this.keeperChain.Add(slot);
        foreach (var keeper in this.Keepers(slot))
        {
            if (this.StoredType(keeper.Type, owner) is not { } kept)
            {
                continue;
            }

            if (keeper.Via is null)
            {
                members.Add(new(input, kept, exclusive));
            }
            else
            {
                this.KeptReferents(keeper.Via, kept, input, exclusive, members, chain);
            }
        }

        this.keeperChain.RemoveAt(this.keeperChain.Count - 1);
    }

    // SPEC 5.7 (D15; DECISIONS BU-24): retention follows a slot's keepers through nested Types' slots and ends a chain at a slot already
    // on it (KeptReferents), which loses nothing only when the cycle returns the Type it started from. Each keeper cycle of a struct's or
    // enum's slot is walked from that declaration's own Type (SelfType); one that returns another Type (slot recursion through a raw
    // pointer that changes its Type arguments) is a located Unsupported_Kd at the written Type of the Field that closes it, a limit
    // (STATUS).
    private void RejectPolymorphicKeeperCycles()
    {
        foreach (var (slot, list) in this.keepers)
        {
            if (list.Pass == this.originRequirementPass && slot.Origin.Binder?.BoundSymbol is { Adt: not null } declaration)
            {
                var own = this.SelfType(declaration);
                this.keeperChain.Add(slot);
                this.KeeperCycles(slot, slot, own, own);
                this.keeperChain.Clear();
            }
        }
    }

    // The keeper cycles from `slot`, whose keepers `owner` substitutes, back to `start`, whose declaration's own Type is `own`.
    private void KeeperCycles(OriginParameter start, OriginParameter slot, BoundType owner, BoundType own)
    {
        foreach (var keeper in this.Keepers(slot))
        {
            if (keeper.Via is not { } via || this.StoredType(keeper.Type, owner) is not { } kept)
            {
                continue;
            }

            if (ReferenceEquals(via, start))
            {
                if (!ReferenceEquals(kept, own) && ClosingField(slot, keeper.Type) is { } field)
                {
                    this.Fail(field, BindingFailure.Unsupported);
                }
            }
            else if (!this.keeperChain.Contains(via))
            {
                this.keeperChain.Add(via);
                this.KeeperCycles(start, via, kept, own);
                this.keeperChain.RemoveAt(this.keeperChain.Count - 1);
            }
        }
    }

    // A slot's keeper: the referent of a borrow keeping it, or (Via) a nested Type whose slot Via keeps it.
    private readonly record struct Keeper(BoundType Type, OriginParameter? Via);

    // A slot's keepers in one pass, without duplicates; reused across passes (a warm pass allocates nothing).
    private sealed class KeeperList
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
