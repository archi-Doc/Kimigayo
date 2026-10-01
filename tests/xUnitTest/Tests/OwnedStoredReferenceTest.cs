// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 13.5.5.1, 15.6.2: a reference stored in an inline Field, Tuple element or nested part of an owned root is followed
// through that slot; let forbids replacing the slot but keeps the stored reference's capability.
public class OwnedStoredReferenceTest
{
    private const string Holder = "struct Holder {a}\n    public let item: uniq/i32 during a\n    public let extra: i32\n    public init(item: uniq/i32 during a, extra: i32)\n        self.item = item@move\n        self.extra = extra\n";

    [Theory]
    [InlineData("LetWrite", "var value = 42\nlet item = (value@uniq, 7)\nitem.0@follow = 99\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("VarWrite", "var value = 42\nvar item = (value@uniq, 7)\nitem.0@follow = 99\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("Compound", "var value = 42\nlet item = (value@uniq, 7)\nitem.0@follow += 57\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("Read", "var value = 42\nlet item = (value@uniq, 7)\nlet read: i32 = item.0@follow\nrequire read == 42 else => $abort(\"read\")")]
    [InlineData("Reborrow", "var value = 42\nlet item = (value@uniq, 7)\nlet target = item.0@follow@uniq\ntarget@follow = 99\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("DisjointRead", "var value = 42\nlet item = (value@uniq, 7)\nlet child = item.0@follow@ref\nlet other = item.1\nrequire child == 42 and other == 7 else => $abort(\"child\")")]
    [InlineData("Parameter", "func run(value: uniq/i32 during a)\n    let item = (value@move, 7)\n    item.0@follow = 99\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("Nested", "func run(value: uniq/i32 during a)\n    let nested = ((value@move, 1), 2)\n    nested.0.0@follow = 99\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("OptionPayload", "func run(value: uniq/i32 during a)\n    let item = Option.Some((value@move, 7))\n    match item@move\n        .Some(let pair) => pair.0@follow = 99\n        .None => ()\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("DictionaryRemoved", "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n    match entries.remove(1)\n        .Some((let key, let item)) => item.0@follow = 99\n        .None => $abort(\"missing\")\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("StructField", Holder + "func run(value: uniq/i32 during a)\n    let holder = Holder.init(value@move, 7)\n    holder.item@follow += 57\n    let child = holder.item@follow@ref\n    require child == 99 and holder.extra == 7 else => $abort(\"child\")\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("DictionaryRemovedStruct", Holder + "func run(value: uniq/i32 during a)\n    var entries = [1: Holder.init(value@move, 7)]\n    match entries.remove(1)\n        .Some((let key, let holder)) => holder.item@follow = 99\n        .None => $abort(\"missing\")\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    public void StoredReferencesInOwnedPathsLendTheirCapability(string name, string source)
        => ScalarEmissionTest.EmitFixture("OwnedStoredReference" + name, source, string.Empty);

    // An owning-iteration item was transferred out of the iterator, which keeps the input's authority for the remaining
    // elements; the item's slot borrow descends from the iterator like a direct use of the item (SPEC 14.6.2, 15.6.2).
    [Theory]
    [InlineData("ArrayWrite", "var items = [(value@move, 7)]\n    for item in items@move\n        item.0@follow = 99")]
    [InlineData("ArrayRead", "var items = [(value@move, 7)]\n    for item in items@move\n        let read: i32 = item.0@follow\n        require read == 42 else => $abort(\"read\")\n        item.0@follow = 99")]
    [InlineData("ArrayMoved", "var items = [(value@move, 7)]\n    for item in items@move\n        let pair = item@move\n        pair.0@follow = 99")]
    [InlineData("DictionaryWrite", "var entries = [1: (value@move, 7)]\n    for (key, item) in entries@move\n        item.0@follow = 99")]
    [InlineData("DictionaryMoved", "var entries = [1: (value@move, 7)]\n    for (key, item) in entries@move\n        let pair = item@move\n        pair.0@follow += 57\n        pair.0@follow -= 57\n        pair.0@follow = 99")]
    public void OwningIterationItemsLendTheirStoredReferences(string name, string body)
    {
        var source = "func run(value: uniq/i32 during a)\n    " + body + "\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")";
        ScalarEmissionTest.EmitFixture("OwnedStoredReferenceIteration" + name, source, string.Empty);
    }

    // SPEC 10.2: at a ref/uniq parameter the stored reference is Reborrowed through its slot, as at a borrowed root.
    private const string Calls = "func change(target: uniq/i32) => target@follow = 99\nfunc inspect(target: ref/i32) -> i32 => target@follow\nfunc both(target: uniq/i32, other: ref/i32) => target@follow = other@follow + 1\n";

    [Theory]
    [InlineData("LetExclusive", "var value = 42\nlet item = (value@uniq, 7)\nchange(item.0)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("VarExclusive", "var value = 42\nvar item = (value@uniq, 7)\nchange(item.0)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("Shared", "var value = 42\nlet item = (value@uniq, 7)\nrequire inspect(item.0) == 42 else => $abort(\"read\")")]
    [InlineData("Field", Holder + "func run(value: uniq/i32 during a)\n    let holder = Holder.init(value@move, 7)\n    require inspect(holder.item) == 42 else => $abort(\"read\")\n    change(holder.item)\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("Iteration", "func run(value: uniq/i32 during a)\n    var items = [(value@move, 7)]\n    for item in items@move\n        require inspect(item.0) == 42 else => $abort(\"read\")\n        change(item.0)\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    public void StoredReferenceArgumentsReborrowThroughTheSlot(string name, string source)
        => ScalarEmissionTest.EmitFixture("OwnedStoredReferenceArgument" + name, Calls + source, string.Empty);

    [Theory]
    [InlineData("let child = item.0@follow@ref\nchange(item.0)\nrequire child == 42 else => $abort(\"child\")", true)]
    [InlineData("both(item.0, item.0)", true)]
    [InlineData("let child = item.0@follow@uniq\nlet read = inspect(item.0)\nchild@follow = 1", false)]
    public void StoredReferenceArgumentsRespectLiveChildren(string body, bool activation)
    {
        var c = MinimalEmissionTest.Analyze(Calls + "var value = 42\nlet item = (value@uniq, 7)\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Activation == activation);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("var items = [(value@move, 7)]\n    for item in items@move\n        let child = item.0@follow@uniq\n        item.0@follow = 5\n        child@follow = 1", "item.0")]
    [InlineData("let first = (value@follow@uniq, 1)\n    value@follow = 5\n    first.0@follow = 99", "value")]
    public void ChildrenOfTheItemStillSuspendItsParent(string body, string conflict)
    {
        var c = MinimalEmissionTest.Analyze("func run(value: uniq/i32 during a)\n    " + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == conflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let child = item.0@follow@uniq\nitem.0@follow = 5\nchild@follow = 1", "item.0")]
    [InlineData("let child = item.0@follow@ref\nitem.0@follow = 5\nrequire child == 42 else => $abort(\"child\")", "item.0")]
    [InlineData("let child = item.0@follow@uniq\nlet copy = item.0@follow\nchild@follow = 1", "item.0")]
    [InlineData("let child = item.0@follow@uniq\nlet slot = item@ref\nlet read: i32 = slot.0@follow\nchild@follow = 1", "slot.0")]
    [InlineData("value = 5\nitem.0@follow = 99", "value = 5")]
    public void ConflictingAccessThroughTheParentRejects(string body, string conflict)
    {
        var c = MinimalEmissionTest.Analyze("var value = 42\nvar item = (value@uniq, 7)\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == conflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AMovedRootNoLongerLends()
    {
        var c = MinimalEmissionTest.Analyze("var value = 42\nlet item = (value@uniq, 7)\nlet moved = item@move\nitem.0@follow = 99");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.PossiblyMovedUse && x.Source.ToString() == "item.0");
    }

    [Fact]
    public void ASharedStoredReferenceStaysReadOnly()
    {
        var c = MinimalEmissionTest.Analyze("var value = 42\nlet item = (value@ref, 7)\nitem.0@follow = 99");
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.SharedPathAccess_Kd), error.Code);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void OwnedStoredReferenceAnalysisReusesItsStorage()
    {
        var c = MinimalEmissionTest.Analyze("func run(value: uniq/i32 during a)\n    let item = (value@move, 7)\n    item.0@follow += 1\n    let child = item.0@follow@ref\n    require child == 43 else => $abort(\"value\")");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }
}
