// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StoredReferenceLendingTest
{
    private const string Declarations = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    let value: uniq/i32 during source\n    public init(value: uniq/i32 during source) => self.value = value@move\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        self.value@follow += 1\n        return .Some(self.value@follow@ref)\nfunc advance<T>(cursor: uniq/T during step) -> Option<T.(LendingIterator).LentItem(step)>\n    T is LendingIterator\n    return cursor.next()\n";

    [Theory]
    [InlineData(false, "Compound", "self.value@follow += 1")]
    [InlineData(true, "Compound", "self.value@follow += 1")]
    [InlineData(false, "Assignment", "self.value@follow = self.value@follow + 1")]
    [InlineData(true, "Assignment", "self.value@follow = self.value@follow + 1")]
    [InlineData(false, "Postfix", "self.value@follow++")]
    [InlineData(true, "Postfix", "self.value@follow++")]
    public void LendingStepsCanUpdateAndReborrowStoredExclusiveReferences(bool generic, string name, string update)
    {
        var next = generic ? "advance(cursor@uniq)" : "cursor.next()";
        var source = Declarations.Replace("self.value@follow += 1", update, StringComparison.Ordinal) + "var value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = " + next + "\nmatch first\n    .Some(let item) => require item == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nlet second = " + next + "\nmatch second\n    .Some(let item) => require item == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"stored lending\")";
        ScalarEmissionTest.EmitFixture("AssociatedStoredReferenceLending" + name + (generic ? "Generic" : "Concrete"), source, "stored lending\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedStoredReferentBlocksTheNextLendingStep(bool generic)
    {
        var next = generic ? "advance(cursor@uniq)" : "cursor.next()";
        var source = Declarations + "var value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = " + next + "\nlet second = " + next + "\nmatch first\n    .Some(let item) => Console.writeLine(\"first\")\n    .None => ()\n_ = second";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("self.value@follow += 1")]
    [InlineData("self.value@follow++")]
    [InlineData("_ = self.value@follow@uniq")]
    public void SharedReceiverCannotGrantExclusiveReferentAccess(string operation)
    {
        var c = MinimalEmissionTest.Analyze("struct Cursor {source}\n    let value: uniq/i32 during source\n    public func read(self: ref/Self)\n        " + operation);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.SharedPathAccess_Kd);
    }
}
