// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// One source operation, with input inference already complete. Selection changes neither the syntax nor its bindings.
internal sealed class ExplicitAdaptationPlan
{
    internal BoundType Source { get; set; } = null!;

    internal BoundType Target { get; set; } = null!;

    internal uint Operations { get; set; }

    internal bool IsShorthand { get; set; }

    internal BoundType? AddressBorrow { get; set; }

    internal bool Creates => (this.Operations & (1U << (int)ConversionBinding.ObjectCreation)) != 0;

    internal static ConversionBinding Select(BoundType source, BoundType target, bool shorthand = false)
    {
        if (target.Semantics == SemanticsKind.Raw && shorthand && !ReferenceEquals(source, target))
        {
            return ReferenceEquals(source, target.Components[0]) ? ConversionBinding.Address : ConversionBinding.None;
        }

        if (ObjectTypes.HandleMode(target) is not null)
        {
            if (ObjectTypes.HandleMode(source) is not null)
            {
                return source.Semantics != target.Semantics ? ConversionBinding.None
                    : ReferenceEquals(source, target) ? ConversionBinding.Identity : ConversionBinding.ObjectUpcast;
            }

            return source.Semantics == SemanticsKind.Owner && ReferenceEquals(source, target.Components[0])
                ? ConversionBinding.ObjectCreation : ConversionBinding.None;
        }

        if (target.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            return ReferenceEquals(source, target.Components[0]) ? ConversionBinding.Borrow : ConversionBinding.None;
        }

        if (ObjectTypes.IsBorrow(target))
        {
            if ((ObjectTypes.HandleMode(source) is not null || ObjectTypes.IsBorrow(source)) &&
                (target.Semantics != SemanticsKind.ObjUniq || source.Semantics is SemanticsKind.Obj or SemanticsKind.ObjUniq))
            {
                return ReferenceEquals(source.Components[0], target.Components[0]) ? ConversionBinding.Borrow : ConversionBinding.ObjectUpcast;
            }

            return ConversionBinding.None;
        }

        if (ReferenceEquals(source, target))
        {
            return ConversionBinding.Identity;
        }

        return (ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) ||
            (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)) ? ConversionBinding.Pointer : ConversionBinding.None;
    }
}
