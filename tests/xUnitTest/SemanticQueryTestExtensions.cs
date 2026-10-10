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

    internal static BindingSymbol? SymbolOf(this Koto node) => Query(node).SymbolOf(node);

    internal static CallPlan? CallOf(this InvocationKoto node) => Query(node).CallOf(node);

    internal static CallPlan? ValueCallOf(this InvocationKoto node) => Query(node).ValueCallOf(node);

    internal static BindingState StateOf(this Koto node) => Query(node).StateOf(node);

    internal static BindingFailure FailureOf(this Koto node) => Query(node).FailureOf(node);

    internal static BoundConstraint? ConstraintOf(this IsKoto node) => Query(node).ConstraintOf(node);

    internal static BoundRuntimeTypeTest? RuntimeTestOf(this IsKoto node) => Query(node).RuntimeTestOf(node);

    internal static BoundClosure? ClosureOf(this FunctionKoto node) => Query(node).ClosureOf(node);

    internal static (ConversionBinding Kind, InvocationKoto? Creation, ExplicitAdaptationPlan? Adaptation, Int128? Folded) ConversionOf(this ConversionKoto node) => Query(node).ConversionOf(node);

    internal static (InvocationKoto? Entry, BoundIteration? Plan) IterationOf(this ForKoto node) => Query(node).IterationOf(node);

    internal static BoundType? ErasedTypeOf(this Koto node) => Query(node).ErasedTypeOf(node);

    // Test-only writes that stage a semantic state Binding itself would not publish; the only direct slot writes in tests.
    internal static void SetType(this Koto node, BoundType? type) => node.BoundType = type;

    internal static void SetSymbol(this Koto node, BindingSymbol? symbol) => node.BoundSymbol = symbol;

    internal static void SetConversion(this ConversionKoto node, ConversionBinding kind) => node.ConversionBinding = kind;

    private static SemanticQuery Query(Koto node) => node.CodeContext.Compilation.Semantics;
}
