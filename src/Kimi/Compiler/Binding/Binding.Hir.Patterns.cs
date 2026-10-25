// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The Adjust column's rule (Binding.Hir.Adjust) for a match or for (SPEC 15.1.6): how its Subject is acquired or borrowed in place, and how
// each Pattern binding its plan borrows or acquires designates its Place; taken by Consume's reader.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.1.6: a Subject is acquired as written, but a bare Place is borrowed in place: its stored exclusive reference Reborrowed (the
    // borrow keeps the stored Semantics and referent, its Origin Binding's name, DECISIONS BU-27), a stored exclusive reference reached
    // through a shared layer read as one shared reference, and any other Place borrowed in its mode.
    private static void Subject<T>(Koto subject, BoundType? borrow, ref T taker)
        where T : IHirTaker
    {
        if (borrow is null)
        {
            taker.Take(subject);
            return;
        }

        var mode = borrow.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow
            : subject.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ? HirAdaptation.ReferenceRead : HirAdaptation.SharedBorrow;
        var reborrow = subject.BoundType is { Kind: BoundTypeKind.Semantics, Components: [var stored] } own &&
            borrow is { Kind: BoundTypeKind.Semantics, Components: [var lent] } && own.Semantics == borrow.Semantics && ReferenceEquals(lent, stored);
        taker.Write(subject, reborrow ? new(HirAcquire.Reborrow, HirAdaptation.None) : new(HirAcquire.None, mode));
    }

    // A Pattern binding its plan borrows, below a reference layer, designates the payload of its Case position or an element of its Tuple
    // position (SPEC 15.1.6), followed by the pair layer its plan selects (SPEC 13.5.5, DECISIONS BU-14); a binding of the whole Subject
    // acquires it.
    private static void Bindings<T>(BoundMatch? plan, ref T taker)
        where T : IHirTaker
    {
        for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
        {
            if (plan.Positions[i] is { Kind: BoundPatternKind.Binding } position && HirAdjust.Of(position.Acquisition, position.AccessMode) is var adjust)
            {
                var place = adjust.Acquire != HirAcquire.None || position.Parent < 0 ? HirPlaceKind.Unset
                    : plan.Positions[position.Parent].Kind == BoundPatternKind.Case ? HirPlaceKind.Downcast : HirPlaceKind.Field;
                taker.Write(position.Source, adjust, new(place, (byte)position.ImplicitFollows));
            }
        }
    }
}
