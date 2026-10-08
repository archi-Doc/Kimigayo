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
