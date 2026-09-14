// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class SemicolonParseTest
{
    [Theory]
    [InlineData(";")]
    [InlineData(";;")]
    [InlineData("work();")]
    [InlineData("var first = 1;var second = 2")]
    [InlineData("func run() => 1;")]
    [InlineData("func run()\n    return 1;")]
    [InlineData("if ready => 1; else => 2;")]
    [InlineData("match value\n    A => 1;\n    B => 2")]
    [InlineData("defer => work();")]
    [InlineData("unsafe => work(); other()")]
    [InlineData("unsafe\n    ;")]
    [InlineData("struct Example\n    var value: i32;")]
    [InlineData("struct Example\n    var value: i32\n        get(self: ref/Self) -> i32 => 1;")]
    [InlineData("struct Example\n    var value: i32\n        get\n            ;")]
    [InlineData("#if false\n    public struct Empty\n        ;")]
    [InlineData("#if false\n    work();")]
    [InlineData("#if false\n    #switch\n        ;\n        #case true\n            ()")]
    [InlineData("call(1; 2)")]
    [InlineData("let values = [1; 2]")]
    public void ReportsDedicatedErrorAtEverySemicolon(string source)
    {
        var tree = Parse(source);
        var diagnostics = tree.DiagnosticCollection.GetArray()
            .Where(x => x.Entry.Name == nameof(DiagnosticCode.SemicolonNotAllowed_Kd)).ToArray();
        var positions = Enumerable.Range(0, source.Length).Where(i => source[i] == ';').ToArray();
        Assert.Equal(positions.Length, diagnostics.Length);
        Assert.Equal(positions, diagnostics.Select(x => x.Span.Start));
        Assert.All(diagnostics, x =>
        {
            Assert.Equal(1, x.Span.Length);
            Assert.Equal(DiagnosticSeverity.Error, x.Entry.Severity);
        });
    }

    [Theory]
    [InlineData("var first = 1;var second = 2\nnext()")]
    [InlineData("var first = 1;;var second = 2\nnext()")]
    [InlineData("var first = 1;\nvar second = 2\nnext()")]
    public void RecoversFollowingDeclarationsAndStatements(string source)
    {
        var tree = Parse(source);
        Assert.All(tree.DiagnosticCollection.GetArray(), x => Assert.Equal(nameof(DiagnosticCode.SemicolonNotAllowed_Kd), x.Entry.Name));
        AssertItems(tree.GeneratedFunction!.Body!.Items);

        var compilation = Compilation.CreateForTest();
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(tree), ref restored);
        restored!.OnDeserialized(compilation);
        AssertItems(restored.GeneratedFunction!.Body!.Items);
        var text = restored.GeneratedFunction.ToString();
        Assert.DoesNotContain(";", text);
        var reparsed = Parse(text);
        Assert.Empty(reparsed.DiagnosticCollection.GetArray());
        AssertItems(reparsed.GeneratedFunction!.Body!.Items);
    }

    [Fact]
    public void RecoversInsideAnIndentedBlock()
    {
        var tree = Parse("loop\n    var first = 1;var second = 2\n    next()");
        Assert.Equal(nameof(DiagnosticCode.SemicolonNotAllowed_Kd), Assert.Single(tree.DiagnosticCollection.GetArray()).Entry.Name);
        AssertItems(Assert.IsType<LoopKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).Body.Items);
    }

    [Theory]
    [InlineData("let text = \";\"")]
    [InlineData("let character = ';'")]
    [InlineData("let text = \"\"\";\"\"\"")]
    [InlineData("let text = \"prefix;{value};suffix\"")]
    [InlineData("// ;\nwork()")]
    [InlineData("/* ; */ work()")]
    public void AllowsSemicolonsInLiteralContentAndComments(string source)
        => Assert.Empty(Parse(source).DiagnosticCollection.GetArray());

    private static void AssertItems(IReadOnlyList<Koto> items)
    {
        Assert.Equal(3, items.Count);
        Assert.Equal("first", Assert.IsType<FieldKoto>(items[0]).NameKoto.IdentifierName);
        Assert.Equal("second", Assert.IsType<FieldKoto>(items[1]).NameKoto.IdentifierName);
        Assert.Equal("next()", Assert.IsType<InvocationKoto>(items[2]).ToString());
    }
}
