// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC F.3, 3.3, 5: the raw pointer Semantics is spelled raw; unsafe marks only the places where a promise is made. A raw layer may
// state an Origin (SPEC 5.7).
public class RawPointerSyntaxTest
{
    [Fact]
    public void UnsafeIsNoSemanticsName()
    {
        Assert.True(CompilerHelper.TryParse("raw", out var raw));
        Assert.Equal(SemanticsKind.Raw, raw);
        Assert.False(CompilerHelper.TryParse("unsafe", out _));
        Assert.Equal("raw", SemanticsKind.Raw.ToText());
    }

    // raw is reserved only in Semantics positions and after '@'; elsewhere it is an ordinary Name.
    [Fact]
    public void RawIsAnOrdinaryNameElsewhere()
    {
        var c = MinimalEmissionTest.Analyze("struct Sample\n    public let raw: i32\n    public init(raw: i32) => self.raw = raw\n" +
            "func read(pointer: raw/i32) -> raw/i32 => pointer\npublic func main()\n    let raw = Sample.init(raw: 1)\n    let total = raw.raw + 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheOldSpellingIsNoSemantics()
    {
        var c = MinimalEmissionTest.Analyze("func read(pointer: unsafe/i32) -> () => ()\npublic func main() => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 5.7, 3.3.2, 13.5.1 (DECISIONS BU-24): a raw layer states an Origin wherever a Type is written, also inside a Type argument or
    // a pointee, and a grouped adaptation target states its outer raw layer's (the claim, valid in an unsafe context).
    [Theory]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) => ()")]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) -> raw/i32 during b => p")]
    [InlineData("struct View {source}\n    let value: ref/i32 during source\n    let at: raw/i32 during source")]
    [InlineData("struct Buffer\n    let data: raw/i32 during self")]
    [InlineData("enum Cell\n    Some(raw/i32 during self)\n    None")]
    [InlineData("func f(b: ref/i32, p: Option<raw/i32 during b>) => ()")]
    [InlineData("func f(b: ref/i32, p: raw/(raw/i32 during b)) => ()")]
    [InlineData("func f(p: raw/i32) -> raw/i32 during static\n    unsafe => return p@(raw/i32 during static)")]
    [InlineData("func f<T>(x: T)\n    T is raw/i32 during static\n    ()")]
    public void AWrittenRawOriginIsStated(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7 (D17, D18): `during` on a raw layer writes one atom of a borrow: never a meet, a name no borrow, slot or static carries,
    // or `self` outside a raw layer of inline storage; a signature's name is never introduced by a raw layer (bu19).
    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during (a and b)) => ()", "InvalidOriginBinding_Kd: raw/i32 during (a and b)")]
    [InlineData("func f(x: i32, p: raw/i32 during x) => ()", "InvalidOriginBinding_Kd: x")]
    [InlineData("func f<T>(p: raw/T during d) => ()", "MissingOriginBinding_Kd: d")]
    [InlineData("struct S\n    let p: Option<raw/i32 during self>", "InvalidOriginBinding_Kd: raw/i32 during self")]
    [InlineData("struct S\n    let p: raw/(raw/i32 during self)", "InvalidOriginBinding_Kd: raw/i32 during self")]
    public void ARawOriginIsOneAtomOfABorrow(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    // SPEC 5.7, Fits and claims: a raw layer without `during` states an Origin every Origin outlives, so dropping or narrowing an Origin
    // is a fit and stating one is a claim, valid only in an unsafe context; elsewhere it is an Origin relation error (rulings S4), at the
    // value whose unstated Origin does not outlive the stated one. An invariant position relates both ways.
    [Theory]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static\n    return pointer", "UnsatisfiedOriginRelation_Kd: pointer")]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static => pointer@(raw/i32 during static)", "UnsatisfiedOriginRelation_Kd: pointer")]
    [InlineData("func f(r: ref/i32, p: raw/i32)\n    let q: raw/i32 during r = p", "UnsatisfiedOriginRelation_Kd: p")]
    [InlineData("func g(b: ref/i32, p: raw/i32 during b) => ()\nfunc f(b: ref/i32, q: raw/i32) => g(b, q)", "UnsatisfiedOriginRelation_Kd: q")]
    [InlineData("func f(r: ref/i32, a: uniq/Array<raw/i32 during r>)\n    let b: uniq/Array<raw/i32> = a", "UnsatisfiedOriginRelation_Kd: a")]
    [InlineData("func g(a: uniq/Array<raw/i32>) => ()\nfunc f(r: ref/i32, a: uniq/Array<raw/i32 during r>) => g(a)", "UnsatisfiedOriginRelation_Kd: a")]
    [InlineData("func f(r: ref/i32, a: raw/i32 during r)\n    var c = a\n    let u: uniq/(raw/i32) = c@uniq", "UnsatisfiedOriginRelation_Kd: c@uniq")]
    public void StatingARawOriginIsAClaim(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    [Theory]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static\n    unsafe => return pointer@(raw/i32 during static)")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 => p")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 during r => p@(raw/i32 during r)")]
    [InlineData("func f(r: ref/i32) -> raw/i32 during r => null@(raw/i32 during r)")]
    [InlineData("func g(p: raw/i32) => ()\nfunc f(r: ref/i32, p: raw/i32 during r) => g(p)")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/u8 during r => p@raw/u8")]
    public void DroppingOrKeepingARawOriginIsAFit(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7 (Q-O6): a var local keeps its initializer's raw Origin; raw Origins that differ have the unstated common Type, never a meet,
    // also where one branch is a call whose result states its argument's Origin.
    [Fact]
    public void AVarLocalKeepsItsRawOrigin()
        => Assert.Equal(
            ["UnprovenOriginRelation_Kd: q"],
            Records("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    var x = p\n    x = q"));

    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    var x = p\n    x = p")]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    let both = [p, q]\n    let x: raw/i32 = both[0]")]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b) -> bool => p == q")]
    [InlineData("func get(b: ref/i32, q: raw/i32 during b) -> raw/i32 during b => q\nfunc f(c: bool, a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b) -> raw/i32\n    let x = if c => get(b, q) else => p\n    return x")]
    public void DifferingRawOriginsJoinUnstated(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7 (rulings S4, design-final §13 A2): a relation with an unstated end forms no obligation, and no obligation names Shared,
    // Holder or Unstated.
    [Fact]
    public void NoObligationNamesARawOnlyOrigin()
    {
        const string Source = "struct Buffer\n    internal let data: raw/i32 during self\n    public init() => unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)\n" +
            "func shared(b: ref/Buffer) -> raw/i32 => b.data\nfunc pass(r: ref/i32, p: raw/i32 during r) -> raw/i32 => p\n" +
            "func local(n: i32)\n    let r = n@ref\n    let p: raw/i32 during r = null\n    let q: raw/i32 = p\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Binding.Obligations, static x => RawOnly(x.Longer) || RawOnly(x.Shorter));

        static bool RawOnly(BoundOrigin? origin) => origin?.Kind is OriginKind.Shared or OriginKind.Holder or OriginKind.Unstated;
    }

    // SPEC 10.7, 15.3.7 (review 2 B-B1): a conversion's contract compares raw Origins too; a required unstated input the implementation
    // states an Origin for is the member that fails, with the unstated end first.
    [Fact]
    public void AConversionComparesRawOrigins()
        => Assert.Equal(
            ["UnprovenOriginContract_Kd: f"],
            Records("func f(r: ref/i32, p: raw/i32 during r) => ()\nfunc g()\n    let h: (ref/i32, raw/i32) -> () = f"));

    // SPEC 15.3.6, 5.7 (review 2 §6 item 7): a closure's inferred result names no body-local region: a raw Origin over a var local is
    // resolved to that local's sources, as a borrow's is.
    [Fact]
    public void AClosureResultNamesNoLocalRawRegion()
    {
        const string Source = "func f(x: ref/i32) -> raw/i32\n    let g = func [x] ()\n        var v = x\n        let p: raw/i32 during v = null\n        return p\n" +
            "    return g()\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var result = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsAnonymous).SymbolOf()!.Type!;
        Assert.NotNull(result.RawOrigin);
        Assert.False(Binding.IsLocalRegion(Binding.RegionOf(result.RawOrigin)!));
    }

    // SPEC 5.4, 5.7: a provable claim needs no unsafe context, so an unsafe block holding only it is unnecessary; an unprovable one needs it.
    [Theory]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 during r\n    unsafe => return p@(raw/i32 during r)", "UnnecessaryUnsafeBlock_Kd: unsafe")]
    [InlineData("func f(r: ref/i32, p: raw/i32) -> raw/i32 during r\n    unsafe => return p@(raw/i32 during r)", null)]
    public void AClaimNeedsAnUnsafeContextOnlyWhenItIsNoFit(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], Records(declarations));

    // The check's records of declarations completed by an empty main, each as `code: primary span text` (SPEC 23.1).
    internal static string[] Records(string declarations)
    {
        var source = declarations + "\npublic func main() => ()\n";
        return [.. DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Span is { } span && x.Source == 0 ? $"{x.Code}: {source.Substring(span.Start, span.Length)}" : x.Code)];
    }
}
