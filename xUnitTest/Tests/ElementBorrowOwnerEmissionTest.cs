// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementBorrowOwnerEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";
    private const string Make = "func make() -> (string, i32) => (\"held\", 42)\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Parameter", "func f(a: (string, i32)) -> bool => same(a.0, a.0)\nif f((\"held\", 0)) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterArray", "func f(a: [2 of string]) -> bool => a[0] < a[1]\nlet a: [2 of string] = [\"first\", \"last\"]\nif f(a@move) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterMoveAfter", "func f(a: (string, i32)) -> (string, i32)\n    let equal = same(a.0, a.0)\n    return a@move\nif f((\"held\", 0)).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterNested", "func f(a: (string, (string, i32))) -> bool => same(a.0, a.1.0)\nif f((\"held\", (\"held\", 42))) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterDefer", "func f(a: (string, i32))\n    defer\n        if a.0 == \"held\" => Console.writeLine(\"ok\")\nf((\"held\", 0))", "ok\n" },
        { "Literal", "if (\"held\", 42).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "LiteralArguments", "if same((\"held\", 0).0, (\"held\", 1).0) => Console.writeLine(\"ok\")", "ok\n" },
        { "CallComparison", "if make().0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "CallArguments", "if same(make().0, make().0) => Console.writeLine(\"ok\")", "ok\n" },
        { "CallArray", "func names() -> [2 of string] => [\"first\", \"last\"]\nif names()[0] < names()[1] => Console.writeLine(\"ok\")", "ok\n" },
        { "If", "func f(flag: bool) -> bool => same((if flag => (\"held\", 1) else => (\"held\", 2)).0, \"held\")\nif f(true) and f(false) => Console.writeLine(\"ok\")", "ok\n" },
        { "Do", "if (work: do\n    exit to work: (\"held\", 42)\n).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "LoopResult", "if same((loop\n    exit (\"held\", 42)\n).0, \"held\") => Console.writeLine(\"ok\")", "ok\n" },
        { "MatchResult", "if (match true\n    true => (\"held\", 42)\n    false => (\"other\", 0)\n).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "NestedResult", "func nested() -> (i32, (string, i32)) => (0, (\"held\", 42))\nif same(nested().1.0, (if true => nested() else => nested()).1.0) => Console.writeLine(\"ok\")", "ok\n" },
        { "ShortCircuit", "func f() -> (string, i32)\n    Console.writeLine(\"bad\")\n    return (\"held\", 0)\nlet skipped = false and same(f().0, \"held\")\nlet skippedAgain = true or f().0 == \"held\"\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Evaluation", "func f() -> (string, i32)\n    Console.writeLine(\"make\")\n    return (\"held\", 0)\nif same(f().0, f().0) => Console.writeLine(\"ok\")", "make\nmake\nok\n" },
        { "Guard", "match \"other\"\n    let s if same(make().0, s) => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"ok\")", "ok\n" },
        { "NestedGuard", "func three(a: ref/string, b: ref/string, c: ref/string) -> bool => b == c\nmatch \"other\"\n    let s if three(make().0, s, \"other\") => Console.writeLine(\"ok\")\n    _ => Console.writeLine(\"bad\")", "ok\n" },
        { "IndependentResult", "func inspect(a: ref/string) -> (string, i32) => (\"held\", 0)\nif inspect(make().0).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "MixedLocal", "let a = (\"held\", 0)\nif same(a.0, make().0) and same(make().0, a.0) => Console.writeLine(\"ok\")\nlet moved = a@move", "ok\n" },
        { "DeferClones", "func f(flag: bool)\n    defer\n        if same((work: do\n            exit to work: (\"held\", 0)\n        ).0, make().0) => Console.writeLine(\"ok\")\n    if flag => return\nf(true)\nf(false)", "ok\nok\n" },
        { "Repeated", "var n = 0\nloop\n    if not same(make().0, (\"held\", 0).0) => exit\n    n += 1\n    if n < 3 => continue\n    exit\nif n == 3 => Console.writeLine(\"ok\")", "ok\n" },
        { "Unreachable", "func f()\n    return\n    let equal = same(make().0, (\"held\", 0).0)\nf()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Covered", "match true\n    _ => Console.writeLine(\"ok\")\n    true => (work: do\n        let equal = same(make().0, (\"held\", 0).0)\n    )", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void OwnersSupportStaticStringBorrowing(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementBorrowOwner" + name, Same + Make + source, stdout);

    [Theory]
    [InlineData("func inspect(a: ref/string, b: (string, i32)) => ()\nfunc f(a: (string, i32)) => inspect(a.0, a@move)", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("func take(a: (string, i32)) -> string => \"held\"\nfunc f(a: (string, i32)) -> bool => a.0 == take(a@move)", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("func f(a: (string, i32)) -> bool\n    let moved = a@move\n    return same(a.0, a.0)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let value = make().0", OwnershipFailure.Unsupported)]
    [InlineData("func f(a: [1 of string], i: isize) -> bool => same(a[i], \"held\")", OwnershipFailure.Unsupported)]
    public void BorrowingDoesNotGrantMoveOrDynamicAccess(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Same + Make + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    public static TheoryData<string, string, string, string, int[]> Audits => new()
    {
        { "LiteralCondition", "if (\"head\", \"tail\").0 == \"head\" => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;ok=1", [0, 1, 0, 2] },
        { "CallCondition", "func make() -> (string, string) => (\"head\", \"tail\")\nif make().0 == \"head\" => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;ok=1", [0, 1, 0, 2] },
        { "ParameterCleanup", "func f(a: (string, string)) -> bool => a.0 == a.0\nif f((\"head\", \"tail\")) => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "ReverseTemporaries", Same + "if same((\"head\", \"tail\").0, (\"head\", \"last\").0) => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;last=1;ok=1", [2, 0, 1, 0, 3] },
        { "ConditionalLiteral", "func f(flag: bool) -> bool => flag and (\"head\", \"tail\").0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;ok=1", [0, 1, 0, 2] },
        { "ConditionalCall", "func make() -> (string, string) => (\"head\", \"tail\")\nfunc f(flag: bool) -> bool => flag and make().0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;ok=1", [0, 1, 0, 2] },
        { "ConditionalSelection", "func f(flag: bool) -> bool => flag and (if flag => (\"head\", \"tail\") else => (\"bad\", \"bad\")).0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;bad=0;ok=1", [0, 1, 0, 3] },
        { "RepeatedConditional", "var n = 0\nwhile n < 4\n    let equal = n % 2 == 0 and (if n == 0 => (\"head\", \"tail\") else => (\"head\", \"tail\")).0 == \"head\"\n    n += 1", string.Empty, "head=4;tail=2", [0, 1, 0, 0, 1, 0] },
        { "DeferredConditional", "func f(flag: bool, early: bool)\n    defer\n        let equal = flag and (if flag => (\"head\", \"tail\") else => (\"bad\", \"bad\")).0 == \"head\"\n    if early => return\nf(true, true)\nf(false, false)\nf(true, false)\nf(false, true)", string.Empty, "head=4;tail=2;bad=0", [0, 1, 0, 0, 1, 0] },
        { "ArgumentTransfer", "func inspect(a: ref/string, b: bool) => ()\nfunc f() -> string\n    defer => Console.writeLine(\"cleanup\")\n    inspect((\"head\", \"tail\").0, (return \"out\"))\n    return \"bad\"\nConsole.writeLine(f())", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1;bad=0", [1, 0, 2, 3] },
        { "ComparisonTransfer", "func f() -> string\n    defer => Console.writeLine(\"cleanup\")\n    (\"head\", \"tail\").0 == (return \"out\")\n    return \"bad\"\nConsole.writeLine(f())", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1;bad=0", [1, 0, 2, 3] },
        { "LastUseIsNotDestruction", "func later() -> i32\n    Console.writeLine(\"later\")\n    return 42\nfunc inspect(a: bool, b: i32) => ()\ninspect((\"head\", \"tail\").0 == \"head\", later())\nConsole.writeLine(\"ok\")", "later\nok\n", "head=2;tail=1;later=1;ok=1", [2, 0, 1, 0, 3] },
        { "CalleeCleanup", "func inspect(a: ref/string) -> string\n    defer\n        if a == a => Console.writeLine(\"cleanup\")\n    return \"out\"\nConsole.writeLine(inspect((\"head\", \"tail\").0))", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1", [2, 3, 1, 0] },
    };

    [Theory]
    [MemberData(nameof(Audits))]
    public void OwnersKeepTheirSpecifiedLifetime(string name, string source, string stdout, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowOwnerLifetime" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowOwnerLifetime" + name, source, ir, stdout, destructions, order: order);
    }

    [Fact]
    public void ConditionalConstructionActivatesItsDestructionFlag()
    {
        var c = MinimalEmissionTest.Analyze("func f(flag: bool) -> bool => flag and (\"head\", \"tail\").0 == \"head\"\nf(true)\nf(false)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[1];
        var function = module.GetFunction(1);
        var place = Assert.Single(function.LiveFlags, p => body.Places[p].Type.Kind == BoundTypeKind.Tuple);
        Assert.Contains(function.Instructions, x => x.Opcode == EmissionOpcode.StoreLiveFlag && x.Place == place && x.Constant == 1 && body.Operations[x.Operation].Kind == OwnershipOperationKind.CompleteConstruction);
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("completion")]
    [InlineData("clear")]
    [InlineData("value")]
    [InlineData("duplicate")]
    public void ConditionalFlagsRejectMissingOrIncorrectTransitions(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func f(flag: bool) -> bool => flag and (\"head\", \"tail\").0 == \"head\"\nf(true)\nf(false)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[1];
        var function = module.GetFunction(1);
        var place = Assert.Single(function.LiveFlags, p => body.Places[p].Type.Kind == BoundTypeKind.Tuple);
        var index = function.Instructions.FindIndex(x => x.Place == place && (defect == "entry" ? x.Opcode == EmissionOpcode.InitializeLiveFlag :
            x.Opcode == EmissionOpcode.StoreLiveFlag && body.Operations[x.Operation].Kind == (defect == "clear" ? OwnershipOperationKind.Cleanup : OwnershipOperationKind.CompleteConstruction)));
        Assert.True(index >= 0);
        var instruction = function.Instructions[index];
        if (defect == "duplicate")
        {
            function.Instructions.Insert(index, instruction);
        }
        else if (defect == "value")
        {
            function.Instructions[index] = instruction with { Constant = 0 };
        }
        else
        {
            function.Instructions.RemoveAt(index);
        }

        Assert.False(BodyLowering.ValidateStringFlags(body, function, new int[body.Places.Count + body.Operations.Count]));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData("parameter", "func f(a: (string, i32)) -> bool => a.0 == a.0\nf(make())")]
    [InlineData("construction", "let equal = (\"held\", 0).0 == \"held\"")]
    [InlineData("call", "let equal = make().0 == \"held\"")]
    [InlineData("selection", "let equal = (if true => (\"held\", 0) else => (\"held\", 1)).0 == \"held\"")]
    public void BorrowOwnersRequireTheirStoragePlan(string defect, string source)
    {
        var c = MinimalEmissionTest.Analyze(Make + source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Projections.Count > 0);
        var root = body.Projections[0].Root;
        switch (defect)
        {
            case "parameter": body.PlaceStorage[root] = body.Places[root] with { Kind = OwnershipPlaceKind.Temporary }; break;
            case "construction": body.ConstructionStorage.Clear(); break;
            case "call":
                var produce = body.OperationStorage.FindIndex(x => x.Place == root && x.Kind == OwnershipOperationKind.Produce);
                body.OperationStorage[produce] = body.Operations[produce] with { Kind = OwnershipOperationKind.Declare };
                break;
            case "selection": body.SlotResults.Clear(); break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData(OwnershipOperationKind.LocateReceiver)]
    [InlineData(OwnershipOperationKind.Cleanup)]
    public void SecuredSelectionStorageIsUnavailableDuringItsCleanup(OwnershipOperationKind kind)
    {
        const string Source = "let equal = (work: do\n    defer => Console.writeLine(\"cleanup\")\n    exit to work: (\"held\", 0)\n).0 == \"held\"";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.SlotResults);
        var deferred = Assert.Single(body.DeferredPlans, x => body.IsReachable(x.Entry));
        var id = body.OperationStorage.FindIndex(deferred.Entry, deferred.End - deferred.Entry, x => x.Kind == (kind == OwnershipOperationKind.Cleanup ? OwnershipOperationKind.Cleanup : OwnershipOperationKind.Produce));
        Assert.True(id >= 0);
        Assert.True((body.GetInputState(id, result.Place) & PlaceState.MustInit) != 0);
        body.OperationStorage[id] = body.Operations[id] with { Kind = kind, Place = result.Place };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.Contains("normal delivery", error);
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData("Argument", "func inspect(a: ref/string, b: i32) => ()\nlet empty: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\ninspect((\"head\", \"tail\").0, empty[0])", 4, 29)]
    [InlineData("Construction", "let empty: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\nlet equal = ((\"head\", empty[0]), \"tail\").0.0 == \"head\"", 3, 23)]
    public void AbortDoesNotDestroyBorrowOwners(string name, string source, int line, int column)
    {
        var error = $"Hello.kimi:{line}:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowOwnerAbort" + name, source, string.Empty, 1, error);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowOwnerAbort" + name, source, ir, string.Empty, "head=0;tail=0;bad=0", 1, error);
    }

    [Theory]
    [InlineData("Call", "func forever(a: ref/string) -> Never\n    loop => ()\nforever((\"held\", 0).0)")]
    [InlineData("ResultCleanup", "let equal = (work: do\n    defer => loop => ()\n    exit to work: (\"held\", 0)\n).0 == \"held\"")]
    public void NonterminationCannotReleaseOrDeliverAnOwner(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrowOwnerDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);

    [Fact]
    public void WarmBindingAndOwnerBorrowEmissionAllocateNothing()
    {
        const string Source = Same + Make + "func f(a: (string, i32), flag: bool) -> bool\n    defer\n        let equal = flag and (if flag => make() else => (\"held\", 0)).0 == a.0\n    if flag => return same(a.0, make().0)\n    return flag or same((\"held\", 0).0, a.0)\nf(make(), true)\nf(make(), false)";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var valid = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
