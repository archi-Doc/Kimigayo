// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementBorrowEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "Tuple", "let a = (\"first\", 42)\nif a.0 == \"first\" and a.0 != \"last\" => writeLine(\"ok\")\nlet whole = a" },
        { "SameElement", "let a: [1 of string] = [\"first\"]\nif same(a[0], a[((0x0))]) => writeLine(\"ok\")\nlet whole = a" },
        { "BothElements", "let a = (\"first\", \"first\")\nif same(a.0, a.1) and a.0 == a.1 => writeLine(\"ok\")\nlet whole = a" },
        { "Nested", "let a: [1 of (string, [1 of string])] = [(\"first\", [\"first\"])]\nif same(a[0].0, a[0].1[0]) => writeLine(\"ok\")" },
        { "Partial", "let a = ((\"first\", \"last\"), \"sibling\")\nlet taken = a.0.1\nif same(a.0.0, a.0.0) => writeLine(\"ok\")" },
        { "Repair", "var a = (\"first\", \"last\")\nlet taken = a.0\na.0 = \"new\"\nif same(a.0, a.0) => writeLine(\"ok\")\nlet whole = a" },
        { "SiblingMove", "func inspect(left: ref/string, right: string) -> bool => left == left\nlet a = (\"first\", \"last\")\nif inspect(a.0, a.1) => writeLine(\"ok\")" },
        { "SiblingReplace", "var a = (\"first\", \"last\")\nif a.0 == (work: do\n    a.1 = \"new\"\n    exit to work: \"first\"\n) => writeLine(\"ok\")" },
        { "SiblingUpdate", "var a = (\"first\", 40)\na.1 += if a.0 == \"first\" => 2 else => 0\nif a.1 == 42 => writeLine(\"ok\")" },
        { "DynamicSibling", "func inspect(left: ref/string, ignored: ()) -> bool => left == left\nvar a: (string, [1 of string]) = (\"first\", [\"last\"])\nvar i: isize = 0\nif inspect(a.0, a.1[i] = \"new\") => writeLine(\"ok\")" },
        { "CallRelease", "var a = (\"first\", \"last\")\nlet equal = same(a.0, a.0)\na.0 = \"new\"\nlet whole = a\nif equal => writeLine(\"ok\")" },
        { "CompareRelease", "var a = (\"first\", \"last\")\nlet equal = a.0 == \"first\"\na.0 = \"new\"\nlet whole = a\nif equal => writeLine(\"ok\")" },
        { "NestedCall", "func identity(value: bool) -> bool => value\nlet a = (\"first\", \"last\")\nif identity(same(a.0, a.0)) and same(a.1, a.1) => writeLine(\"ok\")" },
        { "Named", "let a = (\"first\", \"first\")\nif same(right: a.1, left: a.0) => writeLine(\"ok\")" },
        { "MixedTemporary", "let a = (\"first\", 0)\nif same(a.0, (work: do\n    exit to work: \"first\"\n)) => writeLine(\"ok\")" },
        { "Loop", "var a = (\"first\", \"last\")\nvar i = 0\nloop\n    if not same(a.0, a.0) => exit\n    a.0 = \"new\"\n    i += 1\n    if i < 3 => continue\n    exit\nif i == 3 => writeLine(\"ok\")" },
        { "Defer", "func f()\n    let a = (\"first\", \"last\")\n    defer\n        if same(a.0, a.0) => writeLine(\"ok\")\n    let taken = a.1\nf()" },
        { "ConditionalPartial", "func f(take: bool) -> bool\n    let a = (\"first\", \"last\")\n    if take\n        let taken = a.1\n    return same(a.0, a.0)\nif f(true) and f(false) => writeLine(\"ok\")" },
        { "AggregateResult", "func inspect(a: ref/string) -> (string, i32) => (\"new\", 42)\nvar a = (\"first\", 0)\nlet result = inspect(a.0)\na.0 = \"last\"\nif result.1 == 42 => writeLine(\"ok\")" },
        { "Dead", "func f()\n    return\n    let a = (\"first\", 0)\n    let equal = same(a.0, a.0)\nf()\nwriteLine(\"ok\")" },
        { "Covered", "let a = (\"first\", 0)\nmatch true\n    _ => ()\n    true => (work: do\n        let equal = same(a.0, a.0)\n    )\nwriteLine(\"ok\")" },
        { "GuardCandidate", "let a = (\"first\", 0)\nmatch \"other\"\n    let s if same(a.0, s) => writeLine(\"bad\")\n    _ => writeLine(\"ok\")" },
        { "NestedGuardCandidate", "func three(a: ref/string, b: ref/string, c: ref/string) -> bool => b == c\nlet a = (\"first\", 0)\nmatch \"other\"\n    let s if three(a.0, s, \"other\") => writeLine(\"ok\")\n    _ => writeLine(\"bad\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SharedElementsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrow" + name, Same + source, "ok\n");

    [Theory]
    [InlineData("==", "first", true)]
    [InlineData("!=", "last", true)]
    [InlineData("<", "last", true)]
    [InlineData("<=", "first", true)]
    [InlineData(">", "aaa", true)]
    [InlineData(">=", "first", true)]
    [InlineData("==", "last", false)]
    [InlineData("!=", "first", false)]
    [InlineData("<", "first", false)]
    [InlineData("<=", "aaa", false)]
    [InlineData(">", "first", false)]
    [InlineData(">=", "last", false)]
    public void AllComparisonsInspectTheHandle(string op, string right, bool expected)
    {
        var source = $"let a = (\"first\", \"{right}\")\nif (a.0 {op} a.1) == {(expected ? "true" : "false")} => writeLine(\"ok\")\nlet whole = a";
        ScalarEmissionTest.EmitFixture("ElementBorrowCompare" + Array.IndexOf(new[] { "==", "!=", "<", "<=", ">", ">=" }, op) + expected, source, "ok\n");
    }

    [Theory]
    [InlineData("func take(a: string) -> string => a\nlet a = (\"first\", 0)\nlet equal = a.0 == take(a.0)")]
    [InlineData("func inspect(a: ref/string, b: string) => ()\nlet a = (\"first\", 0)\ninspect(a.0, a.0)")]
    [InlineData("func inspect(a: ref/string, b: (string, i32)) => ()\nlet a = (\"first\", 0)\ninspect(a.0, a)")]
    [InlineData("func inspect(a: ref/string, b: ()) => ()\nvar a = (\"first\", 0)\ninspect(a.0, a.0 = \"new\")")]
    [InlineData("func inspect(a: ref/string, b: ()) => ()\nvar a = (\"first\", 0)\ninspect(a.0, a = (\"new\", 1))")]
    [InlineData("var a = ((\"first\", \"last\"), 0)\nlet equal = a.0.0 == (work: do\n    a.0 = (\"new\", \"last\")\n    exit to work: \"first\"\n)")]
    [InlineData("var a: [2 of string] = [\"first\", \"last\"]\nvar i: isize = 1\nlet equal = a[0] == (work: do\n    a[i] = \"new\"\n    exit to work: \"first\"\n)")]
    [InlineData("var a = (\"first\", 0)\nlet equal = a.0 == (work: do\n    defer => a.0 = \"new\"\n    exit to work: \"first\"\n)")]
    [InlineData("var a = (\"first\", 0)\nlet equal = a.0 == (work: do\n    let nested = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work: \"first\"\n)")]
    [InlineData("func inspect(a: ref/string, b: bool) => ()\nvar a = (\"first\", 0)\ninspect(a.0, (work: do\n    let nested = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work: nested\n))")]
    [InlineData("var a: (string, [1 of i32]) = (\"first\", [0])\nlet x = a.1[(work: do\n    let equal = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work: 0\n)]")]
    public void OverlappingLoansAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.ComparisonLoanConflict);

    [Theory]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\nlet equal = a.0 == \"first\"")]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\nlet equal = same(a.0, a.0)")]
    [InlineData("let a = ((\"first\", \"last\"), 0)\nlet taken = a.0\nlet equal = same(a.0.0, a.0.0)")]
    [InlineData("func f(take: bool)\n    let a = (\"first\", 0)\n    if take\n        let taken = a.0\n    let equal = same(a.0, a.0)")]
    [InlineData("func f()\n    return\n    let a = (\"first\", 0)\n    let taken = a.0\n    let equal = same(a.0, a.0)")]
    public void MissingElementsAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.PossiblyMovedUse);

    [Theory]
    [InlineData("var a: (string, i32)\nlet equal = same(a.0, a.0)")]
    [InlineData("var a: [1 of string]\nlet equal = a[0] == \"first\"")]
    public void UnconstructedReceiversAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.UninitializedUse);

    [Theory]
    [InlineData("let a: [1 of string] = [\"first\"]\nvar i: isize = 0\nlet equal = same(a[i], a[0])")]
    [InlineData("let a: [1 of string] = [\"first\"]\nlet equal = a[0 + 0] == \"first\"")]
    public void UnsupportedReceiversAndPathsRemainExplicit(string source)
        => Reject(Same + source, OwnershipFailure.Unsupported);

    [Theory]
    [InlineData("Comparison", "func f() -> string\n    var a = (\"held\", \"sibling\")\n    defer => a.0 = \"new\"\n    a.0 == (return \"ok\")\n    return \"bad\"\nwriteLine(f())", "held=1;sibling=1;new=1;ok=1;bad=0", new[] { 0, 1, 2, 3 })]
    [InlineData("Argument", "func inspect(a: ref/string, b: bool) => ()\nfunc f() -> string\n    var a = (\"held\", \"sibling\")\n    defer => a.0 = \"new\"\n    inspect(a.0, (return \"ok\"))\n    return \"bad\"\nwriteLine(f())", "held=1;sibling=1;new=1;ok=1;bad=0", new[] { 0, 1, 2, 3 })]
    [InlineData("ReturnResult", "func inspect(a: ref/string) -> string\n    defer\n        if a == a => writeLine(\"cleanup\")\n    return \"ok\"\nlet a = (\"held\", \"sibling\")\nwriteLine(inspect(a.0))", "held=1;sibling=1;cleanup=1;ok=1", new[] { 2, 3, 1, 0 })]
    public void CleanupRetainsResponsibilityAndOrdering(string name, string source, string destructions, int[] order)
    {
        var stdout = name == "ReturnResult" ? "cleanup\nok\n" : "ok\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowCleanup" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowCleanup" + name, source, ir, stdout, destructions, order: order);
    }

    [Fact]
    public void GuardCandidateKeepsItsOwnAddressUnderAnElementLoan()
    {
        var c = MinimalEmissionTest.Analyze(Same + "let a = (\"first\", 0)\nmatch \"other\"\n    let s if same(a.0, s) => ()\n    _ => ()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var call = Assert.Single(body.CallLoans).Call;
        var function = module.GetFunction(0);
        var instruction = Assert.Single(function.Instructions, x => x.Operation == call && x.Opcode == EmissionOpcode.Call);
        Assert.Equal(EmissionOperandKind.ElementAddress, function.Operands[instruction.OperandStart].Kind);
        var candidate = function.Operands[instruction.OperandStart + 1];
        Assert.Equal(EmissionOperandKind.SlotAddress, candidate.Kind);
        Assert.Equal(Assert.Single(body.Matches).Subject, candidate.Value);
    }

    [Fact]
    public void ComparisonDoesNotAcquireOrDestroyItsElement()
    {
        const string Source = "let a = (\"held\", \"sibling\")\nlet same = a.0 == a.0\nif same => writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowResponsibility", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementBorrowResponsibility", Source, ir, "ok\n", "held=1;sibling=1;ok=1", order: [2, 1, 0]);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        Assert.DoesNotContain(body.Places, p => p.Kind == OwnershipPlaceKind.Temporary && p.Source is Kimi.Compiler.Parsing.MemberAccessKoto);
        Assert.Equal(0, body.MovePathCount);
        var comparison = Assert.Single(module.GetFunction(0).Instructions, x => x.Opcode == EmissionOpcode.StringEquals);
        Assert.All(module.GetFunction(0).Operands.Skip(comparison.OperandStart).Take(2), x => Assert.Equal(EmissionOperandKind.ElementAddress, x.Kind));
    }

    [Fact]
    public void LaterBoundsAbortDoesNotRunCleanup()
    {
        const string Source = "func inspect(a: ref/string, b: i32) => ()\nlet a = (\"held\", \"sibling\")\nlet empty: [0 of i32] = []\ndefer => writeLine(\"bad\")\ninspect(a.0, empty[0])";
        const string Error = "Hello.kimi:5:14: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowBounds", Source, ir, string.Empty, "held=0;sibling=0;bad=0", 1, Error);
    }

    [Theory]
    [InlineData("Call", "func forever(a: ref/string) -> Never\n    loop => ()\nlet a = (\"held\", 0)\nforever(a.0)")]
    [InlineData("Cleanup", "func f() -> string\n    let a = (\"held\", 0)\n    defer => loop => ()\n    a.0 == (return \"ok\")\n    return \"bad\"\nwriteLine(f())")]
    public void NonterminationCannotReleaseOrDeliverNormally(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrowDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);

    [Theory]
    [InlineData("missing")]
    [InlineData("operation")]
    [InlineData("source")]
    [InlineData("selector")]
    [InlineData("dynamic")]
    [InlineData("loan")]
    [InlineData("mode")]
    [InlineData("parent")]
    [InlineData("release")]
    [InlineData("move")]
    [InlineData("type")]
    [InlineData("inspection")]
    public void CorruptedBorrowPlansFailBeforeOutputAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Same + "let a: [2 of string] = [\"held\", \"sibling\"]\nlet equal = same(a[0], a[0])\nlet inspected = a[1] == a[1]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        var loan = body.LoanStates[plan.Borrow];
        switch (defect)
        {
            case "missing": body.Projections[0] = plan with { Borrow = -1 }; break;
            case "operation": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Projection = 1 }; break;
            case "source": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Source = body.Operations[body.Projections[1].Borrow].Source }; break;
            case "selector": body.Projections[0] = plan with { Selector = 1 }; break;
            case "dynamic": body.Projections[0] = plan with { Path = -1 }; break;
            case "loan": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Projection = 1 }; break;
            case "mode": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Mode = LoanRequirement.Uniq }; break;
            case "parent": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Parent = plan.Loan }; break;
            case "release": body.LoanStates[plan.Borrow] = -1; break;
            case "move": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Acquisition = AcquisitionKind.Move }; break;
            case "type":
                var place = body.Operations[plan.Borrow].Input;
                body.PlaceStorage[place] = body.Places[place] with { Type = BoundType.I32 };
                break;
            case "inspection": body.StringComparisons[0] = body.StringComparisons[0] with { LeftLoan = -1 }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmBindingAndBorrowEmissionAllocateNothing()
    {
        const string Source = Same + "func f(take: bool)\n    var a: (string, [1 of string], i32) = (\"held\", [\"sibling\"], 40)\n    if take\n        let moved = a.1[0]\n    defer\n        if same(a.0, a.0) => a.2 += 1\n    a.2 += if a.0 == \"held\" => 1 else => 0\n    a.1[0] = \"new\"\nf(true)\nf(false)";
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

    private static void Reject(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
