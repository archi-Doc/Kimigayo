// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsObjectSemantics(SemanticsKind kind)
        => kind is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    private bool TryPayloadProjection(Koto source, BoundType pattern, BoundType actual, BindingScope scope, out BoundType result)
    {
        result = actual;
        var objects = this.ObjectCases(actual, scope, out var view); // Every case an object form of `view` (a pair layer's included).
        if (objects == SemanticsMask.None ||
            pattern is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
            !ReferenceEquals(view, pattern.Components[0]))
        {
            return false;
        }

        if (this.ProveSealed(view, scope) != ConstraintProof.Proven)
        {
            return false;
        }

        // SPEC 13.5.5: exclusive payload access needs exclusive authority along the whole path and through the receiver's own layer, a
        // pair layer's weakest case included: the authority a member write through it reads (PlaceAuthority, ExclusivelyAcquirable).
        var exclusive = pattern.Semantics == SemanticsKind.Uniq;
        if (exclusive && !ExclusivelyAcquirable(source))
        {
            return false;
        }

        // A receiver's implicit Borrow is lent as SPEC lends it (BorrowablePlace).
        if ((objects & SemanticsMask.Object) != 0 && !this.LendsHandle(source, scope, exclusive, out _))
        {
            return false;
        }

        result = this.types.Intern(BoundTypeKind.Semantics, null, pattern.Semantics, [view], origin: this.PlaceOrigin(source));
        return true;
    }
}
