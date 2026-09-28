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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpectedReferencesReborrowStoredExclusiveFields(bool retain)
    {
        var source = Declarations.Replace(".Some(self.value@follow@ref)", ".Some(self.value)", StringComparison.Ordinal) +
            "var value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\n" +
            (retain ? "let second = cursor.next()\n" : string.Empty) +
            "match first\n    .Some(let item) => require item == 41 else => $abort(\"value\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"implicit\")";
        if (retain)
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
            Assert.DoesNotContain(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.Unsupported);
        }
        else
        {
            ScalarEmissionTest.EmitFixture("AssociatedStoredReferenceImplicit", source, "implicit\n");
        }
    }

    [Fact]
    public void ExpectedScalarsReadTheReferentOfStoredExclusiveFields()
    {
        const string source = "struct Reader {source}\n    let value: uniq/i32 during source\n    public init(value: uniq/i32 during source) => self.value = value@move\n    public func read(self: ref/Self) -> i32 => self.value\nvar value = 42\nlet reader = Reader.init(value@uniq)\nrequire reader.read() == 42 else => $abort(\"copy\")\nConsole.writeLine(\"snapshot\")";
        ScalarEmissionTest.EmitFixture("AssociatedStoredReferenceSnapshot", source, "snapshot\n");
    }

    [Fact]
    public void ExclusiveEnumPayloadReborrowsKeepTheOriginalTarget()
    {
        const string source = "func wrap(value: uniq/i32) -> Option<uniq/i32 during value> => .Some(value)\nvar value = 40\nlet wrapped = wrap(value@uniq)\nmatch wrapped@move\n    .Some(let item) => item@follow += 2\n    .None => $abort(\"empty\")\nrequire value == 42 else => $abort(\"target\")\nConsole.writeLine(\"exclusive payload\")";
        ScalarEmissionTest.EmitFixture("AssociatedExclusiveEnumReborrow", source, "exclusive payload\n");
    }

    [Fact]
    public void EnumReferenceReadsRetainTheInnerLoan()
    {
        const string source = "func wrap(value: ref/(ref/i32 during source) during slot) -> Option<ref/i32 during source> => .Some(value)\nfunc run(first: ref/i32 during a, other: ref/i32 during a)\n    var slot = first\n    let wrapped = wrap(slot@ref)\n    slot = other\n    match wrapped\n        .Some(let item) => require item == 42 else => $abort(\"inner\")\n        .None => $abort(\"empty\")\n    Console.writeLine(\"inner loan\")\nlet value = 42\nlet other = 7\nrun(value@ref, other@ref)";
        ScalarEmissionTest.EmitFixture("AssociatedEnumReferenceLayers", source, "inner loan\n");
    }
}
