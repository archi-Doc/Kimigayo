// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
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

    // A direct exclusive borrow placed into a literal is the same case without a Move, its acceptance verified by analysis.
    [Theory]
    [InlineData("var n: i32 = 1\nvar p = n@uniq\nvar pair = (p@uniq, 1)\n_ = pair.0@follow\np@follow = 2")]
    [InlineData("var n: i32 = 1\nvar m: i32 = 3\nvar p = n@uniq\nlet c = p\nlet items: [2 of uniq/i32] = [c@move, m@uniq]\n_ = items[0]\np@follow = 2")]
    public void AParentIsUsableAfterItsPlacedChildsLastUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // The write through the parent meets the value that still holds the moved child's Loan, at the write, not at the call. Since the MIR
    // decides acceptance (H5, DECISIONS R5-M185) the record is A5's Loan conflict at the write's statement (H3b T44: at `p@follow`
    // before), relating the Reborrow `let c = p` and the holder's later use: the holder's next use in the access's chain is the witness
    // (DIAGNOSTICS A6's `Use`; DECISIONS R4-M57 and its U-T2 amendment: the declaration only where that use lies past the chain).
    [Theory]
    [InlineData(Keep + Child + "let k = keep(c@move)\np@follow = 2\n_ = k@follow", "p@follow = 2", "borrow 4:9 p | loan 7:5 k@follow")]
    [InlineData(Keep2 + Child + "let k = keep2(c@move)\np@follow = 2\n_ = k@follow", "p@follow = 2", "borrow 4:9 p | loan 7:5 k@follow")]
    [InlineData(Child + "var pair = (c@move, 1)\np@follow = 2\n_ = pair.0@follow", "p@follow = 2", "borrow 3:9 p | loan 6:5 pair.0@follow")]
    [InlineData(Keep + "func g<T>(p: uniq/T, x: T) -> ()\n    T is Copy\n    let c = p\n    let k = keep(c@move)\n    p@follow = x\n    _ = k@follow\nvar n: i32 = 1\ng(n@uniq, 2)", "p@follow = x", "borrow 4:13 p | loan 7:9 k@follow")]
    public void AParentUsedWhileTheMovedChildsLoanIsHeldIsRejectedAtTheUse(string source, string write, string related)
        => CallReservationTest.AssertMirRecord(source, DiagnosticCode.ComparisonLoanConflict_Kd, write, related);

    // Moving the child while its own Reborrow child is live is a Loan conflict at the moved operand (H5; H3b T46: the call activation's
    // conflict before), relating the Reborrow `let d = c` and its later use.
    [Fact]
    public void MovingAChildWhileItsOwnChildIsLiveIsRejected()
        => CallReservationTest.AssertMirRecord(Take + Child + "let d = c\ntake(c@move)\n_ = d@follow", DiagnosticCode.ComparisonLoanConflict_Kd, "c", "borrow 5:9 c | loan 7:5 d@follow", "c@move)");

    // SPEC 15.6.3, 15.6.7 (PLAN G53): a parent placed into a literal while its child is live there, whichever is placed first. The MIR's
    // record (H5) is a Loan conflict at the placed parent `p` (H3b T46: PlacementActivationConflict_Kd before); it relates the Reborrow
    // `let c = p` and the literal, or the placed child, that holds the child's Loan, with no Note: the MIR reads the placed parent as an
    // ordinary access (H3b T46), and A6 has no acquisition fact for the placement Note ("acquired exclusively where the literal places
    // it"; PLAN G433).
    [Theory]
    [InlineData("var pair = (c@move, p@move)\n_ = pair.0@follow", "borrow 3:9 p | loan 4:12 (c@move, p@move)")]
    [InlineData("var pair = (p@move, c@move)\n_ = pair.1@follow", "borrow 3:9 p | loan 4:21 c")]
    [InlineData("let items: [2 of uniq/i32] = [c@move, p@move]\n_ = items[0]", "borrow 3:9 p | loan 4:30 [c@move, p@move]")]
    public void AParentPlacedWhileItsChildIsLiveInTheLiteralIsRejected(string body, string related)
        => CallReservationTest.AssertMirRecord(Child + body, DiagnosticCode.ComparisonLoanConflict_Kd, "p", related, "p@move");

    // A placed child holds its Loan until the literal completes: the parent is neither borrowed exclusively again nor read. The record
    // (H5) relates the placed child's Borrow and the literal; the read's is at the literal (H3b T44: at `p@follow` before), which is then
    // the value retaining the Loan (DIAGNOSTICS A6's `Store` witness).
    [Theory]
    [InlineData("let q = (p@uniq, p@uniq)\n_ = q.0@follow", "p@uniq", "p@uniq)", "borrow 3:10 p@uniq | loan 3:9 (p@uniq, p@uniq)")]
    [InlineData("let q = (p@uniq, p@follow)\n_ = q.0@follow", "(p@uniq, p@follow)", null, "borrow 3:10 p@uniq | loan 3:9 (p@uniq, p@follow)" + CallReservationTest.Retaining)]
    public void AParentUsedWhileItsPlacedChildIsLiveInTheLiteralIsRejected(string body, string text, string? at, string related)
        => CallReservationTest.AssertMirRecord("var n: i32 = 1\nvar p = n@uniq\n" + body, DiagnosticCode.ComparisonLoanConflict_Kd, text, related, at);
}
