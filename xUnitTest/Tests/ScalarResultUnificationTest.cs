// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 14.9.1, 10.2: result sources that differ only in safe reference layers over one Scalar Type have that
/// Scalar as their common Type; sources with the same layers keep the borrow result with every dependency.</summary>
public class ScalarResultUnificationTest
{
    private const string UnificationSource =
        "let x: Option<i32> = .Some(3)\nlet count = match x\n    .Some(let n) => n\n    .None => 0\n" +
        "var number = 5\nlet r = number@ref\nlet flag = count == 3\nlet chosen = if flag => r else => 7\nnumber = 6\n" +
        "require count == 3 and chosen == 5 else => $abort(\"unify\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void ReferenceAndValueSourcesUnifyToTheScalar()
        => ScalarEmissionTest.EmitFixture("ScalarResultUnification", UnificationSource, "ok\n");

    [Theory]
    [InlineData("let x: Option<i32> = .Some(3)\nlet count = match x\n    .Some(let n) => n\n    .None => 0", "count", "i32")]
    [InlineData("func f(c: bool, a: ref/i32, b: ref/i32) -> i32\n    let r = if c => a else => b\n    return r", "r", "ref")]
    [InlineData("func f(c: bool, a: ref/i32, b: uniq/i32) -> i32\n    let r = if c => a else => b\n    return r", "r", "i32")]
    public void OnlyDifferingLayersUnifyToTheScalar(string source, string name, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == name);
        var type = local.BoundSymbol!.Type!;
        Assert.Equal(expected, type.Kind == BoundTypeKind.Semantics && type.Semantics == SemanticsKind.Ref ? "ref" : type.Name);
    }

    [Fact]
    public void DifferentScalarsStillNeedAnAnnotation()
    {
        var c = MinimalEmissionTest.Analyze("func f(c: bool, a: ref/i32, b: i64) -> ()\n    let r = if c => a else => b");
        Assert.False(c.Binding.Result.IsComplete);
    }
}
