// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementReplacementEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Tuple", "var a = (\"old\", 42)\na.0 = \"new\"\nif a.1 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "Array", "var a: [2 of string] = [\"old\", \"sibling\"]\nvar i: isize = 0\na[i] = \"new\"\nConsole.writeLine(\"ok\")", "old=1;sibling=1;new=1;ok=1" },
        { "Nested", "var a: (string, [1 of (string, i32)]) = (\"sibling\", [(\"old\", 0)])\na.1[0] = (\"new\", 42)\nif a.1[0].1 == 42 => Console.writeLine(\"ok\")", "sibling=1;old=1;new=1;ok=1" },
        { "ArrayValue", "var a: ([2 of string], i32) = ([\"first\", \"last\"], 42)\na.0 = [\"newFirst\", \"newLast\"]\nif a.1 == 42 => Console.writeLine(\"ok\")", "first=1;last=1;newFirst=1;newLast=1;ok=1" },
        { "Move", "var a = (\"old\", 42)\nlet text = \"new\"\na.0 = text\nlet moved = a\nConsole.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "AggregateMove", "var a = ((\"old\", 0), 1)\nlet value = (\"new\", 42)\na.0 = value\nif a.0.1 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "Call", "func make() -> string => \"new\"\nvar a = (\"old\", 42)\na.0 = make()\nConsole.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "AggregateCall", "func make() -> (string, i32) => (\"new\", 42)\nvar a = ((\"old\", 0), 1)\na.0 = make()\nif a.0.1 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "Selection", "var a = (\"old\", 42)\na.0 = if true => \"new\" else => \"unused\"\nConsole.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "Match", "var a = ((\"old\", 0), 1)\na.0 = match true\n    true => (\"new\", 42)\n    false => (\"unused\", 0)\nif a.0.1 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "ResultCleanup", "var a = (\"old\", 42)\na.0 = work: do\n    defer => Console.writeLine(\"ok\")\n    exit to work: \"new\"", "old=1;new=1;ok=1" },
        { "Loop", "var a: [1 of string] = [\"old\"]\nvar i = 0\nloop\n    defer => a[0] = \"new\"\n    i += 1\n    if i < 3 => continue\n    exit\nConsole.writeLine(\"ok\")", "old=1;new=3;ok=1" },
        { "Sibling", "var a = (40, \"old\")\na.0 += work: do\n    a.1 = \"new\"\n    exit to work: 2\nif a.0 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "SiblingAggregate", "var a = (40, (\"old\", 0))\na.0 += work: do\n    a.1 = (\"new\", 2)\n    exit to work: a.1.1\nif a.0 == 42 => Console.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "ZeroSize", "var a: ([0 of string], i32) = ([], 42)\nlet empty: [0 of string] = []\na.0 = empty\nif a.1 == 42 => Console.writeLine(\"ok\")", "ok=1" },
        { "EmptyString", "var a = (\"old\", 42)\na.0 = \"\"\nConsole.writeLine(\"ok\")", "old=1;=1;ok=1" },
        { "ConditionalSource", "func f(replace?: bool)\n    var a = (\"old\", 42)\n    let text = \"new\"\n    if replace => a.0 = text\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "old=2;new=2;ok=1" },
        { "ReinitializeSource", "var a: [1 of string] = [\"old\"]\nvar text = \"new\"\na[(work: do\n    text = \"again\"\n    exit to work: 0\n)] = text\nConsole.writeLine(\"ok\")", "old=1;new=1;again=1;ok=1" },
        { "RestoreParent", "var a = (\"old\", 0)\na.0 = work: do\n    a = (\"intermediate\", 42)\n    exit to work: \"new\"\nif a.1 == 42 => Console.writeLine(\"ok\")", "old=1;intermediate=1;new=1;ok=1" },
        { "CoveredArm", "var a = (\"old\", 0)\nmatch true\n    _ => a.0 = \"new\"\n    true => a.0 = \"unused\"\nConsole.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "StringMatch", "var a = (\"old\", 0)\na.0 = match true\n    true => \"new\"\n    false => \"unused\"\nConsole.writeLine(\"ok\")", "old=1;new=1;ok=1" },
        { "CopyMatch", "var a = ((0, 0), 1)\na.0 = match true\n    true => (40, 2)\n    false => (0, 0)\nif a.0.0 + a.0.1 == 42 => Console.writeLine(\"ok\")", "ok=1" },
        { "Dead", "func f()\n    return\n    var a = (\"old\", 0)\n    a.0 = \"new\"\nf()\nConsole.writeLine(\"ok\")", "ok=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CompleteOwnedElementsCanBeReplaced(string name, string source, string destructions)
    {
        var fixture = "ElementReplacement" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, "ok\n");
        StringEmissionTest.WriteAuditedFixture(fixture, source, ir, "ok\n", destructions);
    }

    [Fact]
    public void DestructionOrderSeparatesOldAndNewResponsibility()
    {
        const string Source = "var a = (\"sibling\", (\"first\", \"last\"))\na.1 = (\"newFirst\", \"newLast\")\nConsole.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementReplacementOrder", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementReplacementOrder", Source, ir, "ok\n", "sibling=1;first=1;last=1;newFirst=1;newLast=1;ok=1", order: [2, 1, 5, 4, 3, 0]);
    }

    [Fact]
    public void RightSidePrecedesEveryIndexAndOldDestruction()
    {
        const string Source = "func make() -> string\n    Console.writeLine(\"rhs\")\n    return \"new\"\nfunc outer() -> isize\n    Console.writeLine(\"outer\")\n    return 0\nfunc inner() -> isize\n    Console.writeLine(\"inner\")\n    return 0\nvar a: [1 of [1 of string]] = [[\"old\"]]\na[outer()][inner()] = make()\nConsole.writeLine(\"ok\")";
        const string Output = "rhs\nouter\ninner\nok\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementReplacementEvaluation", Source, Output);
        StringEmissionTest.WriteAuditedFixture("ElementReplacementEvaluation", Source, ir, Output, "rhs=1;outer=1;inner=1;old=1;new=1;ok=1", order: [0, 1, 2, 3, 5, 4]);
    }

    [Theory]
    [InlineData("String", "string", "\"old\"", "\"new\"")]
    [InlineData("Aggregate", "(string, i32)", "(\"old\", 0)", "(\"new\", 42)")]
    public void IndexTransferCleansSecuredInputWithoutReplacingOldValue(string name, string type, string initial, string value)
    {
        var source = $"func f() -> i32\n    var a: [1 of {type}] = [{initial}]\n    defer => Console.writeLine(\"cleanup\")\n    a[(return 42)] = {value}\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")";
        const string Output = "cleanup\nok\n";
        var fixture = "ElementReplacementTransfer" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, Output);
        StringEmissionTest.WriteAuditedFixture(fixture, source, ir, Output, "old=1;new=1;cleanup=1;ok=1", order: [1, 2, 0, 3]);
    }

    [Fact]
    public void BoundsAbortKeepsOldValueAndSkipsCleanup()
    {
        const string Source = "var a: [1 of string] = [\"old\"]\ndefer => Console.writeLine(\"bad\")\na[1] = \"new\"";
        const string Error = "Hello.kimi:3:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementReplacementBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementReplacementBounds", Source, ir, string.Empty, "old=0;new=0;bad=0", 1, Error);
    }

    [Fact]
    public void RightSideTransferDoesNotLocateDestination()
    {
        const string Source = "func index() -> isize\n    Console.writeLine(\"bad\")\n    return 0\nfunc f() -> i32\n    var a: [1 of string] = [\"old\"]\n    a[index()] = (return 42)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementReplacementRhsTransfer", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementReplacementRhsTransfer", Source, ir, "ok\n", "old=1;ok=1", order: [0, 1]);
    }

    [Theory]
    [InlineData("Rhs", "a[0] = work: do\n    defer => loop => ()\n    exit to work: \"new\"")]
    [InlineData("Index", "a[(work: do\n    defer => loop => ()\n    exit to work: 0\n)] = \"new\"")]
    public void NonterminatingCleanupPreventsDestructionAndPlacement(string name, string expression)
    {
        var source = "var a: [1 of string] = [\"old\"]\n" + expression;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var function = module.GetFunction(0);
        Assert.DoesNotContain(function.Instructions, x => x.Callee == WindowsLowering.DestroyString || x.Opcode == EmissionOpcode.DestroyAggregate);
        Assert.DoesNotContain(c.Ownership.Bodies[0].Operations, x => x.Kind == OwnershipOperationKind.WriteElement);
        ScalarEmissionTest.EmitFixture("ElementReplacementDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);
    }

    [Theory]
    [InlineData("var a = (\"old\", 0)\nlet text = \"new\"\na.0 = text\nConsole.writeLine(text)")]
    [InlineData("var a: [1 of string]\na[0] = \"new\"")]
    [InlineData("let a = (\"old\", 0)\na.0 = \"new\"")]
    [InlineData("var a = (\"old\", 0)\nlet moved = a\na.0 = \"new\"")]
    [InlineData("var a = (40, \"old\")\na.0 += work: do\n    a = (0, \"new\")\n    exit to work: 2")]
    [InlineData("func f(a?: [1 of string])\n    a[0] = \"new\"")]
    [InlineData("func make() -> [1 of string] => [\"old\"]\nmake()[0] = \"new\"")]
    [InlineData("var a: [1 of string] = [\"old\"]\nlet text = \"new\"\na[(work: do\n    Console.writeLine(text)\n    exit to work: 0\n)] = text")]
    [InlineData("func f()\n    return\n    var a = (\"old\", 0)\n    let text = \"new\"\n    a.0 = text\n    Console.writeLine(text)")]
    public void InvalidOrUnsupportedReplacementProducesNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("var a: [1 of string] = [\"old\"]\na[(work: do\n    a[0] = \"bad\"\n    exit to work: 0\n)] = \"new\"")]
    [InlineData("var a = ((\"old\", 40), 1)\na.0.1 += work: do\n    a.0 = (\"new\", 0)\n    exit to work: 2")]
    [InlineData("var a: [2 of (i32, string)] = [(40, \"old\"), (0, \"other\")]\nvar i: isize = 0\nvar j: isize = 1\na[i].0 += work: do\n    a[j].1 = \"new\"\n    exit to work: 2")]
    public void OverlappingReplacementIsRejectedByLoanChecking(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void WriteHoldsExclusiveAuthorityAndTransfersOnlyInputResponsibility()
    {
        var c = MinimalEmissionTest.Analyze("var a = (\"old\", 0)\na.0 = \"new\"");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        var input = body.Operations[plan.Write].Input;
        Assert.Equal(plan.Loan, body.LoanInputs[plan.Operation]);
        Assert.Equal(plan.Exclusive, body.LoanStates[plan.Operation]);
        Assert.True(body.HasComparisonLoan(plan.Write, plan.Exclusive));
        Assert.Equal(body.ComparisonLoans[plan.Loan].Parent, body.LoanStates[plan.Write + 1]);
        Assert.True((body.GetInputState(plan.Write + 1, plan.Root) & PlaceState.MustInit) != 0);
        Assert.True((body.GetInputState(plan.Write + 1, input) & PlaceState.MayInit) == 0);
        var instructions = module.GetFunction(0).Instructions.Where(x => x.Operation == plan.Write).ToArray();
        Assert.Equal(WindowsLowering.DestroyString, instructions[0].Callee);
        Assert.Equal(EmissionOpcode.MoveString, instructions[1].Opcode);
        Assert.Empty(module.GetFunction(0).LiveFlags);
    }

    [Theory]
    [InlineData("exclusive")]
    [InlineData("mode")]
    [InlineData("release")]
    [InlineData("input")]
    [InlineData("type")]
    [InlineData("write")]
    [InlineData("root")]
    [InlineData("output")]
    public void CorruptedReplacementPlansFailBeforeOutputAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var a = ((\"old\", 0), 1)\na.0 = (\"new\", 42)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        var write = body.Operations[plan.Write];
        switch (defect)
        {
            case "exclusive": body.Projections[0] = plan with { Exclusive = -1 }; break;
            case "mode": body.ComparisonLoans[plan.Exclusive] = body.ComparisonLoans[plan.Exclusive] with { Mode = LoanRequirement.Ref }; break;
            case "release": body.LoanStates[plan.Write + 1] = plan.Exclusive; break;
            case "input": body.OperationStorage[plan.Write] = write with { Input = plan.Root }; break;
            case "type": body.PlaceStorage[write.Input] = body.Places[write.Input] with { Type = BoundType.String }; break;
            case "write": body.Projections[0] = plan with { Write = -1 }; break;
            case "root": body.PlaceStorage[plan.Root] = body.Places[plan.Root] with { Mutable = false }; break;
            case "output": body.Projections[0] = plan with { Output = plan.Write }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmReplacementAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "func make() -> (string, i32) => (\"new\", 42)\nvar a = ((\"old\", 0), 1)\nvar i = 0\nloop\n    defer => a.0 = if true => make() else => (\"other\", 1)\n    i += 1\n    if i < 3 => continue\n    exit\na.0.0 = \"last\"";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var success = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
