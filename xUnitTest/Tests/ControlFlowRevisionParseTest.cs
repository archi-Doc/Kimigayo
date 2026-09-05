// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ControlFlowRevisionParseTest
{
    [Fact]
    public void RecognizesExitWithoutReservingBreakOrFrom()
    {
        Assert.Equal(TokenKind.Exit, TokenHelper.GetKeywordOrIdentifierKind("exit"));
        Assert.Equal("exit", TokenKind.Exit.ToText());
        Assert.True(TokenKind.Exit.IsKeyword());
        Assert.Equal(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind("break"));
        Assert.Equal(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind("from"));
        Parse("var break = 1");
    }

    [Fact]
    public void ParsesLabelsAndKeepsResultOperandsSeparateFromTargets()
    {
        var parsed = Parse(
            """
            func run() -> i32
                work:
                    exit from work
                outer: for item in values
                    retry: while ready
                        continue outer
                        exit from retry
                return search: loop
                    block:
                        exit value
                        exit value + 1 from search
            """);
        foreach (var tree in Versions(parsed))
        {
            var function = Function(tree);
            var body = function.Body!;
            Assert.False(body.HasTrailingExpression);
            var work = Assert.IsType<LabeledKoto>(body.Items[0]);
            Assert.Equal("work", work.Label);
            var workBody = Assert.IsType<CodeBlockKoto>(work.Target);
            var blockExit = Assert.IsType<ExitKoto>(Assert.Single(workBody.Items));
            Assert.Null(blockExit.Expression);
            Assert.Equal("work", blockExit.Label);
            Assert.False(workBody.HasTrailingExpression);
            var outer = Assert.IsType<LabeledKoto>(body.Items[1]);
            var forLoop = Assert.IsType<ForKoto>(outer.Target);
            var retry = Assert.IsType<LabeledKoto>(Assert.Single(forLoop.Body.Items));
            var whileLoop = Assert.IsType<WhileKoto>(retry.Target);
            Assert.Equal("outer", Assert.IsType<ContinueKoto>(whileLoop.Body.Items[0]).Label);
            Assert.Equal("retry", Assert.IsType<ExitKoto>(whileLoop.Body.Items[1]).Label);

            var result = Assert.IsType<LabeledKoto>(Assert.IsType<ReturnKoto>(body.Items[2]).Expression);
            var loop = Assert.IsType<LoopKoto>(result.Target);
            Assert.True(KotoHelper.IsValueContext(loop));
            var block = Assert.IsType<CodeBlockKoto>(Assert.IsType<LabeledKoto>(Assert.Single(loop.Body.Items)).Target);
            var plainExit = Assert.IsType<ExitKoto>(block.Items[0]);
            Assert.Null(plainExit.Label);
            Assert.IsType<IdentifierNameKoto>(plainExit.Expression);
            var namedExit = Assert.IsType<ExitKoto>(block.Items[1]);
            Assert.Equal("search", namedExit.Label);
            Assert.IsType<PlusKoto>(namedExit.Expression);
            Assert.Same(block, namedExit.Parent);
            Assert.Same(namedExit, namedExit.Expression!.Parent);
        }
    }

    [Fact]
    public void PreservesExpressionBodiesForNamedAndAnonymousFunctions()
    {
        var parsed = Parse(
            """
            func run() -> i32
                func add(a: i32, b: i32) -> i32 => a + b
                let increment = func (x: i32) -> i32 => x + 1
                let block = func () -> i32
                    return 2
                return add(increment(1), block())
            """);
        foreach (var tree in Versions(parsed))
        {
            var body = Function(tree).Body!;
            var add = Assert.IsType<FunctionKoto>(body.Items[0]);
            Assert.Null(add.Body);
            Assert.IsType<PlusKoto>(add.ExpressionBody);
            Assert.True(KotoHelper.IsValueContext(add.ExpressionBody!));
            Assert.Same(add, add.ExpressionBody!.Parent);
            var increment = Assert.IsType<FunctionKoto>(Assert.IsType<FieldKoto>(body.Items[1]).InitializerKoto);
            Assert.Equal(string.Empty, increment.Name);
            Assert.IsType<PlusKoto>(increment.ExpressionBody);
            var block = Assert.IsType<FunctionKoto>(Assert.IsType<FieldKoto>(body.Items[2]).InitializerKoto);
            Assert.Null(block.ExpressionBody);
            Assert.IsType<ReturnKoto>(Assert.Single(block.Body!.Items));
            Assert.False(block.Body.HasTrailingExpression);
        }

        Parse("group Math\n    func add(a: i32, b: i32) -> i32 => a + b");
        Parse("struct Math\n    func add(a: i32, b: i32) -> i32 => a + b");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DerivesNestedValueContextFromDirectBranchElements(bool addStatement)
    {
        var source = "func run()\n    let result = if a\n        if b\n            yield 1\n        else\n            yield 2\n" +
            (addStatement ? "        log()\n        yield 3\n" : string.Empty) + "    else\n        0";
        foreach (var tree in Versions(Parse(source)))
        {
            var outer = Assert.IsType<IfKoto>(Assert.IsType<FieldKoto>(Assert.Single(Function(tree).Body!.Items)).InitializerKoto);
            var branch = outer.Branches[0].Body;
            var inner = Assert.IsType<IfKoto>(branch.Items[0]);
            Assert.True(KotoHelper.IsValueContext(outer));
            Assert.Equal(!addStatement, KotoHelper.IsValueContext(inner));
            Assert.Equal(!addStatement, branch.HasTrailingExpression);
            Assert.Equal(!addStatement, inner.Branches[0].Body.HasTrailingExpression);
        }
    }

    [Fact]
    public void PreservesBranchSemicolonsThroughSerializationAndUnparse()
    {
        var parsed = Parse(
            """
            func run()
                let first = if a
                    if b
                        1
                    else
                        2;
                else
                    3;
                let second = match a
                    A => if b 1 else 2;
                    B =>
                        3;
                return ()
            """);
        foreach (var tree in Versions(parsed))
        {
            var body = Function(tree).Body!;
            var first = Assert.IsType<IfKoto>(Assert.IsType<FieldKoto>(body.Items[0]).InitializerKoto);
            Assert.True(first.ElseBody!.HasTrailingSemicolon);
            Assert.False(first.ElseBody.HasTrailingExpression);
            var nested = Assert.IsType<IfKoto>(first.Branches[0].Body.Items[0]);
            Assert.True(nested.ElseBody!.HasTrailingSemicolon);
            var second = Assert.IsType<MatchKoto>(Assert.IsType<FieldKoto>(body.Items[1]).InitializerKoto);
            Assert.IsType<IfKoto>(second.Arms[0].Body is ParenthesizedKoto grouped ? grouped.Operand : second.Arms[0].Body);
            Assert.True(second.Arms[0].HasTrailingSemicolon);
            Assert.False(KotoHelper.IsValueContext(second.Arms[0].Body));
            var blockArm = Assert.IsType<CodeBlockKoto>(second.Arms[1].Body);
            Assert.True(blockArm.HasTrailingSemicolon);
            Assert.False(blockArm.HasTrailingExpression);
            Assert.IsType<UnitLiteralKoto>(Assert.IsType<ReturnKoto>(body.Items[2]).Expression);
        }
    }

    [Fact]
    public void InlineMatchSemicolonDiscardsNestedConstructResult()
    {
        foreach (var tree in Versions(Parse("func run() => match x\n    A => (if b 1 else 2);\n    B => ()")))
        {
            var match = Assert.IsType<MatchKoto>(Function(tree).ExpressionBody);
            Assert.True(match.Arms[0].HasTrailingSemicolon);
            var inner = Assert.IsType<IfKoto>(Assert.IsType<ParenthesizedKoto>(match.Arms[0].Body).Operand);
            Assert.False(KotoHelper.IsValueContext(inner));
            Assert.False(inner.Branches[0].Body.HasTrailingExpression);
        }
    }

    [Fact]
    public void DistinguishesDictionaryKeysAndConversionTypesFromLabels()
    {
        foreach (var tree in Versions(Parse("func run()\n    let map = [key: 1, other: 2]\n    return outer: loop\n        exit value@i32 from outer")))
        {
            var body = Function(tree).Body!;
            Assert.IsType<DictionaryLiteralKoto>(Assert.IsType<FieldKoto>(body.Items[0]).InitializerKoto);
            var label = Assert.IsType<LabeledKoto>(Assert.IsType<ReturnKoto>(body.Items[1]).Expression);
            var exit = Assert.IsType<ExitKoto>(Assert.Single(Assert.IsType<LoopKoto>(label.Target).Body.Items));
            Assert.Equal("outer", exit.Label);
            Assert.IsType<ConversionKoto>(exit.Expression);
        }
    }

    [Theory]
    [InlineData("exit from")]
    [InlineData("exit from 1")]
    [InlineData("exit 1 from")]
    [InlineData("continue 10")]
    [InlineData("yield")]
    [InlineData("work: if a 1 else 2")]
    [InlineData("work:")]
    [InlineData("func nested() =>")]
    public void ReportsMalformedSyntaxAndPreservesFollowingStatement(string malformed)
    {
        var compilation = Compilation.CreateForTest();
        var tree = compilation.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, "func run()\n    " + malformed + "\n    return ()");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        Assert.IsType<ReturnKoto>(Function(tree).Body!.Items[^1]);
    }

    [Fact]
    public void ParsesExplicitUnitTransferOperands()
    {
        foreach (var tree in Versions(Parse("func run()\n    let result = if a\n        yield ()\n    else\n        loop\n            exit ()\n    return ()")))
        {
            var body = Function(tree).Body!;
            var conditional = Assert.IsType<IfKoto>(Assert.IsType<FieldKoto>(body.Items[0]).InitializerKoto);
            Assert.IsType<UnitLiteralKoto>(Assert.IsType<YieldKoto>(Assert.Single(conditional.Branches[0].Body.Items)).Expression);
            var loop = Assert.IsType<LoopKoto>(conditional.ElseBody!.TrailingExpression);
            Assert.IsType<UnitLiteralKoto>(Assert.IsType<ExitKoto>(Assert.Single(loop.Body.Items)).Expression);
        }
    }

    [Fact]
    public void PreservesExplicitLabelAfterACompoundOperand()
    {
        foreach (var tree in Versions(Parse("func run() => outer: loop\n    exit (if flag 1 else 2) from outer")))
        {
            var labeled = Assert.IsType<LabeledKoto>(Function(tree).ExpressionBody);
            var loop = Assert.IsType<LoopKoto>(labeled.Target);
            var exit = Assert.IsType<ExitKoto>(Assert.Single(loop.Body.Items));
            Assert.Equal("outer", exit.Label);
            var conditional = Assert.IsType<IfKoto>(Assert.IsType<ParenthesizedKoto>(exit.Expression).Operand);
            Assert.True(KotoHelper.IsValueContext(conditional));
        }
    }

    [Fact]
    public void PreservesSemicolonOnAWholeNestedIf()
    {
        foreach (var tree in Versions(Parse("func run() => if flag\n    if other 1 else 2;\nelse\n    ()")))
        {
            var outer = Assert.IsType<IfKoto>(Function(tree).ExpressionBody);
            var branch = outer.Branches[0].Body;
            Assert.True(branch.HasTrailingSemicolon);
            Assert.False(branch.HasTrailingExpression);
            var inner = branch.Items[0] is ParenthesizedKoto parentheses ? parentheses.Operand : branch.Items[0];
            Assert.False(KotoHelper.IsValueContext(Assert.IsType<IfKoto>(inner)));
        }
    }

    [Fact]
    public void KeepsLoopBodiesInStatementContext()
    {
        var tree = Parse("func run()\n    loop\n        if flag 1 else 2\n    let result = loop\n        exit 1");
        var body = Function(tree).Body!;
        var loop = Assert.IsType<LoopKoto>(body.Items[0]);
        Assert.False(KotoHelper.IsValueContext(loop));
        var conditional = Assert.IsType<IfKoto>(Assert.Single(loop.Body.Items));
        Assert.False(KotoHelper.IsValueContext(conditional));
        Assert.False(conditional.Branches[0].Body.HasTrailingExpression);
        var valueLoop = Assert.IsType<LoopKoto>(Assert.IsType<FieldKoto>(body.Items[1]).InitializerKoto);
        Assert.True(KotoHelper.IsValueContext(valueLoop));
        Assert.False(valueLoop.Body.HasTrailingExpression);
    }

    private static FunctionKoto Function(Kotonoha tree)
        => Assert.IsType<FunctionKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items));

    private static Kotonoha Parse(string source)
    {
        var tree = Compilation.CreateForTest().Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        var diagnostics = tree.DiagnosticCollection.GetArray();
        Assert.True(diagnostics.Length == 0, source + "\n" + string.Join("\n", diagnostics.Select(x => x.Message)));
        return tree;
    }

    private static IEnumerable<Kotonoha> Versions(Kotonoha parsed)
    {
        yield return parsed;
        var compilation = Compilation.CreateForTest();
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(parsed), ref restored);
        restored!.OnDeserialized(compilation);
        yield return restored;
        var builder = new IndentedStringBuilder();
        string source;
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            source = builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }

        yield return Parse(source);
    }
}
