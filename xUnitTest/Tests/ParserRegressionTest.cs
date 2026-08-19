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
        var (root, diagnostics) = Parse("func F(value: borrowref/SomeType<List<owner/T>, I>)");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var semantics = Assert.IsType<TypeSemanticsKoto>(function.Parameters.Single().Type);
        Assert.Equal(SemanticsKind.BorrowRef, semantics.SemanticsKind);
        Assert.IsType<GenericsKoto>(semantics.Type);
        Assert.Equal("borrowref/SomeType<List<owner/T>, I>", semantics.ToString());
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
