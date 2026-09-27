// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class IteratorIndependenceTest
{
    [Theory]
    [InlineData("Iterator", "Iterator.Item", false)]
    [InlineData("LendingIterator", "LentItem(step)", true)]
    public void RepeatedWriteToPublishedReferentNeedsIndependence(string contract, string item, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is " + contract + "\n    associate " + item + " is ref/i32 during source\n    let value: uniq/i32 during source\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during source>\n        self.value@follow += 1\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Fact]
    public void SharedExternalItemsAllowCursorUpdates()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void SharedItemsRemainUsableAfterAnotherStep()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)\nlet value = 42\nvar cursor = Cursor.init(value@ref)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let item) => require item == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let item) => require item == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"independent\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIndependentShared", source, "independent\n");
    }

    [Theory]
    [InlineData("self.value@follow += 1", false)]
    [InlineData("self.count += 1", true)]
    public void HelperEffectsParticipateInIndependence(string operation, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    func update(self: uniq/Self)\n        " + operation + "\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update()\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Theory]
    [InlineData("self.value@follow += 1", false)]
    [InlineData("self.count += 1", true)]
    public void RecursiveGenericHelperEffectsParticipateInIndependence(string operation, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    func update<T>(self: uniq/Self, depth: i32)\n        if depth > 0 => self.update<T>(depth - 1)\n        " + operation + "\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update<i32>(2)\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Fact]
    public void UncalledMutatingHelperDoesNotAffectIndependence()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    func update(self: uniq/Self) => self.value@follow += 1\n    public func next(self: uniq/Self) -> Option<ref/i32 during source> => .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("T", "self.value@follow += 1", false)]
    [InlineData("T", "self.count += 1", true)]
    [InlineData("bool", "self.value@follow += 1", true)]
    [InlineData("i32", "self.value@follow += 1", false)]
    public void GenericCallsCheckEverySelectableSpecialization(string argument, string operation, bool valid)
    {
        var source = "struct Cursor<T> {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => " + operation + "\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update<" + argument + ">()\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Theory]
    [InlineData("2", true)]
    [InlineData("3", false)]
    public void LengthCallsCheckTheSelectedSpecialization(string argument, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    func update<length M>(self: uniq/Self) => self.count += 1\n    specialize func update<3>(self: uniq/Self) => self.value@follow += 1\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update<" + argument + ">()\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Theory]
    [InlineData("(T, bool)", false)]
    [InlineData("(T, i32)", true)]
    public void CompoundSpecializationKeysKeepTheirFixedParts(string argument, bool valid)
    {
        var source = "struct Cursor<T> {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<(i32, bool)>(self: uniq/Self) => self.value@follow += 1\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update<" + argument + ">()\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Theory]
    [InlineData("ref", true)]
    [InlineData("uniq", false)]
    public void PublishedPayloadLoansAreEffectsOfNext(string semantics, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is " + semantics + "/i32 during source\n    let value: uniq/i32 during source\n    public func next(self: uniq/Self) -> Option<" + semantics + "/i32 during source> => .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }
}
