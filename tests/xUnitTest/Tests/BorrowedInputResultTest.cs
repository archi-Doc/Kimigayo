// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1 well-formedness, 15.6.4 steps 4-6: a call result over a borrowed input's Origin (`during self`) may hold what that
// input's referent holds, so while the result lives it keeps the Loans that referent carries, such as a stored reference's.
public class BorrowedInputResultTest
{
    private const string Holder = "struct H {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n" +
        "    public func get(self: ref/Self) -> ref/i32 during self => self.item\n";

    private const string Exclusive = "struct E {a}\n    public var view: uniq/i32 during a\n\n    public init(view: uniq/i32 during a) => self.view = view\n\n" +
        "    public func take(self: uniq/Self) -> uniq/i32 during self => self.view\n    public func peek(self: ref/Self) -> ref/i32 during self => self.view@follow@ref\n";

    private const string Named = "struct E {a}\n    public let view: uniq/i32 during a\n\n    public init(view: uniq/i32 during a) => self.view = view\n\n" +
        "    public func get(self: ref/Self) -> ref/i32 during self.a => self.view@follow@ref\n";

    private const string Free = "func get(h: ref/H) -> ref/i32 during h => h.item\n";

    [Theory]
    [InlineData(Holder + "var n = 7\nlet h = H.init(item: n@ref)\nlet r = h.get()\nn = 9\nrequire r == 7 else => $abort(\"aliased\")")]
    [InlineData(Holder + Free + "var n = 7\nlet h = H.init(item: n@ref)\nlet r = get(h@ref)\nn = 9\nrequire r == 7 else => $abort(\"aliased\")")]
    [InlineData(Holder + "var n = 7\nlet h = H.init(item: n@ref)\nlet r = h.get()\nlet s = r\nn = 9\nrequire s == 7 else => $abort(\"aliased\")")]
    [InlineData(Exclusive + "var n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.take()\nn = 9\nr@follow = 1")]
    [InlineData(Exclusive + "var n = 7\nlet e = E.init(view: n@uniq)\nlet r = e.peek()\nn = 9\nrequire r == 7 else => $abort(\"aliased\")")]
    [InlineData(Exclusive + "var n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.take()\nlet read = n\nr@follow = 1")]
    public void TheResultKeepsTheLoansHeldInsideItsInput(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, issue.Failure);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("AfterLastUse", Holder + "var n = 7\nlet h = H.init(item: n@ref)\nlet r = h.get()\nrequire r == 7 else => $abort(\"read\")\nn = 9\nrequire n == 9 else => $abort(\"write\")")]
    [InlineData("Free", Holder + Free + "let n = 7\nlet h = H.init(item: n@ref)\nlet r = get(h@ref)\nrequire r == 7 else => $abort(\"read\")")]
    [InlineData("Exclusive", Exclusive + "var n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.take()\nr@follow = 1\nn = n + 1\nrequire n == 2 else => $abort(\"write\")")]
    [InlineData("Follow", Exclusive + "var n = 7\nlet e = E.init(view: n@uniq)\nlet r = e.peek()\nrequire r == 7 else => $abort(\"read\")\nn = 9\nrequire n == 9 else => $abort(\"write\")")]
    [InlineData("NamedSlot", Named + "var n = 7\nlet e = E.init(view: n@uniq)\nlet r = e.get()\nrequire r == 7 else => $abort(\"read\")\nn = 9\nrequire n == 9 else => $abort(\"write\")")]
    public void ResultsMayEndBeforeTheInputsLoansAreReleased(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("BorrowedInputResult" + name, source, string.Empty);
    }

    [Fact]
    public void APartBorrowBesideAStoredReferenceStaysPrecise()
    {
        // SPEC 15.6.2: without a call result over the root, a borrow of the Scalar part does not hold the stored reference's Loan.
        var c = MinimalEmissionTest.Analyze("var n = 7\nlet item = (n@ref, 5)\nlet x = item.1@ref\nn = 9\nrequire x == 5 else => $abort(\"part\")");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOwnershipOverBorrowedInputResultsAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Holder + Free + "var n = 7\nlet h = H.init(item: n@ref)\nlet r = h.get()\nlet s = get(h@ref)\nrequire r == 7 and s == 7 else => $abort(\"read\")\nn = 9");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
