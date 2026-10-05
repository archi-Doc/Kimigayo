// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 15.6.3, 15.6.7 (G75 E5): `r@move` transfers the reference value itself, so a Reborrow child moved into a reserved
// call argument or a placed element carries its Loan into the callee or the literal as its parent's descendant, not as a
// sibling Loan on the same root. The parent is usable again after the moved value's last use and conflicts only with a value
// that still holds the child's Loan; a parent placed into a literal while its child is live there remains rejected.
public sealed class ChildMoveActivationTest
{
    private const string Take = "func take(r: uniq/i32) -> () => ()\n";
    private const string Keep = "func keep<F>(f: F) -> F => f@move\n";
    private const string Keep2 = "func keep2(r: uniq/i32) -> uniq/i32 => r@move\n";
    private const string Child = "var n: i32 = 1\nvar p = n@uniq\nlet c = p\n";
    private const string Done = "p@follow = 2\nrequire n == 2 else => $abort(\"parent\")";

    [Theory]
    [InlineData("Call", Take + Child + "take(c@move)\n" + Done)]
    [InlineData("CallWithoutLaterParent", Take + Child + "take(c@move)\nrequire n == 1 else => $abort(\"parent\")")]
    [InlineData("GenericKeep", Keep + Child + "let k = keep(c@move)\nk@follow = 5\n" + Done)]
    [InlineData("ConcreteKeep", Keep2 + Child + "let k = keep2(c@move)\nk@follow = 5\n" + Done)]
    [InlineData("Placement", Child + "var pair = (c@move, 1)\n_ = pair.0@follow\n" + Done)]
    [InlineData("ImmutablePlacement", Child + "let pair = (c@move, 1)\n_ = pair.0@follow\n" + Done)]
    [InlineData("ChainedMove", Take + Child + "let d = c@move\ntake(d@move)\n" + Done)]
    [InlineData("Parameter", Take + "func g(p: uniq/i32, x: i32) -> ()\n    let c = p\n    take(c@move)\n    p@follow = x\nvar n: i32 = 1\ng(n@uniq, 2)\nrequire n == 2 else => $abort(\"parameter\")")]
    [InlineData("GenericOrigin", Keep + "func g<T>(p: uniq/T during a, x: T) -> ()\n    T is Copy\n    let c = p\n    let k = keep(c@move)\n    _ = k@follow\n    p@follow = x\nvar n: i32 = 1\ng(n@uniq, 2)\nrequire n == 2 else => $abort(\"origin\")")]
    [InlineData("Generic", Keep + "func g<T>(p: uniq/T, x: T) -> ()\n    T is Copy\n    let c = p\n    let k = keep(c@move)\n    _ = k@follow\n    p@follow = x\nvar n: i32 = 1\ng(n@uniq, 2)\nrequire n == 2 else => $abort(\"generic\")")]
    public void AParentIsUsableAfterItsMovedChildsLastUse(string name, string source)
        => ScalarEmissionTest.EmitFixture("ChildMove" + name, source, string.Empty);

    // A direct exclusive borrow placed into a literal is the same case without a Move; reading it back through the slot
    // is not lowered yet, so the acceptance is verified by analysis alone.
    [Theory]
    [InlineData("var n: i32 = 1\nvar p = n@uniq\nvar pair = (p@uniq, 1)\n_ = pair.0@follow\np@follow = 2")]
    [InlineData("var n: i32 = 1\nvar m: i32 = 3\nvar p = n@uniq\nlet c = p\nlet items: [2 of uniq/i32] = [c@move, m@uniq]\n_ = items[0]\np@follow = 2")]
    public void AParentIsUsableAfterItsPlacedChildsLastUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // The write through the parent meets the value that still holds the moved child's Loan, at the write, not at the call.
    [Theory]
    [InlineData(Keep + Child + "let k = keep(c@move)\np@follow = 2\n_ = k@follow", "let k = keep(c@move)")]
    [InlineData(Keep2 + Child + "let k = keep2(c@move)\np@follow = 2\n_ = k@follow", "let k = keep2(c@move)")]
    [InlineData(Child + "var pair = (c@move, 1)\np@follow = 2\n_ = pair.0@follow", "var pair = (c@move, 1)")]
    [InlineData(Keep + "func g<T>(p: uniq/T, x: T) -> ()\n    T is Copy\n    let c = p\n    let k = keep(c@move)\n    p@follow = x\n    _ = k@follow\nvar n: i32 = 1\ng(n@uniq, 2)", "let k = keep(c@move)")]
    public void AParentUsedWhileTheMovedChildsLoanIsHeldIsRejectedAtTheUse(string source, string holder)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, issue.Failure);
        Assert.False(issue.Activation);
        Assert.Equal(source.IndexOf("p@follow", StringComparison.Ordinal), issue.Source.Span.Start);
        Assert.Equal(holder, issue.LoanSource!.ToString());
    }

    // Moving the child while its own Reborrow child is live is the call activation's conflict.
    [Fact]
    public void MovingAChildWhileItsOwnChildIsLiveIsRejected()
    {
        var c = MinimalEmissionTest.Analyze(Take + Child + "let d = c\ntake(c@move)\n_ = d@follow");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, issue.Failure);
        Assert.True(issue.Activation);
        Assert.Equal("let d = c", issue.LoanSource!.ToString());
    }

    // SPEC 15.6.3, 15.6.7 (PLAN G53): a parent placed into a literal while its child is live there, whichever is placed first.
    [Theory]
    [InlineData("var pair = (c@move, p@move)\n_ = pair.0@follow", "c@move")]
    [InlineData("var pair = (p@move, c@move)\n_ = pair.1@follow", "let c = p")]
    [InlineData("let items: [2 of uniq/i32] = [c@move, p@move]\n_ = items[0]", "c@move")]
    public void AParentPlacedWhileItsChildIsLiveInTheLiteralIsRejected(string body, string loan)
    {
        var c = MinimalEmissionTest.Analyze(Child + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(DiagnosticCode.PlacementActivationConflict_Kd, issue.Code);
        Assert.Equal(loan, issue.LoanSource!.ToString());
    }

    // A placed child holds its Loan until the literal completes: the parent is neither borrowed exclusively again nor read.
    [Theory]
    [InlineData("let q = (p@uniq, p@uniq)\n_ = q.0@follow")]
    [InlineData("let q = (p@uniq, p@follow)\n_ = q.0@follow")]
    public void AParentUsedWhileItsPlacedChildIsLiveInTheLiteralIsRejected(string body)
    {
        var c = MinimalEmissionTest.Analyze("var n: i32 = 1\nvar p = n@uniq\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, issue.Failure);
        Assert.False(issue.Activation);
        Assert.Equal("p@uniq", issue.LoanSource!.ToString());
    }

    [Fact]
    public void TheChildFirstPlacementNamesThePlacedChild()
    {
        const string Source = "public func main()\n    var n: i32 = 1\n    var p = n@uniq\n    let c = p\n    var pair = (c@move, p@move)\n    _ = pair.0@follow\n";
        var result = DiagnosticCorpus.Check(Source);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.PlacementActivationConflict_Kd), record.Code);
        Assert.Contains("`p@move` is acquired exclusively where the literal places it", record.Note, StringComparison.Ordinal);
        var retained = Assert.Single(record.Related!);
        Assert.Equal("loan", retained.Role);
        Assert.Equal(Source.IndexOf("c@move", StringComparison.Ordinal), retained.Span!.Value.Start);
    }
}
