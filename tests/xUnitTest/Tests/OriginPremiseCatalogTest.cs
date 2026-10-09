// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G74: every premise rule of the catalog (OriginPremiseRule) is extracted from real source: a program the rule's premise
// proves, and the same relation where no visible contract supplies that premise.
public class OriginPremiseCatalogTest
{
    private const string Main = "public func main() => ()\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n    public init(item: ref/i32 during a) => self.item = item\n";

    private const string Nested = "contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b\nstruct S\n    Self is C\n";

    private const string Box = "public struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\n";

    private const string PairHolder = "struct Holder<s/T>\n    s is ref or uniq\n    public let value: s/T\n    public init(value: s/T) => self.value = value@move\n";

    private const string Peek = "func peek<s/T>(b: ref/Box<s/T>) -> ref/T\n    s is ref or uniq\n    return b.item@follow@ref\n";

    // A.1c: `o` relates to `c` only through the borrowed input `y`.
    private const string Pick = "func f<s/T>(x: s/T, y: ref/Box<s/T> during c) -> ref/T during c\n    s is ref\n    return x@follow@ref\n";

    // R3: the result's well-formedness relates `o` to `p`.
    private const string Leak = "func leak<s/T>(p: ref/i32, x: s/T) -> (Option<ref/Box<s/T> during p>, ref/T during p)\n    s is ref\n    return (.None, x@follow@ref)\n";

    // Seven pair binders, the last one's slot stored below a borrow; six admit one Semantics each.
    private const string Seven = "func f<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(x: ref/Box<h/H during q> during r, y: ref/i32 during q) -> ref/i32 during r\n" +
        "    a is ref\n    b is ref\n    c is ref\n    d is ref\n    e is ref\n    g is ref\n";

