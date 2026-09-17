// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>The guarded Subject subset shared by binding, ownership and emission.</summary>
internal static class MatchTypes
{
    internal static bool SupportsGuard(BoundMatch match, int root)
    {
        if (SupportsGuard(match.Positions[root].MatchedType))
        {
            return true;
        }

        // Composite candidates currently expose independent scalar/Unit values.
        // Reference-bearing and whole aggregate candidates need additional Loan plans.
        for (var i = root; i < match.Positions[root].End; i++)
        {
            var position = match.Positions[i];
            if (position.AccessMode != PatternAccessMode.Owned || position.ImplicitDeref != PatternImplicitDeref.None ||
                (position.Kind == BoundPatternKind.Binding && !ScalarTypes.Supports(position.MatchedType) && !ReferenceEquals(position.MatchedType, BoundType.Unit)))
            {
                return false;
            }
        }

        return match.Positions[root].Kind is BoundPatternKind.Case or BoundPatternKind.Tuple;
    }

    internal static bool SupportsGuard(BoundType? type) => ScalarTypes.Supports(type) ||
        ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String);
}
