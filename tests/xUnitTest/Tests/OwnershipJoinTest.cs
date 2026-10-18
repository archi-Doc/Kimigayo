// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class OwnershipJoinTest
{
    private const string Pair = "struct Cell\n    public var value: i32 = 7\nfunc inspect(c: bool)\n    var a = Cell.init()\n    var b = Cell.init()\n";

    [Fact]
    public void Milestone15()
    {
        var c = MinimalEmissionTest.Analyze(Source());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("    else\n        current = Item.init(12)\n", "", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("            current = Item.init(20)", "            // current = Item.init(20)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("            consume(current@move, initial + 1)", "            consume(current@move, initial + 1)\n            let invalid = current.value", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("        current.value = current.value + 1", "        current.value = current.value + 1\n        consume(current@move, initial + 1)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("            total = total + selected.value", "            current.value = 99\n            total = total + selected.value", DiagnosticCode.ComparisonLoanConflict_Kd)]
    public void RejectsInvalidJoin(string before, string after, DiagnosticCode failure)
    {
        var source = Source();
        Assert.Contains(before, source, StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source.Replace(before, after, StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, issue => issue.Code == failure);
        Assert.False(c.Validate(out _));
    }

    // SPEC 14.9.2, 15.6 (F1): a joined borrow's Type names both Origins and its value carries both Loans, so a write of either owner while
    // it is live conflicts, on the MIR (A5) as on the old path, which decides until R6-check.
    [Theory]
    [InlineData("if c => a@ref else => b@ref")]
    [InlineData("if c => b@ref else => a@ref")]
    [InlineData("if c\n        yield a@ref\n    else => b@ref")]
    [InlineData("match c\n        true => a@ref\n        false => b@ref")]
    [InlineData("label choose: do\n        if c => exit to choose a@ref\n        exit to choose b@ref")]
    [InlineData("if c => (if c => a@ref else => b@ref) else => a@ref")]
    public void BorrowResultsRetainBothDependencies(string expression)
    {
        foreach (var annotation in new[] { string.Empty, ": ref/Cell" })
        {
            var source = Pair + "    let selected" + annotation + " = " + expression + "\n    let n = selected.value\n    a.value = n\n    b.value = n\ninspect(true)\ninspect(false)";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var program = c.BuildMir();
            var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "inspect");
            var selected = Assert.Single(body.Locals.ToArray(), static x => x.Name == "selected");
            Assert.Equal(OriginKind.Intersection, selected.Type.Origin!.Kind);
            Assert.Equal(2, selected.Type.Origin.Operands.Count);
            Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Code.IsLoanConflict());
            foreach (var owner in new[] { "a", "b" })
            {
                var invalid = MinimalEmissionTest.Analyze(source.Replace("    let n =", "    " + owner + ".value = 99\n    let n =", StringComparison.Ordinal));
                Assert.True(invalid.Binding.Result.IsComplete);
                Assert.Contains(invalid.Problems, issue => issue.Code.IsLoanConflict());
                Assert.Contains(invalid.BuildMir().Problems.All.ToArray(), static x => x.Code.IsLoanConflict());
                Assert.False(invalid.Validate(out _));
            }
        }
    }

    [Fact]
    public void JoinedBorrowCannotOutliveEitherLocalSource()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected = label choose: do\n        let local = Cell.init()\n        exit to choose if c => a@ref else => local@ref\n    let n = selected.value\ninspect(true)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, issue => issue.Code.IsLoanConflict());

        // SPEC 15.6.5, 16.2.2: one record, at the exit whose cleanup destroys `local`, never also at the join or the later use.
        var conflict = Assert.Single(c.Ownership.Issues);
        Assert.Equal((OwnershipRequirement.ComparisonLoanConflict, "local"), (conflict.Failure, conflict.Destroyed));
        Assert.IsType<Kimi.Compiler.Parsing.ExitKoto>(conflict.Source);
        Assert.False(c.Validate(out _));
    }

    [Theory]
    [InlineData("ref/Cell during static", "a@ref", "b@ref")]
    [InlineData("ref/Cell", "a@ref", "b@uniq")]
    [InlineData("ref/Cell", "a@ref", "true")]
    public void BorrowJoinCannotChangeItsContract(string annotation, string first, string second)
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected: " + annotation + " = if c => " + first + " else => " + second + "\ninspect(true)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Validate(out _));
    }

    // SPEC 14.9.2, 15.6.1: a join of a body-local Borrow with a fixed source waits for every source and takes their meet, so
    // neither is refitted to the other's Origin; before, the fixed source alone was the expected Type and the Borrow was refuted.
    [Theory]
    [InlineData("IfLocalFirst", "let r = if c => n@ref else => z", "5\n")]
    [InlineData("IfLocalSecond", "let r = if c => z else => n@ref", "1\n")]
    [InlineData("Annotated", "let r: ref/i32 = if c => n@ref else => z", "5\n")]
    [InlineData("Match", "let r = match c\n        true => n@ref\n        false => z", "5\n")]
    [InlineData("Label", "let r = label pick: do\n        if c\n            exit to pick n@ref\n        exit to pick z", "5\n")]
    public void ALocalBorrowJoinsAFixedSourceThroughTheMeet(string name, string join, string stdout)
    {
        var source = "func pick(c: bool, z: ref/i32) -> i32\n    let n = 5\n    " + join + "\n    return r@follow\nlet z = 1\nConsole.writeLine(\"\\(pick(true, z@ref))\")";
        ScalarEmissionTest.EmitFixture("OwnershipJoinLocalFixed" + name, source, stdout);
        var invalid = MinimalEmissionTest.Analyze(source.Replace("    return r@follow", "    n = 6\n    return r@follow", StringComparison.Ordinal).Replace("    let n = 5", "    var n = 5", StringComparison.Ordinal));
        Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
        Assert.Contains(invalid.Problems, static issue => issue.Code.IsLoanConflict());
    }

    // SPEC 15.4.4, 15.6.5: the declaration owns one inferred slot; each assigned value retains its own Loan.
    [Fact]
    public void ReassigningABorrowLocalToAnotherBorrowRuns()
        => ScalarEmissionTest.EmitFixture("OwnershipJoinReassign", "let z = 0\nvar r = z@ref\nlet n = 5\nr = n@ref\nrequire r@follow == 5 else => $abort(\"r\")", string.Empty);

    [Fact]
    public void CheckedValuesRejectLostOriginDependency()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected = if c => a@ref else => b@ref\n    let n = selected.value\ninspect(true)");
        Assert.True(c.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = Assert.Single(c.Ownership.Bodies, b => b.Function.Name == "inspect");
        var phi = body.Values.FindIndex(value => value.Kind == OwnershipValueKind.Phi);
        var incoming = body.PhiInputs[body.Values[phi].Start].Value;
        var narrowed = body.Places[body.Operations[incoming].Input].Type;
        var place = body.Operations[phi].Place;
        body.PlaceStorage[place] = body.Places[place] with { Type = narrowed };
        body.Operations[phi].Source.SetType(narrowed);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    private static string Source() => Repository.ReadText("tests", "milestones", "Milestone15.kimi");
}
