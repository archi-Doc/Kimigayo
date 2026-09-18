// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The guarded Subject subset shared by binding, ownership and emission.</summary>
internal static class MatchTypes
{
    // Called after finite storage/layout preparation. User destructors, reference
    // payloads and other aggregate shapes require their own decomposition proof.
    internal static bool SupportsOwnedPatternValue(BoundType type, Dictionary<BoundType, bool> cache)
    {
        if (ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String))
        {
            return true;
        }

        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null || type.OriginArguments.Count != 0)
        {
            return false;
        }

        if (cache.TryGetValue(type, out var supported))
        {
            return supported;
        }

        // Repeated payload Types share one proof. A pending entry also rejects
        // a cycle defensively, even if called with unprepared storage.
        cache[type] = false;
        if (type.Kind == BoundTypeKind.Tuple)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!SupportsOwnedPatternValue(type.Components[i], cache))
                {
                    return false;
                }
            }

            return cache[type] = true;
        }

        if (EnumStorage.IsEnum(type) && type.StoredCases is { } cases)
        {
            for (var i = 0; i < cases.Length; i++)
            {
                if (!SupportsOwnedPatternValue(cases[i], cache))
                {
                    return false;
                }
            }

            return cache[type] = true;
        }

        return false;
    }

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
