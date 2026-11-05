// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>PLAN G272's shared acceptance rows on this class's own programs (problem statement r2 §3 p1 and the ledger rows): a part
/// shared at depth 0 and N levels down a Tuple, as <see cref="ADeepSharedPartKeepsTheBoundsVerdict"/> builds it, on the MIR. Past the
/// walk's bound of 32 the Drop of d is not a use that keeps the Loan live: one <c>OwnershipDepthLimit_Kd</c> record at `let d` where the
/// write's check rests on it, no conflict, and no record without the write or at the bound. The rows read the MIR's records only: the old
/// path keeps its own bound (64) and its records, outside G272 (at 70 it still reports its conflict).</summary>
public partial class TypeDagScalingTest
{
    // Problem r2 §3 p1 (at 40, and at 33, the first depth past the bound) and the ledger rows 07dc9460e03d (60, d then s), 0984e5818869
    // (60, s then d), 56024d19baf2 (70, s then d) and e5c59f5f3cbf (70, d then s): the write conflicted with the Loan d's Drop kept (dev).
    [Theory]
    [InlineData(33, "d")]
    [InlineData(40, "d")]
    [InlineData(60, "d,s")]
    [InlineData(60, "s,d")]
    [InlineData(70, "s,d")]
    [InlineData(70, "d,s")]
    public void APartPastTheBoundGivesNoLanguageRecord(int depth, string order)
        => Assert.Empty(DepthRows(depth, order, true).Where(static x => MirBorrowsTest.IsLanguage(x.Code)).Select(static x => $"{x.Line} {x.Code}"));

    // The same forms: the write's check rests on the Drop of d past the bound, so one limit record, at d's declaration (as p4's).
    [Theory]
    [InlineData(33, "d")]
    [InlineData(40, "d")]
    [InlineData(60, "d,s")]
    [InlineData(60, "s,d")]
    [InlineData(70, "s,d")]
    [InlineData(70, "d,s")]
    public void ACheckRestingOnAPartPastTheBoundGivesOneLimitRecord(int depth, string order)
    {
        Assert.Equal(
            [$"let d = {DeepNest(depth)} {MirBorrowsTest.DepthLimit}"],
            DepthRows(depth, order, true).Where(static x => x.Code.ToString() == MirBorrowsTest.DepthLimit).Select(static x => $"{x.Line} {x.Code}"));
        MirBorrowsTest.AssertTheDepthLimitIsAResourceError();
    }

    // Problem r2 §3: p1's twin at the bound (32), and p1 without the write at 40 (a4-p1-40-nowrite) and at 70 beside s (td-70-nowrite):
    // no check rests on d's Drop, so no record.
    [Theory]
    [InlineData(32, "d", true)]
    [InlineData(40, "d", false)]
    [InlineData(70, "d,s", false)]
    public void NoCheckRestingOnTheBoundGivesNoRecord(int depth, string order, bool written)
        => Assert.Empty(DepthRows(depth, order, written).Select(static x => $"{x.Line} {x.Code}"));

    // The MIR records of module 0 of main: d nested to a depth and s nested 10 levels, in an order ("d", "d,s" or "s,d"), then the write.
    private static (string Line, Kimi.DiagnosticCode Code)[] DepthRows(int depth, string order, bool written)
    {
        var deep = $"    let d = {DeepNest(depth)}\n";
        var shallow = $"    let s = {DeepNest(10)}\n";
        var lets = order switch
        {
            "d" => deep,
            "d,s" => deep + shallow,
            "s,d" => shallow + deep,
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
        };
        var c = MirBorrowsTest.CompileDepthForm("public func main() -> ()\n    var value = 42\n" + lets + (written ? "    value = 99\n" : string.Empty) + "    return\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return MirBorrowsTest.DepthRecords(c, program, "main");
    }

    // A borrow of value nested in Tuples to a depth, each level paired with its index.
    private static string DeepNest(int levels) => new string('(', levels) + "value@ref" + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));
}
