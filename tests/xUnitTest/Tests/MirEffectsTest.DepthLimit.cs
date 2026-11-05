// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>PLAN G272's shared acceptance rows for A4 (problem statement r2 §3 A4): a confined witness destroying a value whose destruction
/// walk passes the bound of 32 (A4's <c>Glue.Deep</c>) cannot be judged there: one <c>OwnershipDepthLimit_Kd</c> record, never a
/// Language record, and none at a shallow depth.</summary>
public sealed partial class MirEffectsTest
{
    // AGrowingRecursiveDestructionEndsAtTheBound's growing Type.
    private const string GrowingNest = "struct Nest<T>\n    var value: T\n    var next: Option<rc/Nest<(T, T)>>\n    public init(value: T)\n" +
        "        self.value = value@move\n        self.next = .None\n";

    // A Tuple of integers nested 40 levels destroyed in the confined witness: its destruction walk passes the bound (dev: the located
    // Unsupported_Kd at `Self is Sink`), so the obligation's verdict rests on it: one limit record and nothing else.
    [Theory]
    [InlineData("tuple40")]
    public void ACheckRestingOnAPartPastTheBoundGivesOneLimitRecord(string form)
    {
        Assert.Equal([MirBorrowsTest.DepthLimit], DepthRows(form).Select(static x => x.Code.ToString()));
        MirBorrowsTest.AssertTheDepthLimitIsAResourceError();
    }

    // The shallow twin: the same Tuple nested 10 levels is judged, and it has no effect.
    [Theory]
    [InlineData("tuple10")]
    public void NoCheckRestingOnTheBoundGivesNoRecord(string form)
        => Assert.Empty(DepthRows(form).Select(static x => $"{x.Line} {x.Code}"));

    // AGrowingRecursiveDestructionEndsAtTheBound's witness: its located Unsupported_Kd, A4's Limit for the bound, becomes the limit record
    // (problem r2: A4's Limit for the bound becomes this record), and nothing else is recorded; that pin's expectation changes with it.
    [Theory]
    [InlineData("nest")]
    public void TheBoundsLocatedRecordsBecomeTheLimitRecord(string form)
    {
        Assert.Equal([MirBorrowsTest.DepthLimit], DepthRows(form).Select(static x => x.Code.ToString()));
        MirBorrowsTest.AssertTheDepthLimitIsAResourceError();
    }

    // Problem r2 §3, a row a design may decide (scored apart): A4's limit record at the obligation, where its Limit's Unsupported_Kd lies.
    [Theory]
    [InlineData("nest", "Self is Sink OwnershipDepthLimit_Kd")]
    [InlineData("tuple40", "Self is Sink OwnershipDepthLimit_Kd")]
    public void TheLimitRecordsLocationLeftToTheDesign(string form, string expected)
        => Assert.Equal(
            MirBorrowsTest.Rows(expected),
            DepthRows(form).Where(static x => x.Code.ToString() == MirBorrowsTest.DepthLimit).Select(static x => $"{x.Line} {x.Code}"));

    // The MIR records of module 0 of a form's program.
    private static (string Line, Kimi.DiagnosticCode Code)[] DepthRows(string form)
    {
        var (declarations, body) = form switch
        {
            "nest" => (GrowingNest, "        _ = Nest<i32>.init(value: 1)\n"),
            "tuple40" => (string.Empty, $"        _ = {Nested(40)}\n"),
            "tuple10" => (string.Empty, $"        _ = {Nested(10)}\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var c = MirBorrowsTest.CompileDepthForm(Sink + Loud + declarations + Witness + body + "        return .Ok(())\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return MirBorrowsTest.DepthRecords(c, program, null);

        static string Nested(int levels) => new string('(', levels) + "0" + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));
    }
}
