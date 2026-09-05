// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ControlTransferParseTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(DeclarationContainerKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void ParsesEachConstructAndControlTransferKeywordPair()
    {
        var function = ParseSingleFunction(
            """
            func Control(value: i32, values: Values) -> i32
                if value < 0
                    return -1

                for item in values
                    if done(item)
                        exit

                    continue

                while ready()
                    if done()
                        exit

                    continue

                var loopResult = loop
                    if retry()
                        continue

                    exit 10

                var ifResult = if flag()
                    trace()
                    yield 20
                else
                    30

                var matchResult = match value
                    0 =>
                        trace()
                        yield 40
                    1 => 50

                return loopResult
            """);

        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(7, body.Items.Count);

        var returnIf = Assert.IsType<IfKoto>(body.Items[0]);
        var earlyReturn = Assert.IsType<ReturnKoto>(Assert.Single(returnIf.Branches[0].Body.Items));
        Assert.IsType<PrefixMinusKoto>(earlyReturn.Expression);

        var forExpression = Assert.IsType<ForKoto>(body.Items[1]);
        var forIf = Assert.IsType<IfKoto>(forExpression.Body.Items[0]);
        Assert.IsType<ExitKoto>(Assert.Single(forIf.Branches[0].Body.Items));
        Assert.IsType<ContinueKoto>(forExpression.Body.Items[1]);

        var whileExpression = Assert.IsType<WhileKoto>(body.Items[2]);
        var whileIf = Assert.IsType<IfKoto>(whileExpression.Body.Items[0]);
        Assert.IsType<ExitKoto>(Assert.Single(whileIf.Branches[0].Body.Items));
        Assert.IsType<ContinueKoto>(whileExpression.Body.Items[1]);

        var loopField = Assert.IsType<FieldKoto>(body.Items[3]);
        var loopExpression = Assert.IsType<LoopKoto>(loopField.InitializerKoto);
        var loopIf = Assert.IsType<IfKoto>(loopExpression.Body.Items[0]);
        Assert.IsType<ContinueKoto>(Assert.Single(loopIf.Branches[0].Body.Items));
        var valueExit = Assert.IsType<ExitKoto>(loopExpression.Body.Items[1]);
        Assert.IsType<NumberLiteralKoto>(valueExit.Expression);

        var ifField = Assert.IsType<FieldKoto>(body.Items[4]);
        var valueIf = Assert.IsType<IfKoto>(ifField.InitializerKoto);
        var ifYield = Assert.IsType<YieldKoto>(valueIf.Branches[0].Body.Items[1]);
        Assert.IsType<NumberLiteralKoto>(ifYield.Expression);

        var matchField = Assert.IsType<FieldKoto>(body.Items[5]);
        var valueMatch = Assert.IsType<MatchKoto>(matchField.InitializerKoto);
        var matchArm = Assert.IsType<CodeBlockKoto>(valueMatch.Arms[0].Body);
        var matchYield = Assert.IsType<YieldKoto>(matchArm.Items[1]);
        Assert.IsType<NumberLiteralKoto>(matchYield.Expression);

        var finalReturn = Assert.IsType<ReturnKoto>(body.Items[6]);
        Assert.IsType<IdentifierNameKoto>(finalReturn.Expression);

        Assert.Same(loopExpression, loopExpression.Body.Parent);
        Assert.Same(valueIf.Branches[0].Body, ifYield.Parent);
        Assert.Same(ifYield, ifYield.Expression.Parent);
        Assert.Same(matchArm, matchYield.Parent);
        Assert.Same(matchYield, matchYield.Expression.Parent);
    }

    [Fact]
    public void PreservesLoopAndYieldThroughSerializationAndUnparse()
    {
        const string Source = """
            func Preserve(value: i32) -> i32
                var selected = if value > 0
                    loop
                        if retry()
                            continue

                        exit 1
                else
                    0

                return match value
                    0 =>
                        yield selected
                    1 => 2
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var deserialized = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref deserialized);
        var restored = deserialized ?? throw new InvalidOperationException();
        restored.OnDeserialized(compilation);

        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(restored.RootKoto)));
        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        var selected = Assert.IsType<FieldKoto>(body.Items[0]);
        var valueIf = Assert.IsType<IfKoto>(selected.InitializerKoto);
        var loop = Assert.IsType<LoopKoto>(valueIf.Branches[0].Body.TrailingExpression);
        Assert.Same(loop, loop.Body.Parent);

        var valueMatch = Assert.IsType<MatchKoto>(Assert.IsType<ReturnKoto>(body.Items[^1]).Expression);
        var firstArm = Assert.IsType<CodeBlockKoto>(valueMatch.Arms[0].Body);
        var yield = Assert.IsType<YieldKoto>(Assert.Single(firstArm.Items));
        Assert.Same(yield, yield.Expression.Parent);

        var builder = new IndentedStringBuilder();
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            var unparsed = builder.ToString();
            Assert.Contains("loop", unparsed, StringComparison.Ordinal);
            Assert.Contains("yield selected", unparsed, StringComparison.Ordinal);

            var reparsedCompilation = Compilation.CreateForTest();
            var reparsed = reparsedCompilation.Kotonoha;
            reparsed.CreateCodeContext().Parse(reparsed.RootKoto, unparsed);
            Assert.Empty(reparsed.DiagnosticCollection.GetArray());
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void RequiresYieldOperandAndRecoversAtTheNextStatement()
    {
        const string Source = """
            func Recover()
                var selected = if true
                    yield
                else
                    0

                return 1
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);

        Assert.NotEmpty(kotonoha.DiagnosticCollection.GetArray());
        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(2, body.Items.Count);

        var field = Assert.IsType<FieldKoto>(body.Items[0]);
        var valueIf = Assert.IsType<IfKoto>(field.InitializerKoto);
        var yield = Assert.IsType<YieldKoto>(Assert.Single(valueIf.Branches[0].Body.Items));
        Assert.IsType<ErrorKoto>(yield.Expression);
        Assert.IsType<ReturnKoto>(body.Items[1]);
    }

    [Fact]
    public void RecognizesLoopAndYieldAsKeywords()
    {
        Assert.True(TokenKind.Loop.IsKeyword());
        Assert.Equal(Constants.LoopKeyword, TokenKind.Loop.ToText());
        Assert.True(TokenKind.Yield.IsKeyword());
        Assert.Equal(Constants.YieldKeyword, TokenKind.Yield.ToText());
    }

    private static FunctionKoto ParseSingleFunction(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        return Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
    }

    private static List<Koto> GetChildren(DeclarationContainerKoto group)
        => ReferenceEquals(group, group.Kotonoha.RootKoto)
            ? group.Kotonoha.GeneratedFunction?.Body?.Items.ToList() ?? []
            : (List<Koto>)KotoListProperty.GetValue(group)!;
}
