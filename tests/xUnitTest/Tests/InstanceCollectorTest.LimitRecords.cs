// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// IMPL 21.3.2, DIAGNOSTICS rule 6 (DECISIONS R5-M171, PLAN G153): a Type over a layout limit is one record per
// declaration that uses it, at the least of that declaration's uses, whatever the collection's order.
public sealed partial class InstanceCollectorTest
{
    private const string Separate =
        "func g1<T>() -> i32\n    let p: Option<T> = .None\n    return 1\nfunc g2<T>() -> i32\n    let q: Option<T> = .None\n    return 2\n";

    private const string Within = "func g<T>() -> i32\n    let a: Option<T> = .None\n    let b: Option<[2147483648 of u8]> = .None\n    return 1\n";

    // DECISIONS R5-M171: one record per declaration (two functions, a function and an anonymous function in it, a constructor and its
    // creation), at the declaration's least use whichever instance is admitted first (a call routed through a generic helper changes that
    // order), each subject a parsed node, also where Binding synthesizes a call (an index, an iteration, an object creation).
    [Theory]
    [InlineData("func a(x: [2147483648 of u8]) -> i32 => 1\nfunc b(y: [2147483648 of u8]) -> i32 => 2\n", "[2147483648 of u8]|[2147483648 of u8]")]
    [InlineData(
        Separate + "func r1() -> i32 => g2<[2147483648 of u8]>()\nfunc r2() -> i32 => g1<[2147483648 of u8]>()\n",
        "let p: Option<T> = .None|let q: Option<T> = .None")]
    [InlineData(
        Separate + "func w<U>() -> i32 => g2<[2147483648 of u8]>()\nfunc r1() -> i32 => w<i32>()\nfunc r2() -> i32 => g1<[2147483648 of u8]>()\n",
        "let p: Option<T> = .None|let q: Option<T> = .None")]
    [InlineData(Within + "func r1() -> i32 => g<i32>()\nfunc r2() -> i32 => g<[2147483648 of u8]>()\n", "let a: Option<T> = .None")]
    [InlineData(
        Within + "func w<U>() -> i32 => g<i32>()\nfunc r1() -> i32 => w<i32>()\nfunc r2() -> i32 => g<[2147483648 of u8]>()\n", "let a: Option<T> = .None")]
    [InlineData(
        "func host(big: [2147483648 of u8]) -> i32\n    let f = func (x: [2147483648 of u8]) -> i32 => 1\n    return 1\n",
        "[2147483648 of u8]|[2147483648 of u8]")]
    [InlineData("func f(values: ref/Array<(i32, [2147483648 of u8])>) -> i32 => values[0].0\n", "values[0].0")]
    [InlineData(
        "struct Holder<T>\n    public var v: Option<T>\n    public init() => self.v = .None\n" +
        "func f() -> i32\n    let o = Holder<[2147483648 of u8]>.init()@obj\n    return 1\n",
        "self.v|Holder<[2147483648 of u8]>.init()@obj")]
    [InlineData(BigStruct + "func f(values: ref/Array<Big>) -> i32\n    for v in values\n        _ = v.n\n    return 1\n", "for v in values")]
    public void ALayoutLimitIsARecordPerDeclarationAtItsLeastUse(string source, string subjects)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(subjects, string.Join("|", set.Records.Select(x => c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
        Assert.All(set.Records, x => Assert.Equal(DiagnosticCode.GenerationResourceLimit_Kd, x.Code));
        Assert.All(set.Records, x => Assert.True(c.SyntaxOf(x.Subject) is var node && node.SyntaxId <= node.SyntaxOwner!.ParsedCount, "Synthesized."));
    }

    // DECISIONS R5-M171: a type-driven key whose Type passes the height cap is a limit of its erased Type, not a refused key: one record per
    // declaration at its least request (two functions, two records; two locals of one function, one). Known limit: a shared type-driven
    // item keeps its first admitter's record (Option<S>'s glue, reaching an Array of a Type over the size limit, requested from d and e:
    // one record, at d; at e once d is gone). Subjects up to their first colon.
    [Theory]
    [InlineData("twoFunctions", "let v|let v")]
    [InlineData("oneFunction", "let v")]
    [InlineData("shared", "let o")]
    [InlineData("sharedAlone", "let p")]
    public void AHeightCapIsATypeLimitAndASharedItemKeepsItsFirstRecord(string shape, string subjects)
    {
        var deep = "Option<obj/" + string.Concat(Enumerable.Repeat("W<", 130)) + "i32" + new string('>', 130) + "> = .None\n";
        var nest = "struct W<T>\n    public var next: Option<obj/W<T>>\n";
        var shared = "struct S\n    public var a: Array<[2147483648 of u8]>\n";
        var e = "func e() -> i32\n    let p: Option<S> = .None\n    return 2\n";
        var c = MirBuildTest.Compile(shape switch
        {
            "twoFunctions" => nest + $"func f() -> ()\n    let v: {deep}    return\nfunc g() -> ()\n    let v: {deep}    return\n",
            "oneFunction" => nest + $"func f() -> ()\n    let v: {deep}    let u: {deep}    return\n",
            "shared" => shared + "func d() -> i32\n    let o: Option<S> = .None\n    return 1\n" + e,
            _ => shared + e,
        });
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(subjects, string.Join("|", set.Records.Select(x => c.SyntaxOf(x.Subject).ToString().Split(':')[0])));
        Assert.All(set.Records, x => Assert.Equal(DiagnosticCode.GenerationResourceLimit_Kd, x.Code));
    }
}
