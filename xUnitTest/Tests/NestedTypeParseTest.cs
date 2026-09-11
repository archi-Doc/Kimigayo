// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class NestedTypeParseTest
{
    [Theory]
    [InlineData("ref/ref/i32")]
    [InlineData("ref/uniq/T")]
    [InlineData("uniq/ref/T")]
    [InlineData("ref/obj/Node")]
    [InlineData("unsafe/ref/i32")]
    [InlineData("ref/ref/ref/T from outer")]
    [InlineData("ref/(ref/T from inner) from outer")]
    [InlineData("uniq/(ref/(ref/T from a) from b) from c")]
    [InlineData("ref/(View<T> from (source => inner)) from outer")]
    [InlineData("ref/(ref/A.B<List<T>> from inner) from outer")]
    [InlineData("List<ref/(ref/i32 from inner) from outer>")]
    [InlineData("s/ref/T")]
    [InlineData("(ref/T)")]
    [InlineData("(ref/T,)")]
    [InlineData("ref/(ref/T, uniq/U)")]
    [InlineData("ref/((ref/T) -> ref/U)")]
    [InlineData("(ref/ref/T) -> ref/ref/U")]
    public void PreservesLayersAcrossDeclarationPositionsWritingAndSerialization(string type)
    {
        var tree = ParseSuccess($"""
            var local: {type}
            struct Example
                var property: {type}
                func use(value: {type}) -> {type}
                    return value
            """);
        var written = Write(tree);
        Assert.Contains(type, written);
        Assert.Equal(written, Write(ParseSuccess(written)));

        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(tree));
        Assert.NotNull(restored);
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(written, Write(restored));
        AssertTree(restored.RootKoto);
    }

    [Fact]
    public void UngroupedOriginBelongsOnlyToTheOutermostLayer()
    {
        var outer = Assert.IsType<TypeSemanticsKoto>(ParseParameterType("ref/uniq/T from outer"));
        var inner = Assert.IsType<TypeSemanticsKoto>(outer.Type);
        Assert.Equal(SemanticsKind.Ref, outer.SemanticsKind);
        Assert.Equal("outer", outer.OriginName);
        Assert.Equal(SemanticsKind.Uniq, inner.SemanticsKind);
        Assert.Null(inner.OriginExpression);
        Assert.Null(inner.OriginName);
        Assert.Equal("T", Assert.IsType<TypeSemanticsKoto>(inner.Type).Identifier);
    }

    [Fact]
    public void RetainsSeparateOriginsAndParentLinksAtEachLayer()
    {
        const string Type = "ref/(uniq/T from inner.source and other) from outer";
        var outer = Assert.IsType<TypeSemanticsKoto>(ParseParameterType(Type));
        var parentheses = Assert.IsType<ParenthesizedTypeKoto>(outer.Type);
        var inner = Assert.IsType<TypeSemanticsKoto>(parentheses.Type);
        Assert.Equal("outer", outer.OriginName);
        Assert.Equal("inner.source and other", inner.OriginExpression!.ToString());
        Assert.Equal(SemanticsKind.Uniq, parentheses.SemanticsKind);
        Assert.Same(outer, parentheses.Parent);
        Assert.Same(parentheses, inner.Parent);
        Assert.Equal(Type, outer.ToString());
        AssertTree(outer);
    }

    [Fact]
    public void GroupingDoesNotCreateASingletonTuple()
    {
        Assert.IsType<ParenthesizedTypeKoto>(ParseParameterType("(ref/T)"));
        var tuple = Assert.IsType<TupleTypeKoto>(ParseParameterType("(ref/T,)"));
        Assert.Single(tuple.Elements);
        Assert.Equal("(ref/T,)", tuple.ToString());
        Assert.Empty(Assert.IsType<TupleTypeKoto>(ParseParameterType("()")).Elements);
    }

    [Theory]
    [InlineData("ref/(i32) -> bool")]
    [InlineData("i32 -> bool")]
    [InlineData("(i32) from a -> bool")]
    public void FunctionArrowRequiresAParameterList(string type)
    {
        // A bare Type cannot replace the Function Parameter List (SPEC 3.2); recovery still keeps the arrow.
        var tree = Parse($"func use(value: {type}) => ()");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        var function = Assert.Single(tree.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>());
        Assert.IsType<FunctionTypeKoto>(Assert.Single(function.Parameters).Type);
    }

    [Fact]
    public void GroupedFunctionTypeCarriesTheOuterSemantics()
    {
        var reference = Assert.IsType<TypeSemanticsKoto>(ParseParameterType("ref/((i32) -> bool)"));
        Assert.IsType<FunctionTypeKoto>(Assert.IsType<ParenthesizedTypeKoto>(reference.Type).Type);
        Assert.IsType<FunctionTypeKoto>(ParseParameterType("(i32,) -> (bool) -> bool"));
    }

    [Theory]
    [InlineData("ref/ref/i32")]
    [InlineData("ref/(ref/i32)")]
    [InlineData("uniq/ref/A.B<List<i32>>")]
    public void AdaptationTargetRetainsAllLayersAndComparisonBoundary(string type)
    {
        var tree = ParseSuccess($"let result = value@{type} < limit");
        var field = Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items));
        var comparison = Assert.IsType<LessThanKoto>(field.InitializerKoto);
        var conversion = Assert.IsType<ConversionKoto>(comparison.Left);
        Assert.Equal(type, conversion.Right.ToString());
        Assert.Equal(Write(tree), Write(ParseSuccess(Write(tree))));
    }

    [Theory]
    [InlineData("value@ref/(ref/T from inner)")]
    [InlineData("value@Box<ref/T from inner>")]
    [InlineData("value@ref/((T) -> ref/U from inner)")]
    public void ParenthesesAndTypeArgumentsDoNotPermitOriginsInAdaptationTargets(string expression)
        => Assert.NotEmpty(Parse($"let result = {expression}").DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("ref/ref/")]
    [InlineData("ref/(ref/T from)")]
    [InlineData("ref/(ref/T,)", false)]
    public void DiagnosesIncompleteLayersWithoutLosingTheNextDeclaration(string type, bool invalid = true)
    {
        var tree = Parse($"var value: {type}\nvar after = 1");
        Assert.Equal(invalid, tree.DiagnosticCollection.GetArray().Length > 0);
        Assert.Contains(tree.GeneratedFunction!.Body!.Items.OfType<FieldKoto>(), f => f.NameKoto.IdentifierName == "after");
    }

    [Fact]
    public void SyntaxTypeFactsDoNotFlattenReferenceLayersOrTupleArity()
    {
        var types = new SyntaxControlFlowTypes();
        Assert.Equal(new ControlFlowType("i32"), types.GetDeclaredType(ParseParameterType("(i32)")));
        Assert.Null(types.GetDeclaredType(ParseParameterType("(i32,)")));
        Assert.Null(types.GetDeclaredType(ParseParameterType("ref/ref/i32")));
        Assert.Equal(new ControlFlowType("unsafe/unsafe/i32"), types.GetDeclaredType(ParseParameterType("unsafe/(unsafe/i32)")));
        Assert.Null(types.GetDeclaredType(ParseParameterType("unsafe/(unsafe/i32 from inner)")));
    }

    [Theory]
    [InlineData("unsafe/ref/T", true)]
    [InlineData("(unsafe/ref/T)", true)]
    [InlineData("owner/(unsafe/ref/T)", true)]
    [InlineData("ref/unsafe/T", false)]
    public void GroupingCannotHideKnownUnsafeTargetSemantics(string type, bool requiresUnsafe)
    {
        var tree = ParseSuccess($"let result = value@{type}");
        var conversion = Assert.IsType<ConversionKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        Assert.Equal(requiresUnsafe, new SyntaxControlFlowTypes().RequiresUnsafeContext(conversion) == true);
    }

    private static Koto ParseParameterType(string type)
        => Assert.Single(ParseSingleFunction($"func use(value: {type}) => ()").Parameters).Type!;

    private static void AssertTree(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            Assert.Same(node, child.Parent);
            if (node is TypeKoto)
            {
                Assert.InRange(child.Span.Start, node.Span.Start, node.Span.End);
                Assert.InRange(child.Span.End, child.Span.Start, node.Span.End);
            }

            AssertTree(child);
        }
    }

    private static string Write(Kotonoha tree)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
