// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class FunctionBodyParseTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(GroupKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void ParsesNestedIfElseIfElseAndWhileExpressions()
    {
        var function = ParseSingleFunction(
            """
            public func Main(flag: bool) -> i32
                var x = 0
                while x < 10
                    if (x == 5)
                        break
                    else if flag
                        x += 2
                    else
                        x += 1
                if flag
                    return x
                else
                    x
            """);

        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(3, body.Items.Count);
        Assert.True(body.HasTrailingExpression);

        Assert.IsType<FieldKoto>(body.Items[0]);
        var whileExpression = Assert.IsType<WhileKoto>(body.Items[1]);
        Assert.IsType<LessThanKoto>(whileExpression.Condition);

        var loopIf = Assert.IsType<IfKoto>(Assert.Single(whileExpression.Body.Items));
        Assert.Equal(2, loopIf.Branches.Count);
        Assert.NotNull(loopIf.ElseBody);
        Assert.IsType<BreakKoto>(Assert.Single(loopIf.Branches[0].Body.Items));

        var resultIf = Assert.IsType<IfKoto>(body.TrailingExpression);
        Assert.Single(resultIf.Branches);
        Assert.NotNull(resultIf.ElseBody);
        Assert.IsType<ReturnKoto>(Assert.Single(resultIf.Branches[0].Body.Items));
        Assert.IsType<IdentifierNameKoto>(resultIf.ElseBody.TrailingExpression);

        Assert.Same(function, body.Parent);
        Assert.All(body.Items, item => Assert.Same(body, item.Parent));
    }

    [Fact]
    public void ParsesMatchArmsAsInlineExpressionsOrBlocks()
    {
        var function = ParseSingleFunction(
            """
            func Describe(x: i32) -> string
                match (x)
                    0 => "zero"
                    1 =>
                        var text = "one"
                        text
            """);

        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        var match = Assert.IsType<MatchKoto>(body.TrailingExpression);
        Assert.IsType<ParenthesizedKoto>(match.Expression);
        Assert.Equal(2, match.Arms.Count);
        Assert.IsType<StringLiteralKoto>(match.Arms[0].Body);

        var blockArm = Assert.IsType<CodeBlockKoto>(match.Arms[1].Body);
        Assert.Equal(2, blockArm.Items.Count);
        Assert.True(blockArm.HasTrailingExpression);
        Assert.IsType<IdentifierNameKoto>(blockArm.TrailingExpression);
    }

    [Fact]
    public void DistinguishesUnitAndImplicitReturnBlockEndings()
    {
        var declarationTail = ParseSingleFunction(
            """
            func DeclarationTail()
                var value = 1
            """);
        Assert.False(declarationTail.Body!.HasTrailingExpression);
        Assert.Null(declarationTail.Body.TrailingExpression);

        var assignmentTail = ParseSingleFunction(
            """
            func AssignmentTail()
                var value = 1
                value = 2
            """);
        Assert.True(assignmentTail.Body!.HasTrailingExpression);
        Assert.IsType<EqualsKoto>(assignmentTail.Body.TrailingExpression);

        var semicolonTail = ParseSingleFunction(
            """
            func SemicolonTail()
                1;
            """);
        Assert.False(semicolonTail.Body!.HasTrailingExpression);

        var localDeclarationTail = ParseSingleFunction(
            """
            func LocalDeclarationTail()
                struct Local
                    var value: i32
            """);
        Assert.False(localDeclarationTail.Body!.HasTrailingExpression);
        Assert.IsType<StructKoto>(Assert.Single(localDeclarationTail.Body.Items));
    }

    [Fact]
    public void ParsesConditionsWithOrWithoutParenthesesAndOptionalElse()
    {
        var function = ParseSingleFunction(
            """
            func Check(flag: bool)
                if (flag)
                    1
                if flag
                    continue
            """);

        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        var first = Assert.IsType<IfKoto>(body.Items[0]);
        var second = Assert.IsType<IfKoto>(body.Items[1]);
        Assert.IsType<ParenthesizedKoto>(first.Branches[0].Condition);
        Assert.IsType<IdentifierNameKoto>(second.Branches[0].Condition);
        Assert.Null(first.ElseBody);
        Assert.Null(second.ElseBody);
        Assert.IsType<ContinueKoto>(Assert.Single(second.Branches[0].Body.Items));
    }

    [Fact]
    public void RecognizesWhileAsAKeyword()
    {
        Assert.True(TokenKind.While.IsKeyword());
        Assert.Equal(Constants.WhileKeyword, TokenKind.While.ToText());
    }

    [Fact]
    public void PreservesControlFlowThroughSerializationAndUnparse()
    {
        var source = """
            func Evaluate(x: i32) -> i32
                struct Local
                    var value: i32
                while x < 0
                    break 0
                match x
                    0 => 10
                    1 =>
                        if x == 1
                            return 20
                        else
                            30
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        var serialized = TinyhandSerializer.Serialize(kotonoha);
        var deserialized = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(serialized, ref deserialized);
        var restored = deserialized ?? throw new InvalidOperationException();
        restored.OnDeserialized(compilation);

        var restoredFunction = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(restored.RootKoto)));
        var restoredBody = Assert.IsType<CodeBlockKoto>(restoredFunction.Body);
        Assert.IsType<StructKoto>(restoredBody.Items[0]);
        Assert.IsType<WhileKoto>(restoredBody.Items[1]);
        Assert.IsType<MatchKoto>(restoredBody.TrailingExpression);

        var builder = new IndentedStringBuilder();
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            var reparsedCompilation = Compilation.CreateForTest();
            var reparsed = reparsedCompilation.Kotonoha;
            var unparsed = builder.ToString();
            reparsed.CreateCodeContext().Parse(reparsed.RootKoto, unparsed);
            var diagnostics = reparsed.DiagnosticCollection.GetArray();
            Assert.True(
                diagnostics.Length == 0,
                $"{unparsed}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}"))}");
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void RecoversAfterAMissingConditionalBlock()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var source = """
            func Recover()
                if true
                var next = 1
                next
            """;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.NotEmpty(kotonoha.DiagnosticCollection.GetArray());
        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(3, body.Items.Count);
        Assert.IsType<IfKoto>(body.Items[0]);
        Assert.IsType<FieldKoto>(body.Items[1]);
        Assert.IsType<IdentifierNameKoto>(body.Items[2]);
    }

    private static FunctionKoto ParseSingleFunction(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, source);

        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        return Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
    }

    private static List<Koto> GetChildren(GroupKoto group)
        => (List<Koto>)KotoListProperty.GetValue(group)!;
}
