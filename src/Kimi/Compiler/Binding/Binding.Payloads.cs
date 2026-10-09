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
        if (!IsObjectSemantics(actual.Semantics) || actual.Components.Count != 1 ||
            pattern is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
            !ReferenceEquals(actual.Components[0], pattern.Components[0]) ||
            this.RequestCapability(actual.Components[0], this.Library.Sealed, scope) != ConstraintProof.Proven)
        {
            return false;
        }

        // SPEC 13.5.5.1: exclusive payload access needs exclusive authority along the whole path, also to an objuniq view.
        var exclusive = pattern.Semantics == SemanticsKind.Uniq;
        if (exclusive && ((actual.Semantics != SemanticsKind.ObjUniq && ObjectTypes.HandleMode(actual) is not { PayloadAuthority: LoanRequirement.Uniq }) || ReachedThroughShared(source)))
        {
            return false;
        }

        if (actual.Semantics is not (SemanticsKind.ObjRef or SemanticsKind.ObjUniq) && !this.BorrowableHandle(source, scope, exclusive, true))
        {
            return false;
        }

        result = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [actual.Components[0]], origin: this.PlaceOrigin(source));
        return true;
    }
}
