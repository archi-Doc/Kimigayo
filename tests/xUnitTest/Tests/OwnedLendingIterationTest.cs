// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class OwnedLendingIterationTest
{
    private const string Source =
        "struct Cursor\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    var count: i32 = 0\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        if self.count == 3 => return .None\n        self.count += 1\n        return .Some(self.count@ref)\nstruct Values\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Cursor\n    public func intoIterator(self: Self) -> Cursor => Cursor.init()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedEntryCanReturnASelfLendingIterator(bool skipMiddle)
    {
        var source = Source + "var values = Values.init()\nvar sum = 0\nfor item in values@move\n" +
            (skipMiddle ? "    if item == 2 => continue\n" : string.Empty) +
            "    sum += item\nrequire sum == " + (skipMiddle ? "4" : "6") + " else => $abort(\"sum\")\nConsole.writeLine(\"owned lending\")";
        ScalarEmissionTest.EmitFixture("AssociatedOwnedLending" + (skipMiddle ? "Continue" : "Backedge"), source, "owned lending\n");
    }

    // SPEC 15.6.4 (R4-M170): the program is valid, but since G264 (S) S2 A5 errs twice at count's `for` (the lent item's composed erased
    // destruction stores next()'s Loan into the iterator, a self-row), and generation refuses it; the old analysis accepted and ran it to
    // "generic lending\n" (fixture AssociatedOwnedLendingGeneric). H5's (e) list, row "S2's self-row family" (the owner's O-9, 「(a) 加えて
    // 着地」): PLAN G416 restores the acceptance and re-points this test back to its run.
    [Fact]
    public void GenericOwnedEntryUsesTheLendingRequirement()
    {
        var source = Source + "func count<T>(values: T) -> i32\n    T is IntoIterable\n    var count = 0\n    for _ in values@move => count += 1\n    return count\nrequire count(Values.init()) == 3 else => $abort(\"count\")\nConsole.writeLine(\"generic lending\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        var loop = new SyntaxLocation("for _ in values@move => count += 1");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, loop), (DiagnosticCode.ComparisonLoanConflict_Kd, loop));
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void OwningIterationConsumesItsSource()
    {
        var c = MinimalEmissionTest.Analyze(Source + "var values = Values.init()\nfor _ in values@move => ()\nlet again = values@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
    }

    [Fact]
    public void AnOwnedLentItemCannotEscapeTheIterator()
    {
        var c = MinimalEmissionTest.Analyze(Source + "func escape() -> ref/i32 during static\n    for item in Values.init()\n        return item\n    $abort(\"empty\")");
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }
}
