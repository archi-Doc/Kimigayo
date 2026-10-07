// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>A generic parameter, projection, Semantics application or adaptation result stands for a complete Type of an instance.</summary>
internal static class AbstractTypes
{
    /// <summary>Gets a value indicating whether a Type stands for any complete Type of an instance.</summary>
    /// <param name="type">The Type.</param>
    /// <returns><see langword="true"/> for a parameter, projection or dependent Semantics term.</returns>
    internal static bool IsAbstract(BoundType type)
        => type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation;

    /// <summary>Gets a value indicating whether a Type is abstract or has an abstract part, whose Loans its Origins do not show.</summary>
    /// <param name="type">The Type.</param>
    /// <returns><see langword="true"/> when the Type or one of its components is abstract.</returns>
    internal static bool HasAbstractPart(BoundType? type)
    {
        if (type is null)
        {
            return false;
        }

        if (IsAbstract(type))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasAbstractPart(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }
}
