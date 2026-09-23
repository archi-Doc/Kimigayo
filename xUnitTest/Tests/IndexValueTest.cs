// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IndexValueTest
{
    [Theory]
    [InlineData("Constructor", "let first = Index.init(3)\nlet last = Index.init(2, fromEnd: true)\nrequire first.offset == 3 and not first.isFromEnd and last.offset == 2 and last.isFromEnd else => $abort(\"index\")")]
    [InlineData("Prefix", "let n: isize = 2\nlet last: Index = ^(n + 1)\nlet copy = last\nrequire last.offset == 3 and copy.offset == 3 and copy.isFromEnd else => $abort(\"index\")")]
    [InlineData("Passing", "func keep(index: Index) -> Index => index\nlet last = keep(^1)\nlet start = keep(Index.init(0))\nrequire last.offset == 1 and last.isFromEnd and start.offset == 0 and not start.isFromEnd else => $abort(\"index\")")]
    [InlineData("Shadow", "struct Index\n    public let different: i32 = 7\nlet last: ::Kimi.Index = ^1\nrequire last.offset == 1 and last.isFromEnd else => $abort(\"index\")")]
    public void ConstructsStoresAndPassesIndices(string name, string source)
        => ScalarEmissionTest.EmitFixture("IndexValue" + name, source, string.Empty);

    [Theory]
    [InlineData("let n: i32 = 1\nlet index = ^n")]
    [InlineData("let index = ^true")]
    [InlineData("let index: isize = ^1")]
    [InlineData("let index = Index.init(true)")]
    public void RejectsWrongOffsetTypes(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void NegativePrefixOffsetAbortsAtConstruction()
        => ScalarEmissionTest.EmitFixture(
            "IndexValueNegativePrefix",
            "Console.writeLine(\"before\")\nlet index = ^(-1)\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:13: abort KIMI_E_ARGUMENT: Invalid argument value\n");

    [Fact]
    public void NegativeConstructorOffsetAbortsBeforeLaterStatements()
        => ScalarEmissionTest.EmitFixture(
            "IndexValueNegativeConstructor",
            "Console.writeLine(\"before\")\nlet index = Index.init(-1)\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/Core.kimi:20:37: abort KIMI_E_ABORT: Negative Index offset\n");

    [Fact]
    public void CatalogRejectsAdditionalStorage()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.Index));
        var index = Assert.IsType<StructKoto>(c.Library.GetSymbol(KimiDeclarationId.Index)!.Declaration);
        c.Library.Kotonoha.CreateCodeContext().Parse(index, "public let extra: i32 = 0");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
    }
}
