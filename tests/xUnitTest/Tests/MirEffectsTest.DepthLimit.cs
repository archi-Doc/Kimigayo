// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>PLAN G272's shared acceptance rows for A4 (problem statement r2 §3 A4): a confined witness destroying a value whose destruction
/// walk passes the bound of 32 (A4's <c>Glue.Deep</c>) cannot be judged there: one <c>OwnershipDepthLimit_Kd</c> record, never a
/// Language record, and none at a shallow depth. Z25 (DECISIONS R4-M153, R7-M54): an instance A4 composes or a published row's parameter
/// past Binding's storage bound (<c>TyCtxt.ReadableInstance</c>'s null) is the same record, as A5 reads retention's limit.</summary>
public sealed partial class MirEffectsTest
{
    // AGrowingRecursiveDestructionEndsAtTheBound's growing Type.
    private const string GrowingNest = "struct Nest<T>\n    var value: T\n    var next: Option<rc/Nest<(T, T)>>\n    public init(value: T)\n" +
        "        self.value = value@move\n        self.next = .None\n";

    // Z25's forms: Bx nests a Type past Binding's storage bound (height 64) at 65 levels; H's functions reach it through a composed atom
    // (release's Drop(T)) and a composed published row (wipe's Array.clear).
    private const string Boxes = "struct Bx<T>\n    public var v: T\ngroup H\n    public func release<T>(x: T) => ()\n" +
        "    public func wipe<T>(a: uniq/Array<T>) => a.clear()\n";

    // A Tuple of integers nested 40 levels destroyed in the confined witness: its destruction walk passes the bound (dev: the located
    // Unsupported_Kd at `Self is Sink`), so the obligation's verdict rests on it: one limit record and nothing else. rel65-loud: Loud's
    // printing destructor under Option<Bx^65<Loud>>, past Binding's bound, is undecided there (IMPL A.4; before Z25: Unsupported_Kd).
    [Theory]
    [InlineData("tuple40")]
    [InlineData("rel65-loud")]
    public void ACheckRestingOnAPartPastTheBoundGivesOneLimitRecord(string form)
    {
        Assert.Equal([MirBorrowsTest.DepthLimit], DepthRows(form).Select(static x => x.Code.ToString()));
        MirBorrowsTest.AssertTheDepthLimitIsAResourceError();
    }

    // The shallow twins: the same Tuple nested 10 levels is judged, and it has no effect; so are Z25's forms at 20 levels.
    [Theory]
    [InlineData("tuple10")]
    [InlineData("rel20")]
    [InlineData("wipe20")]
    [InlineData("clear20")]
    public void NoCheckRestingOnTheBoundGivesNoRecord(string form)
        => Assert.Empty(DepthRows(form).Select(static x => $"{x.Line} {x.Code}"));

    // rel65-loud's twin within the bound (SPEC 8.11.2): Loud's destructor is decided, the Language record.
    [Fact]
    public void ADecidedPartWithinTheBoundIsTheLanguageRecord()
        => Assert.Equal(["Self is Sink IncompatibleContractImplementation_Kd"], DepthRows("rel20-loud").Select(static x => $"{x.Line} {x.Code}"));

    // AGrowingRecursiveDestructionEndsAtTheBound's witness: its located Unsupported_Kd, A4's Limit for the bound, becomes the limit record
    // (problem r2: A4's Limit for the bound becomes this record), and nothing else is recorded; that pin's expectation changes with it. Z25:
    // so does A4's Limit for an instance past Binding's bound (E4: rel65, release's atom; wipe65, wipe's row; E3: clear65, Array.clear's
    // row in the witness), beside A5's own record at the call (wipe65, clear65); fail-before: Unsupported_Kd at `Self is Sink`.
    [Theory]
    [InlineData("nest", 1)]
    [InlineData("rel65", 1)]
    [InlineData("wipe65", 2)]
    [InlineData("clear65", 2)]
    public void TheBoundsLocatedRecordsBecomeTheLimitRecord(string form, int records)
    {
        Assert.Equal(Enumerable.Repeat(MirBorrowsTest.DepthLimit, records), DepthRows(form).Select(static x => x.Code.ToString()));
        MirBorrowsTest.AssertTheDepthLimitIsAResourceError();
    }

    // Problem r2 §3, a row a design may decide (scored apart): A4's limit record at the obligation, where its Limit's Unsupported_Kd lies.
    [Theory]
    [InlineData("nest", "Self is Sink OwnershipDepthLimit_Kd")]
    [InlineData("tuple40", "Self is Sink OwnershipDepthLimit_Kd")]
    [InlineData("rel65", "Self is Sink OwnershipDepthLimit_Kd")]
    [InlineData("rel65-loud", "Self is Sink OwnershipDepthLimit_Kd")]
    [InlineData("wipe65", "Self is Sink OwnershipDepthLimit_Kd|H.wipe(self.arr@uniq) OwnershipDepthLimit_Kd")]
    [InlineData("clear65", "Self is Sink OwnershipDepthLimit_Kd|self.arr.clear() OwnershipDepthLimit_Kd")]
    public void TheLimitRecordsLocationLeftToTheDesign(string form, string expected)
        => Assert.Equal(
            MirBorrowsTest.Rows(expected),
            DepthRows(form).Where(static x => x.Code.ToString() == MirBorrowsTest.DepthLimit).Select(static x => $"{x.Line} {x.Code}"));

    // The MIR records of module 0 of a form's program.
    private static (string Line, Kimi.DiagnosticCode Code)[] DepthRows(string form)
    {
        var (declarations, field, body) = form switch
        {
            "nest" => (GrowingNest, null, "        _ = Nest<i32>.init(value: 1)\n"),
            "tuple40" => (string.Empty, null, $"        _ = {Nested(40)}\n"),
            "tuple10" => (string.Empty, null, $"        _ = {Nested(10)}\n"),
            "rel65" or "rel20" => (Boxes, null, $"        H.release<Option<{Bx(form[3..], "i32")}>>(.None)\n"),
            "rel65-loud" or "rel20-loud" => (Boxes, null, $"        H.release<Option<{Bx(form[3..5], "Loud")}>>(.None)\n"),
            "wipe65" or "wipe20" => (Boxes, $"arr: Array<{Bx(form[4..], "i32")}> = []", "        H.wipe(self.arr@uniq)\n"),
            "clear65" or "clear20" => (Boxes, $"arr: Array<{Bx(form[5..], "i32")}> = []", "        self.arr.clear()\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var witness = field is null ? Witness : WithField(field);
        var c = MirBorrowsTest.CompileDepthForm(Sink + Loud + declarations + witness + body + "        return .Ok(())\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return MirBorrowsTest.DepthRecords(c, program, null);

        static string Nested(int levels) => new string('(', levels) + "0" + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));

        static string Bx(string levels, string leaf)
        {
            var n = int.Parse(levels, System.Globalization.CultureInfo.InvariantCulture);
            return string.Concat(Enumerable.Repeat("Bx<", n)) + leaf + new string('>', n);
        }
    }
}
