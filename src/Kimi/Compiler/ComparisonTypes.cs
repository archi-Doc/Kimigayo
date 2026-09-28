// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ComparisonTypes
{
    internal static bool IsComposite(BoundType? type)
        => type?.Kind == BoundTypeKind.Tuple || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 };

    internal static bool IsBuiltin(BoundType? type, KimiDeclarationId? contract)
        => contract == KimiDeclarationId.Equatable ? ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.String) || ReferenceEquals(type, BoundType.Unit)
            : contract == KimiDeclarationId.Comparable && (ScalarTypes.Width(type) != 0 || ReferenceEquals(type, BoundType.Char) || ReferenceEquals(type, BoundType.String));
}
