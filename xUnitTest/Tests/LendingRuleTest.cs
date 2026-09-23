// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.5, 13.5.3 and 15.1.5: a bare Place is never Moved or lent exclusively; transfers and exclusive borrows are spelled.</summary>
public class LendingRuleTest
{
    private const string Token = "struct Token\n    var id: i32 = 0\n    deinit => Console.writeLine(\"destroy\")\n";

    [Fact]
    public void TryTransfersItsExtractedPayloads()
        => ScalarEmissionTest.EmitFixture(
            "LendingRuleTry",
            Token + "func source() -> Token? => .Some(Token.init())\nfunc fail() -> Result<Token, string> => .Err(\"error\")\nfunc run() -> ()?\n    let token = try source()\n    Console.writeLine(\"after\")\n    return .Some(())\nfunc run2() -> Result<Token, string>\n    let value = try fail()\n    return .Ok(value@move)\n_ = run()\nmatch run2()\n    .Ok(_) => Console.writeLine(\"ok\")\n    .Err(let message) => Console.writeLine(message)",
            "after\ndestroy\nerror\n");

    [Theory]
    [InlineData("Ref", "let text = \"a\"\nConsole.writeLine(text)\nConsole.writeLine(text@ref)\nConsole.writeLine(text)", "a\na\na\n")]
    [InlineData("Move", "let text = \"a\"\nConsole.writeLine(text)\nConsole.writeLine(text@move)", "a\na\n")]
    [InlineData("Chained", "let text = \"ok\"\nConsole.writeLine(text@((owner))@((string)))", "ok\n")]
    public void StringArgumentsBorrowThroughEverySpelling(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("LendingRuleString" + name, source, stdout);

    [Fact]
    public void SharedIterationKeepsTheArrayUsable()
        => ScalarEmissionTest.EmitFixture(
            "LendingRuleSharedIteration",
            "let values: [2 of i32] = [1, 2]\nvar total = 0\nfor value in values\n    total += value\nrequire total == 3 and values[0] == 1 else => $abort(\"iteration\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // PLAN G13: moving the root of a live Loan is reported once, at the Move, not at every later use of the Loan.
    [Fact]
    public void MovingALentRootIsReportedOnce()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public var value: i32 = 3\n    deinit => ()\nfunc borrow(item: ref/Item) -> ref/Item during item => item\nfunc take(item: Item) => ()\nvar item = Item.init()\nlet view = borrow(item@ref)\ntake(item@move)\nlet a = view.value\nlet b = view.value\nlet c = view.value\nrequire a + b + c == 9 else => $abort(\"sum\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, issue.Failure);
        Assert.Equal("item@move", issue.Source.Parent?.ToString()); // Reported at the Move, not at the reads of view.
    }

    [Theory]
    [InlineData("var values: [2 of i32] = [1, 2]\nfor value in values\n    values = [3, 4]", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("let number = 1\nlet taken = number@move\nlet again = number", OwnershipFailure.PossiblyMovedUse)]
    public void OwnershipRejectsConflictingAndMovedPlaces(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
    }

    [Theory]
    [InlineData("func f(x: string) => match x\n    let s => Console.writeLine(s)", DiagnosticCode.UnsupportedBinding_Kd)]
    [InlineData("let text = \"a\"\nlet view = text@ref", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("func take(text: string) => ()\nlet text = \"a\"\ntake(text)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("func bump(n: uniq/i32) => ()\nvar n = 1\nbump(n)", DiagnosticCode.ExclusiveBorrowRequired_Kd)]
    public void BindingNamesTheRequiredSpellingOrBoundary(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
