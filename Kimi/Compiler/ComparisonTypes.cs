// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static class ComparisonTypes
{
    internal static bool IsBuiltin(BoundType? type, KimiDeclarationId? contract)
        => contract == KimiDeclarationId.Equatable ? ScalarTypes.Supports(type)
            : contract == KimiDeclarationId.Comparable && (ScalarTypes.Width(type) != 0 || ReferenceEquals(type, BoundType.Char));
}
