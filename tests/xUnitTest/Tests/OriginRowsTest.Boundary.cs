// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The boundary (2a0-D §5.1 with DECISIONS R4-M108): a relation Binding records at a body chain (2a0-D D1: an end, resolved at its use, is
// not fixed) keeps its record, `Binding.OriginRelation` with its code and location, and its node has rows in its body for A5 (no MIR
// reader yet; 2a-1a reads them): every position of a non-call fit, the fixed-fixed ones of a mixed fit included, and a local's clause.
// A node whose records are all fixed-fixed has no row (Binding's until R7), nor, until 2c-6, a Closure result over call-local storage.
public sealed partial class OriginRowsTest
{
    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd);

    private const string Unproven = nameof(DiagnosticCode.UnprovenOriginRelation_Kd);

    private const string Holder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";

    private const string Bad = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n";

    private const string LocalClause = Holder + "func f(a: ref/i32) -> i32\n    let local = 3\n    let h: H{x} = H.init(local@ref)\n        origin x.s outlives a\n" +
        "    return h.item@follow\n";

    // SPEC 15.6.5's `bad`: the return's fit is Refuted at the Borrow, and its position is a row of bad, finite to fixed.
    [Fact]
    public void ARecordedFitsPositionIsARowOfItsBody()
        => Assert.Equal(["Fn:bad Fit Finite Fixed"], Recorded(Bad, [$"{Unsatisfied} local@ref"], "return local@ref"));

    // SPEC 15.3.3, 15.6.5: a local's clause over a slot its initializer supplies with a Borrow of a local is Refuted at the initializer, and
    // the clause is a row of the function, finite to fixed.
    [Fact]
    public void ARecordedClauseIsARowOfItsBody()
        => Assert.Equal(["Fn:f Declared Finite Fixed"], Recorded(LocalClause, [$"{Unsatisfied} H.init(local@ref)"], "origin x.s outlives a"));

    // A fit with a body chain and a fixed-fixed chain (BU's `mixed`): both are recorded, and every position of the fit is a row, the
    // fixed-fixed one included, so the twins are the node's records (DECISIONS R4-M108).
    [Fact]
    public void AMixedFitsPositionsAreAllRows()
        => Assert.Equal(
            ["Fn:f Fit Finite Fixed", "Fn:f Fit Fixed Fixed"],
            Recorded("func f(x: ref/i32, y: ref/i32) -> (ref/i32 during x, ref/i32 during x)\n    let local = 1\n    let t = (local@ref, y)\n    return t\n", [$"{Unsatisfied} t", $"{Unproven} t"], "return t"));

    // A fixed-fixed relation is Binding's to judge (A3 §7, until R7): a node whose records are all fixed-fixed has no row, at a fit and at a
    // clause.
    [Theory]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during y => x\n", "x", "x")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n", "h0@move", "origin x.s outlives b")]
    public void AFixedRecordHasNoRow(string source, string at, string site)
        => Assert.Empty(Recorded(source, [$"{Unproven} {at}"], site));

    // Pinned until H11 (DECISIONS R4-M108, BU's `fncomp`): CheckTypeUse stops at the first differing Function Type component, so the
    // positions after it have no row: the recorded body chain `local@ref` over x has a row only when it comes first.
    [Theory]
    [InlineData("(g, local@ref)", "(ref/((ref/i32 during x) -> i32), ref/i32 during x)", "Fn:f Fit Fixed Fixed")]
    [InlineData("(local@ref, g)", "(ref/i32 during x, ref/((ref/i32 during x) -> i32))", "Fn:f Fit Finite Fixed|Fn:f Fit Fixed Fixed")]
    public void APositionAfterADifferingFunctionComponentHasNoRowUntilH11(string value, string result, string rows)
    {
        var source = $"func f(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> {result}\n    let local = 1\n    let t = {value}\n    return t\n";
        Assert.Equal(rows.Split('|'), Recorded(source, [$"{Unsatisfied} t", $"{Unproven} t"], "return t"));
    }

    // Pinned until 2c-6 (DECISIONS R4-M108, R5-M63's finding A): a Closure result over call-local storage keeps Binding's record without a
    // row.
    [Fact]
    public void AClosureResultOverCallLocalStorageHasNoRowUntil2c6()
        => Assert.Empty(Recorded("public func main()\n    let f = func (x: i32) => x@ref\n    let r = f(1)\n", [$"{Unsatisfied} x@ref"], "x@ref", main: false));

    // Pooled: a warm rebind of a program with recorded body chains, and the view's reads of their rows, allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RecordedChainsRowsRebindWithoutAllocating()
    {
        var c = MirBuildTest.Compile(Bad + LocalClause + Main);
        var bodies = Rows(c, false).Select(static x => x.Body).Distinct().ToArray();
        var count = Read();
        Assert.NotEqual(0, count);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            _ = Read();
        }));
        Assert.Equal(count, Read());

        int Read()
        {
            var rows = 0;
            foreach (var body in bodies)
            {
                rows += c.Types.OriginRows(body).Rows.Count;
            }

            return rows;
        }
    }

    // The program's records, each `Code location` in any order, and the rows at `site`, each `body source longer-class shorter-class` in
    // slice order.
    private static List<string> Recorded(string source, string[] records, string site, bool main = true)
    {
        source += main ? Main : string.Empty;
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}");
        Assert.Equal(records.Order(StringComparer.Ordinal), errors.Order(StringComparer.Ordinal));
        var c = MirBuildTest.Compile(source);
        Assert.False(c.Binding.Result.IsComplete);
        var rows = new List<string>();
        foreach (var body in Rows(c, false).Select(static x => x.Body).Distinct())
        {
            var slice = c.Types.OriginRows(body);
            foreach (var row in slice.Rows.Where(x => First(x.Site) == site))
            {
                rows.Add($"{Describe(body)} {row.Source} {slice.Atoms[row.Longer].Kind} {slice.Atoms[row.Shorter].Kind}");
            }
        }

        return rows;
    }
}
