// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>Result shapes that use explicit storage rather than a scalar phi. Concrete layouts are checked by lowering.</summary>
internal static class SlotTypes
{
    internal static bool IsResult(BoundType? type) => ReferenceEquals(type, BoundType.String) || type?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Function or BoundTypeKind.Closure || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type);
}