    public static TheoryData<string, string, string> Rows => new()
    {
        // R1, SPEC 15.3.3: an enclosing function's clause.
        {
            nameof(OriginPremiseRule.Declaration),
            "func g(a: ref/i32, x: ref/i32) -> i32\n    origin a outlives x\n    let k: ref/i32 during x = a\n    return k@follow\n" + Main,
            "func g(a: ref/i32, x: ref/i32) -> i32\n    let k: ref/i32 during x = a\n    return k@follow\n" + Main
        },

        // R2, SPEC 15.3.4, 15.6.1: the Origins stored in a borrowed input outlive its borrow.
        {
            nameof(OriginPremiseRule.Input),
            Holder + "func g(p: ref/Holder) -> ref/i32 during p => p.item\n" + Main,
            Holder + "func g(p: Holder, q: ref/i32) -> ref/i32 during q => p.item\n" + Main
        },

        // R3, SPEC 15.3.7: the result Type's well-formedness is a premise of its definition.
        {
            nameof(OriginPremiseRule.Result),
            "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\n" + Main,
            "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/i32 during p>, ref/i32 during p)\n    return (.None, q)\n" + Main
        },

        // R4, SPEC 8.4.3.1, 15.3.7: the formation condition of an associated application in a signature is a premise there and an
        // obligation of each call.
        {
            nameof(OriginPremiseRule.Associated),
            Nested + "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    $abort(\"unused\")\n" + Main,
            Nested + "func f(x: ref/i32 during a, y: ref/i32 during b, keep: uniq/(ref/i32 during b)) -> S.Item(a, b)\n    $abort(\"unused\")\n" +
                "func g(x: ref/i32 during c, y: ref/i32 during d, keep: uniq/(ref/i32 during d)) -> i32\n    let r = f(x, y, keep)\n    return 0\n" + Main
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EachPremiseRuleProvesItsProgram(string rule, string proven, string unproven)
    {
        var (codes, metrics) = Check(proven);
        Assert.Empty(codes);
        Assert.NotEqual(0, metrics.EdgesOf(Enum.Parse<OriginPremiseRule>(rule)));
        Assert.Contains(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), Check(unproven).Codes);
    }

    // R5, SPEC 15.6, 15.6.1: a borrow of a complete local Place is well-formed, so the Origins stored in the Place outlive the
    // borrow's inferred region. That region is never fixed, so region inference also accepts the relation without the premise.
    [Fact]
    public void ABorrowedLocalPlaceSuppliesItsStoredOrigins()
    {
        var source = "enum E<T>\n    Some(T)\n    None\nlet text = \"hello\"\nlet savedText = text@ref\nlet inner = (savedText, 42)\nlet input = inner@ref\n" +
            "let value: E<ref/(ref/string during savedText, i32) during input> = .Some(input)\nmatch value@ref\n    .Some((let saved, _)) => Console.writeLine(saved@follow)\n    .None => $abort(\"none\")\n";
        var (codes, metrics) = Check(source);
        Assert.Empty(codes);
        Assert.NotEqual(0, metrics.EdgesOf(OriginPremiseRule.LocalPlace));
    }

    // SPEC 15.6.5, 8.1.2: a pair layer's slot is stored only in its binder's borrow cases, so it is a premise only of a relation required
    // in those cases: a fit or Type occurrence at the same slot, also nested under two binders, and never an ordinary relation of a body
    // whose binder admits a value case (A.2: `a outlives c` holds only when `s` is ref).
    [Theory]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n    s is owner\n    return x\n", false)]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n    s is owner or ref\n    return x\n", false)]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n    s is ref\n    return x\n", true)]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n    s is owner\n    return x\nfunc g(b: ref/Box<i32>) -> i32\n    var m: i32 = 5\n    let h = f<i32>\n    return h(b, m@ref)@follow\n", false)]
    [InlineData("func f<s/T, t/U>(x: s/(t/U during b) during a, m: s/T) -> i32\n    s is owner or ref\n    t is owner or ref\n    let y: s/(t/U during b) during a = x@move\n    return 1\n", true)]
    [InlineData("func f<s/T, t/U>(x: t/U during b, m: s/T during a) -> i32\n    s is owner or ref\n    t is owner or ref\n    let y: Option<s/(t/U during b) during a> = .None\n    return 1\n", false)]
    [InlineData("func shorten<s/T>(x: ref/(s/T during a) during b, y: s/T during a) -> s/T during b\n    s is owner or ref\n    T is Copy\n    return y\n", true)]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/(ref/i32 during q) during a> during c, w: s/T) -> ref/i32 during c\n    s is owner or ref\n    return b.item@follow\n", true)]
    [InlineData(Box + "func f<s/T>(b: ref/Box<s/(ref/i32 during q) during a> during c, w: s/T, z: ref/i32 during d) -> ref/i32 during d\n    s is owner or ref\n    return b.item@follow\n", false)]
    [InlineData(Box + Seven + "    h is ref\n    let z: ref/Box<h/H during q> during r = x\n    return y\n", true)]
    [InlineData(Box + Seven + "    h is owner or ref\n    let z: ref/Box<h/H during q> during r = x\n    $abort(\"unused\")\n", true)]
    [InlineData(Box + Seven + "    h is owner or ref\n    return y\n", false)]
    public void APairSlotIsAPremiseOnlyInItsBorrowCases(string body, bool valid)
    {
        var codes = Check(body + Main).Codes;
        Assert.Equal(valid ? [] : [nameof(DiagnosticCode.UnprovenOriginRelation_Kd)], codes);
    }

    // SPEC 8.1.1, 15.3.7, 15.6.5: the implicit outer Origin `o` of an original pair layer below a borrow layer is stored there in its
    // binder's borrow cases, so its relation to that layer is a premise of the definition (F14, A.9) and an obligation of each use, both
    // through a concrete argument (A.1c) and through a result premise; a pair without an enclosing borrow relates `o` to nothing, and a
    // binder admitting no borrow stores no `o`. A Field below a borrow proves the relation as its concrete twin does.
    [Theory]
    [InlineData(PairHolder + "    public func get(self: ref/Self) -> ref/T\n        return self.value@follow@ref\n", null)]
    [InlineData(PairHolder + "    public func get(self: ref/Self, q: ref/i32) -> ref/T during q\n        return self.value@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("struct Holder<s/T>\n    s is ref\n    public let value: s/T\n    public init(value: s/T) => self.value = value@move\n    public func get(self: ref/Self) -> ref/T\n        return self.value@follow@ref\n", null)]
    [InlineData(Box + Peek, null)]
    [InlineData(Box + "func peek<s/T>(b: ref/Box<s/T>, q: ref/i32) -> ref/T during q\n    s is ref or uniq\n    return b.item@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func peek<s/T>(b: ref/(s/T, i32)) -> ref/T\n    s is ref or uniq\n    return b.0@follow@ref\n", null)]
    [InlineData("func g<s/T>(x: s/T, q: ref/i32) -> ref/T during q\n    s is ref\n    return x@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func g<s/T>(x: s/T, q: ref/i32) -> ref/T during q\n    s is ref or uniq\n    return x@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(Box + Peek + "func outer<t/U>(b: ref/Box<t/U>) -> ref/U\n    t is ref or uniq\n    return peek(b)\n", null)]
    [InlineData(Box + Peek + "func outer<t/U>(b: ref/Box<t/U>, q: ref/i32) -> ref/U during q\n    t is ref or uniq\n    return peek(b)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(Box + Pick, null)]
    [InlineData(Box + Pick + "func g(q: ref/Box<ref/i32 during s> during t) -> ref/i32 during t\n    let m: i32 = 5\n    return f(m@ref, q)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd))]
    [InlineData(Box + Leak, null)]
    [InlineData(Box + Leak + "func g(q: ref/i32) -> i32\n    let m: i32 = 5\n    let pair = leak(q, m@ref)\n    return 0\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd))]
    [InlineData(Box + "struct View<s/T> {a}\n    s is owner\n    public let b: ref/Box<s/T> during a\n", null)]
    [InlineData(Box + "struct View<s/T> {a}\n    s is ref\n    public let b: ref/Box<s/T> during a\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(Box + "struct View<s/T> {a}\n    s is owner or ref\n    public let b: ref/Box<s/T> during a\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(Box + "struct View<s/T> {a, q}\n    s is ref\n    origin q outlives a\n    public let b: ref/Box<s/T during q> during a\n", null)]
    public void TheImplicitOuterOriginIsAPremiseBelowABorrowLayer(string body, string? code)
    {
        var codes = Check(body + Main).Codes;
        Assert.Equal(code is null ? [] : [code], codes);
    }

    // SPEC 15.3.3, 15.6.1: a local's clause is an obligation, never a premise: it does not prove itself, and the enclosing clause
    // that would prove it does.
    [Fact]
    public void ALocalClauseDoesNotProveItself()
    {
        const string Box = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";
        var local = Box + "func f(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == b\n    return h.item\n" + Main;
        Assert.Contains(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), Check(local).Codes);
        Assert.Empty(Check(local.Replace("    let h0", "    origin a outlives b\n    let h0", StringComparison.Ordinal)).Codes);
    }

    private static (string[] Codes, OriginProofMetrics Metrics) Check(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("catalog.kimi", source));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        return (c.Binding.Issues.Select(static x => x.Code.ToString()).Concat(c.Ownership.Issues.Select(static x => x.Code.ToString())).ToArray(), metrics);
    }
}
