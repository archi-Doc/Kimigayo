// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The guarded Subject subset shared by binding, ownership and emission.</summary>
internal static class MatchTypes
{
    // Called after finite storage/layout and destructor preparation. Tuple/Case
    // decomposition transfers complete array/struct payloads; it never splits a
    // struct with a user destructor. Complete payload Types retain the Loans verified by ownership analysis.
    internal static bool SupportsOwnedPatternValue(BoundType type, Dictionary<BoundType, bool> cache)
    {
        // Complete handles move without decomposing their pointees. Their prepared layout selects destruction:
        // common Function values use their operations table; object handles use their static ownership mode.
        if (ScalarTypes.Supports(type) || ReferenceTypes.IsBorrow(type) || ReferenceTypes.IsPointer(type) ||
            ObjectTypes.HandleMode(type) is not null ||
            ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || type.Kind is BoundTypeKind.Slice or BoundTypeKind.Function)
        {
            return true;
        }

        if (type.Semantics != SemanticsKind.Owner)
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
        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary)
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

        if (StructStorage.IsStruct(type))
        {
            if (type.StoredBase is { } parent && !SupportsOwnedPatternValue(parent, cache))
            {
                return false;
            }

            for (var i = 0; i < StructStorage.Count(type); i++)
            {
                if (StructStorage.FieldType(type, i) is not { } field || !SupportsOwnedPatternValue(field, cache))
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

        for (var i = root; i < match.Positions[root].End; i++)
        {
            var position = match.Positions[i];
            if (position.Kind == BoundPatternKind.Binding && position.CandidateSymbol?.Type is null)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool NeedsGuardProtection(BoundType type) => !ScalarTypes.Supports(type) && !ReferenceEquals(type, BoundType.Unit);

    internal static bool SupportsGuard(BoundType? type) => ScalarTypes.Supports(type) ||
        ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String);
}
