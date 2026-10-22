// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins, slot keepers (SPEC 5.7 Calls, 15.3.5; D15; DECISIONS BU-24; contract in Binding.RawOrigins.cs): the borrows keeping a
// declared slot, collected by the requirement fixed point (Keep, Keepers), and the limit on keeper cycles that change Type arguments
// (RejectPolymorphicKeeperCycles, KeeperCycles, ClosingField).
public sealed partial class Binding
{
    // SPEC 5.7 (D15): the borrows keeping each declared Origin slot, per declaration and pass (Keepers), and the slots whose keepers a
    // walk is following, outermost first (KeeperContent, KeeperCycles), reused.
    private readonly Dictionary<OriginParameter, KeeperList> keepers = new(ReferenceEqualityComparer.Instance);
    private readonly List<OriginParameter> keeperChain = new();

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

    // SPEC 5.7 (D15; DECISIONS BU-24): retention follows a slot's keepers through nested Types' slots and ends a chain at a slot already
    // on it (KeeperContent), which loses nothing only when the cycle returns the Type it started from. Each keeper cycle of a struct's or
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
