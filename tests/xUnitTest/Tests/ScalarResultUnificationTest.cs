// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 14.9.2, 10.2: result sources that differ only in safe reference layers over one Scalar Type have that
/// Scalar as their common Type; sources with the same layers keep the borrow result with every dependency.</summary>
public class ScalarResultUnificationTest
{
    private const string UnificationSource =
        "let x: Option<i32> = .Some(3)\nlet count = match x\n    .Some(let n) => n\n    .None => 0\n" +
        "var number = 5\nlet r = number@ref\nlet flag = count == 3\nlet chosen = if flag => r else => 7\nnumber = 6\n" +
        "require count == 3 and chosen == 5 else => $abort(\"unify\")\nConsole.writeLine(\"ok\")";

    // SPEC 14.9.2 step 2 (PLAN G758, G443's part): sources that differ in Origins alone, a pair layer's slot (j0, j2v, j3-PRU, k06) or a
    // typed Option or Tuple variable (j1, j3) included, join to their common Type (dev: TypeMismatch_Kd); an escape keeps its relation
    // (j1e, j3e, k07 with its concrete twin) and an invariant position stays G443's (j5). Each row's records by code.
    [Theory]
    [InlineData("func f(p: Option<ref/i32 during a>, q: Option<ref/i32 during b>, c: bool) -> i32\n    let z = if c => p else => q\n    _ = z\n    return 0\n", "clean")] // j1-option-join
    [InlineData("func f(p: Option<ref/i32 during a>, q: Option<ref/i32 during b>, c: bool) -> Option<ref/i32 during a>\n    return if c => p else => q\n", "1*UnprovenOriginRelation_Kd")] // j1e-option-join-escape
    [InlineData("func f(t: (ref/i32 during a, i32), u: (ref/i32 during b, i32), c: bool) -> i32\n    let z = if c => t else => u\n    _ = z\n    return 0\n", "clean")] // j3-tuple-var-join
    [InlineData("func f(t: (ref/i32 during a, i32), u: (ref/i32 during b, i32), c: bool) -> (ref/i32 during a, i32)\n    return if c => t else => u\n", "1*UnprovenOriginRelation_Kd")] // j3e-tuple-var-join-escape
    [InlineData("func f(p: uniq/(ref/i32 during a) during x, q: uniq/(ref/i32 during b) during y, c: bool) -> i32\n    let z = if c => p else => q\n    _ = z\n    return 0\n", "1*TypeMismatch_Kd")] // j5-uniq-inner-invariant
    [InlineData("func f<s/T>(value: s/T, other: s/T during a, c: bool) -> i32\n    s is owner or uniq\n    T is Copy\n    let z = if c => value else => other\n    _ = z\n    return 0\n", "clean")] // k06-pou-join-local
    [InlineData("func f<s/T>(value: s/T, other: s/T during a, c: bool) -> s/T during a\n    s is uniq\n    return if c => value else => other\n", "1*UnprovenOriginRelation_Kd")] // k07-pu-join-escape
    [InlineData("func f(value: uniq/i32 during b, other: uniq/i32 during a, c: bool) -> uniq/i32 during a\n    return if c => value else => other\n", "1*UnprovenOriginRelation_Kd")] // k07c-concrete-join-escape
    [InlineData("func f<s/T>(value: s/T, other: s/T during a, b: bool) -> ()\n    s is ref or uniq\n    let r = if b => other else => value\n    _ = r\n", "clean")] // j0-PRU
    [InlineData("func f<s/T>(x: s/T during a, y: s/T during c, b: bool) -> ()\n    s is ref or uniq\n    let r = if b => x else => y\n    _ = r\n", "clean")] // j3-PRU
    [InlineData("func f<s/T>(value: s/T, b: bool) -> ()\n    s is ref or uniq\n    var local = value\n    let r = if b => local else => value\n    _ = r\n", "clean")] // j2v-PRU
    [InlineData("func f<s/T>(value: s/T, b: bool) -> ()\n    s is ref\n    var local = value\n    let r = if b => local else => value\n    _ = r\n", "clean")] // j2v-PR
    public void SourcesThatDifferInOriginsAloneJoin(string source, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source + "public func main() => ()\n", "Application"));

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == name);
        var type = local.SymbolOf()!.Type!;
        Assert.Equal(expected, type.Kind == BoundTypeKind.Semantics && type.Semantics == SemanticsKind.Ref ? "ref" : type.Name);
    }

    [Fact]
    public void DifferentScalarsStillNeedAnAnnotation()
    {
        var c = MinimalEmissionTest.Analyze("func f(c: bool, a: ref/i32, b: i64) -> ()\n    let r = if c => a else => b");
        Assert.False(c.Binding.Result.IsComplete);
    }
}
