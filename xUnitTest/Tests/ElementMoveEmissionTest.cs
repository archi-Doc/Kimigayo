// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementMoveEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Let", "let a = (\"first\", \"last\")\nlet taken = a.0\nConsole.writeLine(\"ok\")", "first=1;last=1;ok=1" },
        { "Repair", "var a = (\"first\", \"last\")\nlet taken = a.0\na.0 = \"new\"\nlet whole = a\nConsole.writeLine(\"ok\")", "first=1;last=1;new=1;ok=1" },
        { "Self", "var a = (\"first\", 42)\na.0 = a.0\nlet whole = a\nConsole.writeLine(\"ok\")", "first=1;ok=1" },
        { "Sibling", "var a: [2 of string] = [\"first\", \"last\"]\na[0] = a[1]\nConsole.writeLine(\"ok\")", "first=1;last=1;ok=1" },
        { "ArrayGap", "let a: [5 of string] = [\"a\", \"b\", \"c\", \"d\", \"e\"]\nlet taken = a[2]\nConsole.writeLine(\"ok\")", "a=1;b=1;c=1;d=1;e=1;ok=1" },
        { "Nested", "var a = (\"sibling\", (\"first\", \"last\"))\nlet taken = a.1.0\nlet other = a.1.1\na.1.0 = \"newFirst\"\na.1.1 = \"newLast\"\nlet whole = a\nConsole.writeLine(\"ok\")", "sibling=1;first=1;last=1;newFirst=1;newLast=1;ok=1" },
        { "Aggregate", "let a = ((\"first\", \"last\"), \"sibling\")\nlet taken = a.0\nConsole.writeLine(\"ok\")", "first=1;last=1;sibling=1;ok=1" },
        { "ReplacePartial", "var a = ((\"first\", \"last\"), \"sibling\")\nlet taken = a.0.0\na.0 = (\"newFirst\", \"newLast\")\nlet whole = a\nConsole.writeLine(\"ok\")", "first=1;last=1;sibling=1;newFirst=1;newLast=1;ok=1" },
        { "ReplaceRoot", "var a = (\"first\", \"last\")\nlet taken = a.0\na = (\"newFirst\", \"newLast\")\nConsole.writeLine(\"ok\")", "first=1;last=1;newFirst=1;newLast=1;ok=1" },
        { "Branch", "func f(take?: bool)\n    let a = (\"first\", \"last\")\n    if take\n        let taken = a.0\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;ok=1" },
        { "BranchRepair", "func f(take?: bool)\n    var a = (\"first\", \"last\")\n    if take\n        let taken = a.0\n    a.0 = \"new\"\n    let whole = a\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;new=2;ok=1" },
        { "BranchParent", "func f(take?: bool)\n    var a = ((\"first\", \"last\"), \"sibling\")\n    if take\n        let taken = a.0\n    else\n        let taken = a.0.0\n    a.0 = (\"newFirst\", \"newLast\")\n    let whole = a\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;sibling=2;newFirst=2;newLast=2;ok=1" },
        { "Loop", "var a = (\"first\", \"last\")\nvar i = 0\nloop\n    let taken = a.0\n    a.0 = \"new\"\n    i += 1\n    if i < 3 => continue\n    exit\nConsole.writeLine(\"ok\")", "first=1;last=1;new=3;ok=1" },
        { "Defer", "var a = (\"first\", \"last\")\nwork: do\n    defer => a.0 = \"new\"\n    let taken = a.0\n    exit to work\nlet whole = a\nConsole.writeLine(\"ok\")", "first=1;last=1;new=1;ok=1" },
        { "Return", "func f() -> string\n    let a = (\"first\", \"last\")\n    return a.0\nlet taken = f()\nConsole.writeLine(\"ok\")", "first=1;last=1;ok=1" },
        { "DynamicSibling", "var a: (string, [1 of i32]) = (\"first\", [40])\nlet taken = a.0\nvar i: isize = 0\na.1[i] += 2\nif a.1[i] == 42 => Console.writeLine(\"ok\")", "first=1;ok=1" },
        { "LiteralIdentity", "var a: [2 of string] = [\"first\", \"last\"]\nlet taken = a[(0x0)]\na[0b0] = \"new\"\nlet whole = a\nConsole.writeLine(\"ok\")", "first=1;last=1;new=1;ok=1" },
        { "ZeroSize", "var a: ([0 of string], i32) = ([], 42)\nlet taken = a.0\na.0 = []\nlet whole = a\nConsole.writeLine(\"ok\")", "ok=1" },
        { "ExclusiveSibling", "var a = (40, \"first\")\na.0 += work: do\n    let taken = a.1\n    exit to work: 2\nif a.0 == 42 => Console.writeLine(\"ok\")", "first=1;ok=1" },
        { "CoveredArm", "var a = (\"first\", \"last\")\nmatch true\n    _ => ()\n    true => (work: do\n        let taken = a.0\n    )\nConsole.writeLine(\"ok\")", "first=1;last=1;ok=1" },
        { "BranchSwapHoles", "func f(take?: bool)\n    let a = (\"first\", \"last\")\n    if take\n        let taken = a.0\n    else\n        let taken = a.1\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;ok=1" },
        { "Dead", "func f()\n    return\n    var a = (\"first\", 0)\n    let taken = a.0\n    a.0 = \"new\"\n    let whole = a\nf()\nConsole.writeLine(\"ok\")", "ok=1" },
        { "LoopLifetime", "var i = 0\nloop\n    let a = (\"first\", \"last\")\n    if i == 0\n        let taken = a.0\n    i += 1\n    if i < 3 => continue\n    exit\nConsole.writeLine(\"ok\")", "first=3;last=3;ok=1" },
        { "ConditionalWhole", "func f(take?: bool)\n    let a = (\"first\", \"last\")\n    if take\n        let whole = a\n    else\n        let taken = a.0\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;ok=1" },
        { "BranchPhi", "func f(take?: bool) -> i32\n    return if take => (work: do\n        let a = (\"first\", \"last\")\n        if take\n            let taken = a.0\n        exit to work: 40\n    ) else => 2\nif f(true) + f(false) == 42 => Console.writeLine(\"ok\")", "first=1;last=1;ok=1" },
        { "NestedArray", "var a: [2 of (string, [2 of string])] = [(\"a\", [\"b\", \"c\"]), (\"d\", [\"e\", \"f\"])]\nlet taken = a[0].1[1]\na[0].1[1] = \"new\"\nlet whole = a[0]\nConsole.writeLine(\"ok\")", "a=1;b=1;c=1;d=1;e=1;f=1;new=1;ok=1" },
        { "DynamicReplacement", "var a: (string, [2 of string]) = (\"first\", [\"old\", \"last\"])\nlet taken = a.0\nvar i: isize = 0\na.1[i] = \"new\"\nConsole.writeLine(\"ok\")", "first=1;old=1;last=1;new=1;ok=1" },
        { "ConditionalSelf", "func f(take?: bool)\n    var a = (\"first\", \"last\")\n    a.0 = if take => a.0 else => \"new\"\n    let whole = a\nf(true)\nf(false)\nConsole.writeLine(\"ok\")", "first=2;last=2;new=1;ok=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RemainingResponsibilityIsDestroyedExactlyOnce(string name, string source, string destructions)
    {
        var analysis = MinimalEmissionTest.Analyze(source);
        Assert.False(analysis.Kotonoha.HasSourceErrors, string.Join("\n", analysis.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray().Select(x => x.ToString("source"))));
        Assert.False(analysis.Kotonoha.DiagnosticCollection.HasErrors, string.Join("\n", analysis.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.ToString("source"))));
        var fixture = "ElementMove" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, "ok\n");
        StringEmissionTest.WriteAuditedFixture(fixture, source, ir, "ok\n", destructions);
    }

    [Theory]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\nlet twice = a.0")]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\nlet whole = a")]
    [InlineData("var a = (\"first\", 0)\nlet whole = a\na.0 = \"new\"")]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\na.0 = \"new\"")]
    [InlineData("var a: [1 of string]\na[0] = \"new\"")]
    [InlineData("var a = ((\"first\", \"last\"), 0)\nlet taken = a.0\na.0.0 = \"new\"")]
    [InlineData("var a: [2 of string] = [\"first\", \"last\"]\nvar i: isize = 0\nlet taken = a[i]")]
    [InlineData("var a: [2 of string] = [\"first\", \"last\"]\nlet taken = a[0 + 0]")]
    [InlineData("var a: [2 of string] = [\"first\", \"last\"]\nlet taken = a[0]\nvar i: isize = 0\na[i] = \"new\"")]
    [InlineData("func f(take?: bool)\n    let a = (\"first\", 0)\n    if take\n        let taken = a.0\n    let whole = a")]
    [InlineData("func f()\n    return\n    let a = (\"first\", 0)\n    let taken = a.0\n    let twice = a.0")]
    [InlineData("var a = (\"first\", 0)\nloop\n    let taken = a.0\n    continue")]
    [InlineData("let taken = (\"first\", 0).0")]
    public void InvalidOrUnsupportedMovesPublishNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RemainingPartsFollowReverseLogicalOrder()
    {
        const string Source = "let a = (\"sibling\", (\"first\", \"last\"))\nlet taken = a.1.0\nConsole.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementMoveOrder", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementMoveOrder", Source, ir, "ok\n", "sibling=1;first=1;last=1;ok=1", order: [3, 1, 2, 0]);
    }

    [Fact]
    public void ArrayGapsAndMovedResultKeepDestructionOrder()
    {
        const string Source = "let a: [5 of string] = [\"a\", \"b\", \"c\", \"d\", \"e\"]\nlet taken = a[2]\nConsole.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementMoveArrayOrder", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementMoveArrayOrder", Source, ir, "ok\n", "a=1;b=1;c=1;d=1;e=1;ok=1", order: [5, 2, 4, 3, 1, 0]);
    }

    [Theory]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0\nlet twice = a.0")]
    [InlineData("let a: ([0 of string], i32) = ([], 0)\nlet taken = a.0\nlet twice = a.0")]
    [InlineData("func f(take?: bool)\n    var a = ((\"first\", \"last\"), 0)\n    if take\n        let taken = a.0\n    a.0.0 = \"new\"")]
    [InlineData("func f()\n    return\n    let a = (\"first\", 0)\n    let taken = a.0\n    let twice = a.0")]
    [InlineData("var a = (\"first\", 0)\ndefer\n    let whole = a\nlet taken = a.0")]
    public void MissingValuesAreRejectedByOwnership(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("acquisition")]
    [InlineData("selector")]
    [InlineData("projection")]
    [InlineData("type")]
    public void CorruptedMovePlansFailAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let a: [2 of string] = [\"first\", \"last\"]\nlet taken = a[0]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        switch (defect)
        {
            case "acquisition": body.OperationStorage[plan.Output] = body.Operations[plan.Output] with { Acquisition = AcquisitionKind.None }; break;
            case "selector": body.Projections[0] = plan with { Selector = 1 }; break;
            case "projection": body.OperationStorage[plan.Output] = body.Operations[plan.Output] with { Projection = -1 }; break;
            case "type": body.PlaceStorage[body.Operations[plan.Output].Place] = body.Places[body.Operations[plan.Output].Place] with { Type = BoundType.I32 }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void OnlyConditionalRemaindersNeedFlags()
    {
        var c = MinimalEmissionTest.Analyze("let a = (\"first\", \"last\")\nlet taken = a.0");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        Assert.Empty(module.GetFunction(0).PathFlags);
        c = MinimalEmissionTest.Analyze("func f(take?: bool)\n    let a = (\"first\", \"last\")\n    if take\n        let taken = a.0\nf(true)");
        Assert.True(c.Emission.TryPrepare(out module, out error), error);
        var function = Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction).Single(x => x.PathFlags.Count != 0);
        Assert.Single(function.PathFlags);
        Assert.Empty(function.LiveFlags);
        var body = c.Ownership.Bodies.Single(x => x.MovePathCount != 0);
        var scratch = new int[body.Operations.Count];
        Assert.True(BodyLowering.ValidatePathFlags(body, function, scratch));
        var instruction = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.StorePathFlag && x.Constant == 1);
        function.Instructions.RemoveAt(instruction);
        Assert.False(BodyLowering.ValidatePathFlags(body, function, scratch));
    }

    [Fact]
    public void SparsePathsDoNotExpandTheDeclaredArrayLength()
    {
        var c = MinimalEmissionTest.Analyze("var a: [1000000 of string]\nlet taken = a[42]");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.Equal(2, c.Ownership.Bodies[0].MovePathCount); // Root plus the one referenced element.
    }

    [Theory]
    [InlineData("func inspect(a?: ref/(string, i32)) => ()\nvar a = (\"first\", 0)\nlet taken = a.0\ninspect(a)")]
    [InlineData("func inspect(a?: ref/(string, i32), b?: string) => ()\nvar a = (\"first\", 0)\ninspect(a, a.0)")]
    public void PartialMoveDoesNotBypassWholeBorrowRules(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void ComparisonBorrowsWithoutMovingTheElement(string op)
    {
        var c = MinimalEmissionTest.Analyze($"let a = (\"first\", 0)\nlet result = a.0 {op} \"first\"");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void SharedArgumentBorrowsWithoutMovingTheElement()
    {
        var c = MinimalEmissionTest.Analyze("func inspect(text?: ref/string) => ()\nlet a: [1 of string] = [\"first\"]\ninspect(a[0])");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("var a: (string, [1 of i32]) = (\"held\", [40])\na.1[(work: do\n    let taken = a.0\n    exit to work: 0\n)] += 2")]
    [InlineData("var a = ((\"held\", 40), 0)\na.0.1 += work: do\n    let taken = a.0\n    exit to work: 2")]
    public void MoveConflictsWithReceiverProtectionAndOverlappingExclusiveLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void BoundsAbortDoesNotDestroyRemainingPartsOrTheMovedResult()
    {
        const string Source = "var a: (string, [1 of i32]) = (\"first\", [0])\nlet taken = a.0\na.1[1] = 42";
        const string Error = "Hello.kimi:3:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementMoveBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementMoveBounds", Source, ir, string.Empty, "first=0", 1, Error);
    }

    [Fact]
    public void IndexTransferCleansPartialParentAfterSecuringTheResult()
    {
        const string Source = "func f() -> i32\n    var a: (string, [1 of i32]) = (\"first\", [0])\n    let taken = a.0\n    a.1[(return 42)] = 0\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementMoveIndexTransfer", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementMoveIndexTransfer", Source, ir, "ok\n", "first=1;ok=1", order: [0, 1]);
    }

    [Fact]
    public void NonterminatingDeferPreventsLaterPartialCleanup()
    {
        const string Source = "func f()\n    let a = (\"first\", \"last\")\n    let taken = a.0\n    defer => loop => ()\n    return\nf()";
        ScalarEmissionTest.EmitFixture("ElementMoveDivergent", Source, string.Empty, timeoutMilliseconds: 300);
    }

    [Fact]
    public void WarmPartialMoveAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "func f(take?: bool)\n    var a = ((\"first\", \"last\"), \"sibling\")\n    if take\n        let taken = a.0.0\n    a.0 = (\"newFirst\", \"newLast\")\n    let whole = a\nf(true)\nf(false)";
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
