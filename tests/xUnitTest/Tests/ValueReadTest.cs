// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.5.3, 7.3, 10.2.1: a read Type (a Scalar, position or range Type) is read through references where it is
/// expected, including as an owning receiver, and a Type argument constrained to a read Contract is inferred as the
/// referent.</summary>
public class ValueReadTest
{
    private const string Reads =
        "func g<R>(r: R, n: isize) -> isize\n    R is PositionRange\n    match r.tryResolve(n)\n        .Some(let s) => return s.end\n        .None => return -1\n" +
        "func double<T>(x: T) -> T\n    T is PrimitiveInteger\n    return x + x\n" +
        "func f<P>(p: P, n: isize) -> isize\n    P is Position\n    match p.tryResolve(n)\n        .Some(let q) => return q\n        .None => return -1\n" +
        "let values: [4 of i32] = [1, 2, 3, 4]\nlet whole = values.indices\nlet w = whole@ref\nlet copy: ResolvedRange = w\n" +
        "let n: i32 = 3\nlet r = n@ref\n" +
        "require copy.end == 4 and g(w, 4) == 4 and g(whole, 3) == -1 and double(r) == 6 and f(r, 5) == 3 else => $abort(\"read\")\n" +
        "match w.tryResolve(5)\n    .Some(_) => ()\n    .None => $abort(\"receiver\")\n" +
        "Console.writeLine(\"\\(whole)\")\nConsole.writeLine(\"ok\")";

    // SPEC 3.5.3: a position is read from an exclusive reference, from two layers of references, into a typed Binding, as
    // a returned value and as a call result used as a key.
    private const string Forms =
        "func back(p: ref/FromEnd<i32>) -> FromEnd<i32> => p\nfunc same(p: FromEnd<i32>) -> FromEnd<i32> => p\n" +
        "let values: [4 of i32] = [1, 2, 3, 4]\nvar last = ^1\nlet viaUniq = values[last@uniq]\nlet shared = last@ref\nlet twice = shared@ref\nlet typed: FromEnd<i32> = twice\n" +
        "let range = 1..^1\nlet ranged = range@ref\n" +
        "require viaUniq == 4 and values[shared] == 4 and values[twice] == 4 and typed == ^1 and back(shared) == ^1 and values[same(^2)] == 3 and values[ranged].length == 2 else => $abort(\"reads\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void ReadTypesAreReadThroughReferences()
        => ScalarEmissionTest.EmitFixture("ValueReadTypes", Reads, "0..4\nok\n");

    [Fact]
    public void PositionsAreReadInEveryForm()
        => ScalarEmissionTest.EmitFixture("ValueReadForms", Forms, "ok\n");

    [Theory]
    [InlineData("let a = ^1\nlet b = ^2\nlet r = if true => a@ref\nelse => b")]
    [InlineData("let a = 0..3\nlet b = 1..4\nlet r = if true => a@ref\nelse => b")]
    [InlineData("let a = ^1\nlet r = if true => a@ref\nelse => ^2")]
    [InlineData("let a = 0..3\nlet r = if true => a@ref\nelse => 1..4")]
    [InlineData("let a = 0..3\nlet r = if true => 1..4\nelse => a@ref")]
    [InlineData("let a = ^1\nlet b = a@ref\nlet r = if true => b@ref\nelse => a")]
    [InlineData("func select<P>(a: ref/P, b: P) -> P\n    P is Position\n    let r = if true => a\n    else => b\n    return r\n()")]
    [InlineData("func select<R>(a: ref/R, b: R) -> R\n    R is PositionRange\n    let r = if true => a\n    else => b\n    return r\n()")]
    [InlineData("let a = 0..3\nlet choice: Option<Range<i32, i32>> = .Some(1..4)\nlet r = match choice\n    .Some(let x) => x\n    .None => a")]
    public void BranchesUnifyReadTypesThroughReferences(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var result = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "r");
        Assert.Equal(SemanticsKind.Owner, result.NameKoto.TypeOf()?.Semantics);
    }

    [Fact]
    public void BranchValueReadsCopySnapshotsAndReleaseTheirBorrows()
    {
        const string Source = "func choose(flag: bool) -> i32\n" +
            "    var p = ^1\n    var r = 0..3\n" +
            "    let picked = if flag => p@ref\n    else => ^2\n" +
            "    let window = if flag => 1..2\n    else => r@ref\n" +
            "    p = ^3\n    r = 2..4\n" +
            "    let data: [4 of i32] = [10, 20, 30, 40]\n" +
            "    return data[picked] + data[window].length@i32\n" +
            "require choose(true) == 41 and choose(false) == 33 else => $abort(\"branch snapshots\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ValueReadBranches", Source, "ok\n");
    }

    [Fact]
    public void AnUnannotatedLocalKeepsTheReference()
    {
        var c = MinimalEmissionTest.Analyze("let values: [4 of i32] = [1, 2, 3, 4]\nlet whole = values.indices\nlet w = whole@ref\nlet kept = w");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var kept = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "kept");
        Assert.Equal(SemanticsKind.Ref, kept.NameKoto.TypeOf()?.Semantics);
    }

    [Fact]
    public void MatchingReferenceLayersKeepTheirDependency()
    {
        var c = MinimalEmissionTest.Analyze("var a = ^1\nlet b = ^2\nlet r = if true => a@ref\nelse => b@ref\na = ^3\nlet p: FromEnd<i32> = r");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var result = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "r");
        Assert.Equal(SemanticsKind.Ref, result.NameKoto.TypeOf()?.Semantics);
        Assert.Contains(c.Ownership.Issues, x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
    }

    // A Copy struct is not a read Type: a reference to it is not read at a by-value parameter.
    [Fact]
    public void OtherCopyTypesAreNotReadImplicitly()
    {
        var c = MinimalEmissionTest.Analyze("struct Point\n    Self is Copy\n    public let x: i32 = 0\n    public init() => ()\nfunc take(p: Point) -> () => ()\nlet p = Point.init()\nlet r = p@ref\ntake(r)");
        Assert.False(c.Binding.Result.IsComplete);
    }
}
