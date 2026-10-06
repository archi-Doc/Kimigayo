// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 15.6.3, 15.5, 3.3.6: a Reborrow through an exclusive reference stored in a Place is contained in the Origin of each safe
// borrow through which a function reaches it, up to and including the first shared layer, so a published result through a
// receiver or parameter stays within that borrow unless `preserves results` makes it independent.
public class StoredReferenceResultBoundTest
{
    private const string View = "struct E {a}\n    public var view: uniq/i32 during a\n\n    public init(view: uniq/i32 during a) => self.view = view\n\n" +
        "    public func bump(self: uniq/Self) -> () => self.view@follow += 1\n";

    [Theory]
    [InlineData(View + "    public func get(self: ref/Self) -> ref/i32 during self.a => self.view@follow@ref\n")]
    [InlineData(View + "    public func step(self: uniq/Self) -> ref/i32 during self.a\n        self.view@follow += 1\n        return self.view@follow@ref\n")]
    [InlineData(View + "    public func bad(self: uniq/Self) -> uniq/i32 during self.a => self.view\n")]
    [InlineData("func get(h: ref/(uniq/i32 during a)) -> ref/i32 during a => h@follow@follow@ref\n")]
    public void AResultDetachedFromTheReachingBorrowIsRejected(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("Shared", View + "    public func get(self: ref/Self) -> ref/i32 during self => self.view@follow@ref\nvar n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.get()\nrequire r == 7 else => $abort(\"r\")\ne.bump()")]
    [InlineData("Exclusive", View + "    public func take(self: uniq/Self) -> uniq/i32 during self => self.view\nvar n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.take()\nr@follow += 1\ne.bump()\nrequire n == 9 else => $abort(\"n\")")]
    public void AResultBoundedByItsReceiverRuns(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("StoredReferenceResult" + name, source, string.Empty);
    }

    [Fact]
    public void TheReceiverStaysLentWhileTheResultLives()
    {
        var c = MinimalEmissionTest.Analyze(View + "    public func get(self: ref/Self) -> ref/i32 during self => self.view@follow@ref\nvar n = 7\nvar e = E.init(view: n@uniq)\nlet r = e.get()\ne.bump()\nrequire r == 7 else => $abort(\"aliased\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }
}
