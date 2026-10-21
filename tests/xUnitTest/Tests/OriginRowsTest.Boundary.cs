// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The boundary (2a0-D §5.1 with DECISIONS R4-M108): a relation Binding records at a body chain (2a0-D D1: an end, resolved at its use, is
// not fixed) keeps its record, `Binding.OriginRelation` with its code and location, and has its rows in its body, which A5 judges: every
// position of a non-call fit, a local's clause. A fixed-fixed record has no row (Binding's until R7), nor, until 2c-6, a Closure result
// over call-local storage.
public sealed partial class OriginRowsTest
{
    private const string Holder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";

    private const string Bad = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n";

    private const string LocalClause = Holder + "func f(a: ref/i32) -> i32\n    let local = 3\n    let h: H{x} = H.init(local@ref)\n        origin x.s outlives a\n" +
        "    return h.item@follow\n";

    // SPEC 15.6.5's `bad`: the return's fit is Refuted at the Borrow, and its position is a row of bad, finite to fixed.
    [Fact]
    public void ARecordedFitsPositionIsARowOfItsBody()
    {
        var row = Assert.Single(Recorded(Bad, nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "return local@ref"));
        Assert.Equal(("Fn:bad", OriginRowSource.Fit, OriginAtomKind.Finite, OriginAtomKind.Fixed), row);
    }

    // SPEC 15.3.3, 15.6.5: a local's clause over a slot its initializer supplies with a Borrow of a local is Refuted at the initializer, and
    // the clause is a row of the function, finite to fixed.
    [Fact]
    public void ARecordedClauseIsARowOfItsBody()
    {
        var row = Assert.Single(Recorded(LocalClause, nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "H.init(local@ref)", "origin x.s outlives a"));
        Assert.Equal(("Fn:f", OriginRowSource.Declared, OriginAtomKind.Finite, OriginAtomKind.Fixed), row);
    }

    // A fixed-fixed relation is Binding's to judge (A3 §7, until R7): its record has no row, at a fit and at a clause.
    [Theory]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during y => x\n", "x", "x")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n", "h0@move", "origin x.s outlives b")]
    public void AFixedRecordHasNoRow(string source, string at, string site)
        => Assert.Empty(Recorded(source, nameof(DiagnosticCode.UnprovenOriginRelation_Kd), at, site));

    // Pinned until 2c-6 (DECISIONS R4-M108, R5-M63's finding A): a Closure result over call-local storage keeps Binding's record without a
    // row.
    [Fact]
    public void AClosureResultOverCallLocalStorageHasNoRowUntil2c6()
        => Assert.Empty(Recorded("public func main()\n    let f = func (x: i32) => x@ref\n    let r = f(1)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "x@ref", "x@ref", main: false));

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

    // The program's one record, with its code at its location, and the rows at `site`: their body, source and end classes.
    private static List<(string Body, OriginRowSource Source, OriginAtomKind Longer, OriginAtomKind Shorter)> Recorded(string source, string code, string at, string site, bool main = true)
    {
        source += main ? Main : string.Empty;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var c = MirBuildTest.Compile(source);
        Assert.False(c.Binding.Result.IsComplete);
        var rows = new List<(string, OriginRowSource, OriginAtomKind, OriginAtomKind)>();
        foreach (var body in Rows(c, false).Select(static x => x.Body).Distinct())
        {
            var slice = c.Types.OriginRows(body);
            foreach (var row in slice.Rows.Where(x => First(x.Site) == site))
            {
                rows.Add((Describe(body), row.Source, slice.Atoms[row.Longer].Kind, slice.Atoms[row.Shorter].Kind));
            }
        }

        return rows;
    }
}
