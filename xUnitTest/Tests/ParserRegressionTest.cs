// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ParserRegressionTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(GroupKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void StopsAtEndOfIncompleteExpression()
    {
        var (_, diagnostics) = Parse("var x = 1 +");

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void ParsesLessThanAsComparison()
    {
        var (root, diagnostics) = Parse("var x = a < b");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        Assert.IsType<LessThanKoto>(field.InitializerKoto);
    }

    [Fact]
    public void BindsMemberAccessBeforeAddition()
    {
        var (root, diagnostics) = Parse("var x = a.b + c");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.IsType<MemberAccessKoto>(addition.Left);
    }

    [Fact]
    public void ParsesLogicalExpressionsWithCorrectPrecedence()
    {
        var source = """
            var first = A and B
            var second = not A or B
            var third = A and not B
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var fields = GetChildren(root).Select(Assert.IsType<FieldKoto>).ToArray();

        var first = Assert.IsType<AndKoto>(fields[0].InitializerKoto);
        Assert.IsType<IdentifierNameKoto>(first.Left);
        Assert.IsType<IdentifierNameKoto>(first.Right);

        var second = Assert.IsType<OrKoto>(fields[1].InitializerKoto);
        Assert.IsType<NotKoto>(second.Left);
        Assert.IsType<IdentifierNameKoto>(second.Right);

        var third = Assert.IsType<AndKoto>(fields[2].InitializerKoto);
        Assert.IsType<IdentifierNameKoto>(third.Left);
        Assert.IsType<NotKoto>(third.Right);
    }

    [Fact]
    public void ParsesLogicalExpressionsAsIsRightOperand()
    {
        var source = """
            var first = X is A and B
            var second = X is not A or B
            var third = X is A and not B
            var fourth = P or X is A and B
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var fields = GetChildren(root).Select(Assert.IsType<FieldKoto>).ToArray();

        var first = Assert.IsType<IsKoto>(fields[0].InitializerKoto);
        Assert.IsType<AndKoto>(first.Right);

        var second = Assert.IsType<IsKoto>(fields[1].InitializerKoto);
        var secondCondition = Assert.IsType<NotKoto>(second.Right);
        Assert.IsType<OrKoto>(secondCondition.Operand);

        var third = Assert.IsType<IsKoto>(fields[2].InitializerKoto);
        var thirdCondition = Assert.IsType<AndKoto>(third.Right);
        Assert.IsType<NotKoto>(thirdCondition.Right);

        var fourth = Assert.IsType<OrKoto>(fields[3].InitializerKoto);
        var fourthCondition = Assert.IsType<IsKoto>(fourth.Right);
        Assert.IsType<AndKoto>(fourthCondition.Right);
    }

    [Fact]
    public void ContinuesAfterOuterIndentedClosingDelimiterOnSameLine()
    {
        var source = """
            var x = foo(
                a
            ) + 1
            """;

        var (root, diagnostics) = Parse(source);

        // Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.IsType<InvocationKoto>(addition.Left);
    }

    [Fact]
    public void ParsesGroupBody()
    {
        var (root, diagnostics) = Parse("group A\n    var x = 1");

        Assert.Empty(diagnostics);
        var group = root.GetOrAddGroup("A", TokenKind.Group, default, default);
        Assert.Equal("A", group.Name);
        Assert.IsType<FieldKoto>(GetChildren(group).Single());
    }

    [Fact]
    public void AppliesSemanticsToCompoundType()
    {
        var (root, diagnostics) = Parse("func F(value: objref/SomeType<List<owner/T>, I>)");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var semantics = Assert.IsType<TypeSemanticsKoto>(function.Parameters.Single().Type);
        Assert.Equal(SemanticsKind.ObjRef, semantics.SemanticsKind);
        Assert.IsType<GenericsKoto>(semantics.Type);
        Assert.Equal("objref/SomeType<List<owner/T>, I>", semantics.ToString());
    }

    [Fact]
    public void ParsesHierarchicalTypeConstraints()
    {
        var source = """
            public open struct A<s/T>
                Self is StructB and InterfaceA
                semantics is not valueborrow and (owning or objectborrow)
                s is reference
                T is Comparable and (Equatable or Serializable)

                var x: i32
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));

        var genericArgument = Assert.Single(type.GenericArguments);
        Assert.Equal("s", genericArgument.SemanticsParameter);
        Assert.Equal("T", genericArgument.Identifier);

        Assert.Equal(4, type.TypeConstraints.Count);

        var selfConstraint = type.TypeConstraints[0];
        Assert.Equal("Self", Assert.IsType<IdentifierNameKoto>(selfConstraint.Left).IdentifierName);
        var selfTypes = Assert.IsType<AndKoto>(selfConstraint.Right);
        Assert.Equal("StructB", Assert.IsType<IdentifierNameKoto>(selfTypes.Left).IdentifierName);
        Assert.Equal("InterfaceA", Assert.IsType<IdentifierNameKoto>(selfTypes.Right).IdentifierName);

        var semanticsConstraint = type.TypeConstraints[1];
        Assert.Equal("semantics", Assert.IsType<IdentifierNameKoto>(semanticsConstraint.Left).IdentifierName);
        var negation = Assert.IsType<NotKoto>(semanticsConstraint.Right);
        var semanticsAnd = Assert.IsType<AndKoto>(negation.Operand);
        Assert.Equal(SemanticsMask.ValueBorrow, Assert.IsType<SemanticsMaskKoto>(semanticsAnd.Left).Mask);
        var parentheses = Assert.IsType<ParenthesizedKoto>(semanticsAnd.Right);
        var semanticsOr = Assert.IsType<OrKoto>(parentheses.Operand);
        Assert.Equal(SemanticsMask.Owning, Assert.IsType<SemanticsMaskKoto>(semanticsOr.Left).Mask);
        Assert.Equal(SemanticsMask.ObjectBorrow, Assert.IsType<SemanticsMaskKoto>(semanticsOr.Right).Mask);

        var semanticsParameterConstraint = type.TypeConstraints[2];
        Assert.Equal("s", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Left).IdentifierName);
        Assert.Equal("reference", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Right).IdentifierName);

        var typeParameterConstraint = type.TypeConstraints[3];
        Assert.Equal("T", Assert.IsType<IdentifierNameKoto>(typeParameterConstraint.Left).IdentifierName);
        var typeAnd = Assert.IsType<AndKoto>(typeParameterConstraint.Right);
        Assert.IsType<IdentifierNameKoto>(typeAnd.Left);
        Assert.IsType<ParenthesizedKoto>(typeAnd.Right);

        Assert.IsType<FieldKoto>(GetChildren(type).Single());

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("public open struct A<s/T>", text);
            Assert.Contains("Self is StructB and InterfaceA", text);
            Assert.Contains("semantics is not valueborrow and (owning or objectborrow)", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ParsesOriginConstraintForSemanticsGenericArgument()
    {
        var source = """
            public open struct TestStruct<s/C>
                origin is a and b
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("TestStruct", TokenKind.Struct, default, default));

        var genericArgument = Assert.Single(type.GenericArguments);
        Assert.Equal("s", genericArgument.SemanticsParameter);
        Assert.Equal("C", genericArgument.Identifier);

        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal("origin", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        var origins = Assert.IsType<AndKoto>(constraint.Right);
        Assert.Equal("a", Assert.IsType<IdentifierNameKoto>(origins.Left).IdentifierName);
        Assert.Equal("b", Assert.IsType<IdentifierNameKoto>(origins.Right).IdentifierName);

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("public open struct TestStruct<s/C>", text);
            Assert.Contains("origin is a and b", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void IgnoresTypeConstraintsFromLaterStructDefinitions()
    {
        var source = """
            struct A<s/T>
                origin is first and shared
                T is FirstConstraint
                var first: i32

            struct A
                origin is ignored and later
                T is IgnoredConstraint
                semantics is DefinitelyInvalid
                var second: i32
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.Collection(
            type.TypeConstraints,
            constraint =>
            {
                Assert.Equal("origin", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
                var origins = Assert.IsType<AndKoto>(constraint.Right);
                Assert.Equal("first", Assert.IsType<IdentifierNameKoto>(origins.Left).IdentifierName);
                Assert.Equal("shared", Assert.IsType<IdentifierNameKoto>(origins.Right).IdentifierName);
            },
            constraint =>
            {
                Assert.Equal("T", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
                Assert.Equal("FirstConstraint", Assert.IsType<IdentifierNameKoto>(constraint.Right).IdentifierName);
            });
        Assert.Equal(2, GetChildren(type).OfType<FieldKoto>().Count());

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("origin is first and shared", text);
            Assert.Contains("T is FirstConstraint", text);
            Assert.DoesNotContain("ignored", text);
            Assert.DoesNotContain("IgnoredConstraint", text);
            Assert.DoesNotContain("DefinitelyInvalid", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void DiagnosesGroupDeclarationTrailingSyntaxOnce()
    {
        var source = """
            struct A: InterfaceA, InterfaceB
                Self is InterfaceA
            """;

        var (root, diagnostics) = Parse(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnexpectedTrailingToken_Kd), diagnostic.Entry.Name);

        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
    }

    [Fact]
    public void ParsesOrderedTypeDeclarationsWithoutOrderWarning()
    {
        var source = """
            struct Ordered
                Self is Interface

                struct Nested
                    var nestedField: i32

                var field: i32

                func Method()
                    return
            """;

        var (root, diagnostics) = Parse(source);

        Assert.DoesNotContain(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.DeclarationOrderWarning_Kd));
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("Ordered", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
        Assert.Collection(
            GetChildren(type),
            x => Assert.IsType<FieldKoto>(x),
            x => Assert.IsType<FunctionKoto>(x));

        var nested = Assert.IsType<StructKoto>(type.GetOrAddGroup("Nested", TokenKind.Struct, default, default));
        Assert.IsType<FieldKoto>(Assert.Single(GetChildren(nested)));
    }

    [Fact]
    public void WarnsForOutOfOrderTypeDeclarationsButParsesThem()
    {
        var source = """
            struct Mixed
                func Method()
                    return

                var field: i32

                struct Nested
                    var nestedField: i32

                Self is Interface
            """;

        var (root, diagnostics) = Parse(source);

        var warnings = diagnostics
            .Where(x => x.Entry.Name == nameof(DiagnosticCode.DeclarationOrderWarning_Kd))
            .ToArray();
        Assert.Equal(3, warnings.Length);
        Assert.All(warnings, x => Assert.Equal(DiagnosticSeverity.Warning, x.Entry.Severity));

        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("Mixed", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
        Assert.Collection(
            GetChildren(type),
            x => Assert.IsType<FunctionKoto>(x),
            x => Assert.IsType<FieldKoto>(x));

        var nested = Assert.IsType<StructKoto>(type.GetOrAddGroup("Nested", TokenKind.Struct, default, default));
        Assert.IsType<FieldKoto>(Assert.Single(GetChildren(nested)));
    }

    [Fact]
    public void ParsesIdentifierExpressionInTypeBody()
    {
        var source = """
            struct A
                Field1.Method2()
                var field: i32
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.Collection(
            GetChildren(type),
            x =>
            {
                var invocation = Assert.IsType<InvocationKoto>(x);
                Assert.IsType<MemberAccessKoto>(invocation.Method);
            },
            x => Assert.IsType<FieldKoto>(x));
    }

    [Theory]
    [InlineData("owner", SemanticsMask.Owner)]
    [InlineData("ref", SemanticsMask.Ref)]
    [InlineData("uniq", SemanticsMask.Uniq)]
    [InlineData("obj", SemanticsMask.Obj)]
    [InlineData("rc", SemanticsMask.Rc)]
    [InlineData("arc", SemanticsMask.Arc)]
    [InlineData("objref", SemanticsMask.ObjRef)]
    [InlineData("objuniq", SemanticsMask.ObjUniq)]
    [InlineData("unsafe", SemanticsMask.Unsafe)]
    [InlineData("valueborrow", SemanticsMask.ValueBorrow)]
    [InlineData("object", SemanticsMask.Object)]
    [InlineData("objectborrow", SemanticsMask.ObjectBorrow)]
    [InlineData("borrow", SemanticsMask.Borrow)]
    [InlineData("owning", SemanticsMask.Owning)]
    [InlineData("value", SemanticsMask.Value)]
    [InlineData("reference", SemanticsMask.Reference)]
    public void ParsesNamedSemanticsConstraints(string text, SemanticsMask expected)
    {
        var (root, diagnostics) = Parse($"struct A\n    semantics is {text}");

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal(expected, Assert.IsType<SemanticsMaskKoto>(constraint.Right).Mask);
    }

    [Fact]
    public void DiagnosesInvalidSemanticsConstraint()
    {
        var (root, diagnostics) = Parse("struct A\n    semantics is Comparable");

        Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.InvalidSemanticsConstraint_Kd));
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.IsType<ErrorKoto>(constraint.Right);
    }

    [Fact]
    public void RemovesAttributeBeyondChainHead()
    {
        var (root, diagnostics) = Parse("#A\n#B\nvar x = 1");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var head = Assert.IsType<AttributeKoto>(field.AttributeChain);
        var tail = Assert.IsType<AttributeKoto>(head.AttributeChain);

        Assert.True(field.RemoveAttribute(tail));
        Assert.Null(head.AttributeChain);
        Assert.Null(tail.Parent);
        Assert.Null(tail.AttributeChain);
        Assert.False(field.RemoveAttribute(tail));
    }

    [Fact]
    public void DeserializesFullDocumentChangeWithoutRange()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var change = JsonSerializer.Deserialize<TextDocumentContentChangeEvent>("{\"text\":\"replacement\"}", options);

        Assert.NotNull(change);
        Assert.Null(change.Range);
        Assert.Equal("replacement", change.Text);
    }

    private static (GroupKoto Root, Diagnostic[] Diagnostics) Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, source);
        return (kotonoha.RootKoto, kotonoha.DiagnosticCollection.GetArray());
    }

    private static List<Koto> GetChildren(GroupKoto group)
        => (List<Koto>)KotoListProperty.GetValue(group)!;
}
