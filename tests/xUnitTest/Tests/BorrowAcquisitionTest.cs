// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 7.6.2, 8.9: a bare acquisition of a Place storing an exclusive reference Reborrows it, as a same-Type
// annotation would; capture entries initialize environment bindings the same way; generic bodies use finite plans.
public class BorrowAcquisitionTest
{
    private const string Bump = "func bump(target: uniq/i32) => target@follow += 1\n";

    [Theory]
    [InlineData("Initializer", "var number: i32 = 1\nlet r = number@uniq\nlet first = r\nfirst@follow += 1\nlet second: uniq/i32 = r\nsecond@follow += 1\nr@follow += 1\nrequire number == 4 else => $abort(\"initializer\")")]
    [InlineData("Discard", "var number: i32 = 1\nlet r = number@uniq\n_ = r\nr@follow += 1\nlet moved = r@move\nmoved@follow += 1\nrequire number == 3 else => $abort(\"discard\")")]
    [InlineData("Repeated", Bump + "var number: i32 = 1\nlet r = number@uniq\nbump(r)\nlet s = r\nbump(s)\nbump(r)\nrequire number == 4 else => $abort(\"repeated\")")]
    [InlineData("TupleElement", Bump + "var number: i32 = 1\nlet r = number@uniq\nlet pair = (r, 1)\nbump(pair.0)\nrequire number == 2 else => $abort(\"tuple\")")]
    [InlineData("FixedArrayElement", Bump + "var a: i32 = 1\nvar b: i32 = 5\nvar refs: [2 of uniq/i32] = [a@uniq, b@uniq]\nlet first = refs[0]\nbump(first)\nlet second: uniq/i32 = refs[1]\nbump(second)\nbump(refs[1])\n_ = refs[0]\nrequire a == 2 and b == 7 else => $abort(\"array\")")]
    [InlineData("Field", Bump + "struct Holder {source}\n    public var link: uniq/i32 during source\n    public init(link: uniq/i32 during source)\n        self.link = link@move\nfunc touch(holder: uniq/Holder)\n    let link = holder.link\n    bump(link)\n    bump(holder.link)\nvar number: i32 = 1\nvar holder = Holder.init(number@uniq)\ntouch(holder@uniq)\nrequire number == 3 else => $abort(\"field\")")]
    [InlineData("ObjectHandle", "struct Counter\n    var value: i32 = 0\n    public func add(self: uniq/Self, n: i32) => self.value = self.value + n\n    public func read(self: ref/Self) -> i32 => self.value\nvar owner = Kimi.Intrinsics.makeObj(Counter.init())\ndo\n    let target = owner@objuniq\n    let second = target\n    second.add(2)\n    target.add(3)\nrequire owner.read() == 5 else => $abort(\"objuniq\")")]
    [InlineData("CaptureReborrow", "var number: i32 = 1\nlet r = number@uniq\nlet view: ref/i32 = r\nlet reader = func [view] () -> i32 => view@follow\nlet snapshot = reader()\nvar bump = func [r] () => r@follow += 1\nbump()\nbump()\nrequire snapshot == 1 and number == 3 else => $abort(\"reborrow\")")]
    [InlineData("CaptureMove", "var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@move] () => r@follow += 1\nbump()\nbump()\nrequire number == 3 else => $abort(\"move\")")]
    [InlineData("CaptureSharedSlot", "var number: i32 = 1\nlet reader = func [number@ref] () -> i32 => number@follow\nrequire reader() == 1 else => $abort(\"ref\")\nnumber += 1\nrequire number == 2 else => $abort(\"after\")")]
    [InlineData("CaptureExclusiveSlot", "var number: i32 = 1\nvar bump = func [number@uniq] () -> ()\n    number@follow += 1\nbump()\nbump()\nrequire number == 3 else => $abort(\"uniq\")")]
    [InlineData("GenericPlan", "func count<s/T>(value: s/T) -> i32\n    s is owner or uniq\n    T is Copy\n    var total = 0\n    for i in 0..3\n        let local = value\n        _ = local\n        total += i\n    _ = value\n    return total\nfunc pass<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    let local = value\n    return local@move\nvar number: i32 = 1\nrequire count(number) == 3 and count(number@uniq) == 3 else => $abort(\"count\")\nlet copied = pass(number)\nlet reborrowed = pass(number@uniq)\nreborrowed@follow += 1\nrequire copied == 1 and number == 2 else => $abort(\"pass\")")]
    public void EmitsBareReborrows(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowAcquisition" + name, source, string.Empty);

    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@uniq] () => r@follow@follow += 1\nbump()", nameof(BindingFailure.InvalidAssignment))]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet bump = func [r] () => r@follow += 1\nbump()", nameof(BindingFailure.InvalidAssignment))]
    [InlineData("let text = \"owned\"\nlet copy = func [text] () => ()", nameof(BindingFailure.TransferRequired))]
    public void RejectsInvalidCaptureEntries(string source, string failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure.ToString() == failure);
    }

    // DIAGNOSTICS.md §10: a rejected entry is located at the entry and names the initialization it stands for; an omitted
    // list names the capture; an Exclusive call of a let closure names the callee.
    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@uniq] () => r@follow@follow += 1\nbump()", "InvalidAssignment_Kd", "r@uniq", "r cannot be written", "slot of the let binding r", false)]
    [InlineData("let text = \"owned\"\nlet keep = func [text] () => ()\nkeep()", "TransferRequired_Kd", "text", "text cannot be read as a Copy value", "let text = text", true)]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet bump = func [r] () => r@follow += 1\nbump()", "InvalidAssignment_Kd", "bump", "bump cannot be written", "Exclusive", false)]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func () => r@follow += 1\nbump()", "TransferRequired_Kd", "func () => r@follow += 1", "r cannot be read as a Copy value", "omitted capture list", false)]
    [InlineData("let text = \"owned\"\nlet keep = func () => Console.writeLine(text)\nkeep()", "TransferRequired_Kd", "func () => Console.writeLine(text)", "text cannot be read as a Copy value", "omitted capture list", false)]
    public void ExplainsRejectedCaptures(string source, string code, string text, string label, string note, bool candidates)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
        Assert.Contains(note, error.Note);
        if (candidates)
        {
            // SPEC 23.3.6.9: a bare entry offers the transfer and the borrow as candidates at the entry.
            Assert.Equal(["Repair.Transfer", "Repair.Borrow"], error.Repairs!.Select(static x => x.Kind));
            Assert.Contains("[text@move]", UnnecessaryUnsafeBlockTest.Apply(source, error.Repairs![0].Edits), StringComparison.Ordinal);
            Assert.Contains("[text@ref]", UnnecessaryUnsafeBlockTest.Apply(source, error.Repairs[1].Edits), StringComparison.Ordinal);
            Assert.Equal([Kimi.Diagnostics.RepairCondition.Take], error.Repairs[0].Verified);
            Assert.Empty(error.Repairs[1].Verified);
        }
    }

    [Fact]
    public void ACaptureEntryExplainsOnlyItsOwnFailure()
    {
        // The closure already failed for its duplicate parameter, so the rejected entry records nothing: the duplicate is not
        // reported at the capture entry as if it were the entry's problem.
        const string Source = "let n: i32 = 1\nlet f = func [n@uniq] (a: i32, a: i32) -> i32 => a";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(Kimi.DiagnosticCode.DuplicateBinding_Kd), error.Code);
        Assert.NotEqual("n@uniq", error.Text);
    }

    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet first = r\nr@follow += 1\nfirst@follow += 1")]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r] () => r@follow += 1\nr@follow += 1\nbump()")]
    [InlineData("var number: i32 = 1\nlet reader = func [number@ref] () -> i32 => number@follow\nnumber += 1\n_ = reader()")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value\n    _ = value\n    _ = local\nvar number: i32 = 1\nf(number)")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value@move\n    let b = local\n    _ = local\n    _ = b\nvar number: i32 = 1\nf(number)")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value\n    let b = local\n    _ = value\n    _ = b\nvar number: i32 = 1\nf(number)")]
    public void RejectsUsesDuringReborrowLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("func f<T>(value: T) -> ()\n    let local = value\n    _ = local")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    let local = value\n    _ = local")]
    [InlineData("let text = \"owned\"\nlet copy = text")]
    public void RejectsBareAcquisitionWithoutPlan(string source)
        => Assert.Contains(MinimalEmissionTest.Analyze(source).Ownership.Issues, static x => x.Failure == OwnershipFailure.TransferRequired);
}
