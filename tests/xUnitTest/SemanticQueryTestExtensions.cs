// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace XunitTest;

/// <summary>Binding facts of a syntax node, read through the <see cref="SemanticQuery"/> of the node's compilation. Tests use
/// these instead of Koto semantic slots, so the slots can move without changing the tests.</summary>
internal static class SemanticQueryTestExtensions
{
    internal static BoundType? TypeOf(this Koto node) => Query(node).TypeOf(node);

    internal static BoundOrigin? OriginOf(this Koto node) => Query(node).OriginOf(node);

    internal static BindingState StateOf(this Koto node) => Query(node).StateOf(node);

    internal static BindingFailure FailureOf(this Koto node) => Query(node).FailureOf(node);

    private static SemanticQuery Query(Koto node) => node.CodeContext.Compilation.Semantics;
}
