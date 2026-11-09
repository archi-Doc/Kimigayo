// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ComparisonTypes
{
    internal static bool IsComposite(BoundType? type)
        => type?.Kind == BoundTypeKind.Tuple || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 };

    // SPEC 13.4: whether the operator table runs a Type's comparison under a Contract. Whether a library group serves a Type is the witness
    // table's question (Binding.WitnessGroup); conformance asks both.
    internal static bool OperatorTableRuns(BoundType? type, KimiDeclarationId? contract)
        => contract == KimiDeclarationId.Equatable ? ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit)
            : contract == KimiDeclarationId.Comparable && (ScalarTypes.Width(type) != 0 || ReferenceEquals(type, BoundType.Char));
}
