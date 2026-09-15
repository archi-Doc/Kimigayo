// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementAssignmentEmissionTest
{
    public static TheoryData<string, string> Fixtures => new()
    {
        { "Tuple", "var a = (1, 2)\na.1 = 42\nif a.0 == 1 and a.1 == 42 => writeLine(\"ok\")" },
        { "Array", "var a: [3 of i32] = [1, 2, 3]\nlet i: isize = 1\na[i] = 42\nif a[0] == 1 and a[1] == 42 and a[2] == 3 => writeLine(\"ok\")" },
        { "Nested", "var a: [2 of [2 of i32]] = [[1, 2], [3, 4]]\na[1][0] = 42\nif a[1][0] == 42 and a[0][0] == 1 and a[1][1] == 4 => writeLine(\"ok\")" },
        { "Mixed", "var a: (string, [2 of i32]) = (\"held\", [1, 2])\n(a.1)[0] = 42\nif a.1[0] == 42 => writeLine(\"ok\")" },
        { "Aggregate", "var a = ((1, false), \"held\")\na.0 = (42, true)\nif a.0.0 == 42 and a.0.1 => writeLine(\"ok\")" },
        { "ArrayAggregate", "var a: [2 of [2 of i32]] = [[1, 2], [3, 4]]\na[0] = a[1]\nif a[0][0] == 3 and a[0][1] == 4 => writeLine(\"ok\")" },
        { "Self", "var a = ((42, true), 1)\na.0 = a.0\na.1 = a.1\nif a.0.0 == 42 and a.0.1 and a.1 == 1 => writeLine(\"ok\")" },
        { "Boolean", "var a: [2 of bool] = [false, true]\na[0] = a[1]\na[1] = false\nif a[0] and not a[1] => writeLine(\"ok\")" },
        { "Unit", "var a: [1 of ()] = [()]\nlet done: () = (a[0] = ())\nwriteLine(\"ok\")" },
        { "Empty", "var a: ([0 of i32], i32) = ([], 42)\na.0 = []\nif a.1 == 42 => writeLine(\"ok\")" },
        { "Call", "func make() -> (i32, bool) => (42, true)\nvar a = ((1, false), 2)\na.0 = make()\nif a.0.0 == 42 and a.0.1 => writeLine(\"ok\")" },
        { "Result", "var a = ((1, false), 2)\na.0 = if true => (42, true) else => (0, false)\nif a.0.0 == 42 and a.0.1 => writeLine(\"ok\")" },
        { "Loop", "var a: [3 of i32] = [0, 0, 0]\nvar i: isize = 0\nwhile i < 3\n    a[i] = 14\n    i += 1\nif a[0] + a[1] + a[2] == 42 => writeLine(\"ok\")" },
        { "Deferred", "var a = (0, true)\nvar i = 0\nloop\n    defer => a.0 = a.0 + 14\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0 == 42 => writeLine(\"ok\")" },
        { "Dead", "func f()\n    return\n    var a = (1, 2)\n    a.0 = 42\nf()\nwriteLine(\"ok\")" },
        { "DeadArm", "var a = (0, true)\nmatch true\n    _ => a.0 = 42\n    true => a.0 = 1\nif a.0 == 42 => writeLine(\"ok\")" },
        { "ReadIndex", "var a: [2 of isize] = [1, 0]\na[a[0]] = 42\nif a[1] == 42 => writeLine(\"ok\")" },
        { "Snapshot", "var a: [1 of i32] = [40]\nvar i: isize = 0\na[(work: do\n    i = 1\n    exit to work: 0\n)] = i@i32 + 42\nif a[0] == 42 and i == 1 => writeLine(\"ok\")" },
        { "RestoreRhs", "var a: (string, i32) = (\"old\", 0)\na.1 = (work: do\n    a = (\"new\", 1)\n    exit to work: 42\n)\nif a.1 == 42 => writeLine(\"ok\")" },
        { "TransferIndex", "func f() -> i32\n    var a: [1 of i32] = [0]\n    defer => a[0] = 1\n    a[(return 42)] = 2\n    return 0\nif f() == 42 => writeLine(\"ok\")" },
        { "TransferRhs", "func index() -> isize\n    writeLine(\"bad\")\n    return 0\nfunc f() -> i32\n    var a: [1 of i32] = [0]\n    a[index()] = (return 42)\n    return 0\nif f() == 42 => writeLine(\"ok\")" },
        { "NestedTransfer", "func index() -> isize\n    writeLine(\"bad\")\n    return 0\nfunc f() -> i32\n    var a: [1 of [1 of i32]] = [[0]]\n    a[(return 42)][index()] = 2\n    return 0\nif f() == 42 => writeLine(\"ok\")" },
        { "TransferPriority", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[(return 1)] = (return 42)\n    return 0\nif f() == 42 => writeLine(\"ok\")" },
        { "TransferBoundary", "func f() -> i32\n    var a: [1 of i32] = [0]\n    let result = work: loop\n        a[(return 1)] = (exit to work: 42)\n    return result\nif f() == 42 => writeLine(\"ok\")" },
        { "DeferredAggregate", "var a = ((0, false), \"held\")\nvar i = 0\nloop\n    defer => a.0 = if true => (42, true) else => (0, false)\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0.0 == 42 and a.0.1 => writeLine(\"ok\")" },
        { "UnitEffects", "func unit()\n    writeLine(\"ok\")\nvar a: [1 of ()] = [()]\na[0] = unit()" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CopyElementsCanBeReplaced(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementAssignment" + name, source, "ok\n");

    [Theory]
    [InlineData("let a = (1, 2)\na.0 = 42")]
    [InlineData("var a: [1 of i32]\na[0] = 42")]
    [InlineData("var a = (\"held\", 0)\nlet b = a\na.1 = 42")]
    [InlineData("var a = (\"held\", 0)\na.0 = a.0")]
    [InlineData("var a: [1 of i32] = [0]\na[0] += true")]
    [InlineData("let a: [1 of i32] = [0]\na[0]++")]
    [InlineData("func f(a: [1 of i32])\n    a[0] = 42")]
    [InlineData("func f() -> [1 of i32] => [0]\nf()[0] = 42")]
    [InlineData("var a: [1 of i32] = [0]\na[0] = true")]
    [InlineData("var a: [1 of i32] = [0]\nlet i: i32 = 0\na[i] = 42")]
    [InlineData("func f()\n    return\n    var a: [1 of i32]\n    a[0] = 42")]
    public void InvalidOrUnsupportedWritesProduceNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RightSideAndEachIndexExecuteOnceInOrder()
    {
        const string Source = "func value() -> i32\n    writeLine(\"value\")\n    return 42\nfunc outer() -> isize\n    writeLine(\"outer\")\n    return 0\nfunc inner() -> isize\n    writeLine(\"inner\")\n    return 0\nvar a: [1 of [1 of i32]] = [[0]]\na[outer()][inner()] = value()\nif a[0][0] == 42 => writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementAssignmentOrder", Source, "value\nouter\ninner\nok\n");
    }

    [Theory]
    [InlineData("i8", "-128")]
    [InlineData("u8", "255")]
    [InlineData("i16", "-32768")]
    [InlineData("u16", "65535")]
    [InlineData("i32", "-2147483648")]
    [InlineData("u32", "4294967295")]
    [InlineData("i64", "-9223372036854775808")]
    [InlineData("u64", "18446744073709551615")]
    [InlineData("i128", "-170141183460469231731687303715884105728")]
    [InlineData("u128", "340282366920938463463374607431768211455")]
    [InlineData("isize", "-9223372036854775808")]
    [InlineData("usize", "18446744073709551615")]
    [InlineData("f32", "1.25")]
    [InlineData("f64", "2.5")]
    [InlineData("char", "'😀'")]
    public void StoresUseTheExactScalarRepresentation(string type, string value)
    {
        var initial = type == "char" ? "'a'" : type is "f32" or "f64" ? "0.0" : "0";
        var source = $"var a: [1 of {type}] = [{initial}]\na[0] = {value}\nif a[0] == {value} => writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementAssignment" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("Negative", "-1", 1, "[0]")]
    [InlineData("Length", "1", 1, "[0]")]
    [InlineData("Large", "9223372036854775807", 1, "[0]")]
    [InlineData("Empty", "0", 0, "[]")]
    public void BoundsAbortAfterTheRightSide(string name, string index, int length, string initial)
    {
        var source = $"func value() -> i32\n    writeLine(\"value\")\n    return 42\nvar a: [{length} of i32] = {initial}\ndefer => writeLine(\"bad\")\na[{index}] = value()\nwriteLine(\"bad\")";
        ScalarEmissionTest.EmitFixture("ElementAssignmentBounds" + name, source, "value\n", 1, "Hello.kimi:6:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Fact]
    public void ZeroByteBoundsAndEarlierIndicesStillAbort()
    {
        const string Source = "func index() -> isize\n    writeLine(\"bad\")\n    return 0\nvar a: [0 of [1 of ()]] = []\na[0][index()] = ()";
        ScalarEmissionTest.EmitFixture("ElementAssignmentZeroBounds", Source, string.Empty, 1, "Hello.kimi:5:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Theory]
    [InlineData("var a: [1 of i32] = [0]\na[(work: do\n    a = [1]\n    exit to work: 0\n)] = 42")]
    [InlineData("var a: [1 of i32] = [0]\na[(work: do\n    a[0] = 1\n    exit to work: 0\n)] = 42")]
    [InlineData("var a: [1 of i32] = [0]\nlet n = a[(work: do\n    a[0] = 42\n    exit to work: 0\n)]")]
    [InlineData("var a: [1 of i32] = [0]\na[(work: do\n    defer => a[0] = 1\n    exit to work: 0\n)] = 42")]
    [InlineData("func take(a: (string, [1 of i32])) => ()\nvar a: (string, [1 of i32]) = (\"held\", [0])\na.1[(work: do\n    take(a)\n    exit to work: 0\n)] = 42")]
    public void AccessProtectionRejectsConflictingWritesAndMoves(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void CopyingAnAggregateSecuresAnIndependentSnapshot()
    {
        const string Source = "var source: [2 of i32] = [40, 2]\nvar target: [1 of [2 of i32]] = [[0, 0]]\ntarget[(work: do\n    source = [0, 0]\n    exit to work: 0\n)] = source\nif target[0][0] == 40 and target[0][1] == 2 and source[0] == 0 => writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementAssignmentAggregateSnapshot", Source, "ok\n");
    }

    [Fact]
    public void ParentResponsibilityAndResultCleanupArePreserved()
    {
        const string Source = "var a = (\"first\", (0, false), \"last\")\na.1 = (work: do\n    defer => writeLine(\"cleanup\")\n    exit to work: (42, true)\n)\nif a.1.0 == 42 and a.1.1 => writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementAssignmentLifetime", Source, "cleanup\nok\n");
        StringEmissionTest.WriteAuditedFixture("ElementAssignmentLifetime", Source, ir, "cleanup\nok\n", "first=1;last=1;cleanup=1;ok=1", order: [2, 3, 1, 0]);
    }

    [Fact]
    public void AbortedLocationDoesNotDestroyTheParentOrStartCleanup()
    {
        const string Source = "var a: (string, [0 of i32]) = (\"held\", [])\ndefer => writeLine(\"bad\")\na.1[0] = 42";
        const string Error = "Hello.kimi:3:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementAssignmentAbortLifetime", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementAssignmentAbortLifetime", Source, ir, string.Empty, "held=0;bad=0", 1, Error);
    }

    [Fact]
    public void NonterminatingRightSideCleanupPreventsDestinationAccess()
    {
        const string Source = "var a = ((0, false), \"held\")\na.0 = work: do\n    defer => loop => ()\n    exit to work: (42, true)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode is EmissionOpcode.StoreElement or EmissionOpcode.ElementAddress or EmissionOpcode.DestroyAggregate);
        ScalarEmissionTest.EmitFixture("ElementAssignmentDivergent", Source, string.Empty, timeoutMilliseconds: 300);
    }

    [Fact]
    public void NonterminatingIndexCleanupPreventsTheStore()
    {
        const string Source = "var a: (string, [1 of i32]) = (\"held\", [0])\na.1[work: do\n    defer => loop => ()\n    exit to work: 0\n] = 42";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode is EmissionOpcode.StoreElement or EmissionOpcode.DestroyAggregate);
        ScalarEmissionTest.EmitFixture("ElementAssignmentIndexDivergent", Source, string.Empty, timeoutMilliseconds: 300);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("loan")]
    [InlineData("root")]
    [InlineData("input")]
    [InlineData("inputRange")]
    [InlineData("parent")]
    [InlineData("release")]
    [InlineData("source")]
    [InlineData("mutable")]
    [InlineData("value")]
    public void InvalidWritePlansAreRejectedAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var a: [1 of i32] = [0]\na[0] = 42");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        var write = body.Operations[plan.Write];
        switch (defect)
        {
            case "write": body.Projections[0] = plan with { Write = -1 }; break;
            case "loan": body.Projections[0] = plan with { Loan = -1 }; break;
            case "root": body.Projections[0] = plan with { Root = -1 }; break;
            case "input": body.OperationStorage[plan.Write] = write with { Input = plan.Root }; break;
            case "inputRange": body.OperationStorage[plan.Write] = write with { Input = int.MaxValue }; break;
            case "parent": body.Projections[0] = plan with { Parent = 0 }; break;
            case "release": body.LoanStates[plan.Write - 1] = plan.Loan; break;
            case "source": body.OperationStorage[plan.Write] = write with { Source = body.Operations[plan.Operation].Source }; break;
            case "mutable": body.PlaceStorage[plan.Root] = body.Places[plan.Root] with { Mutable = false }; break;
            case "value": body.Values[plan.Write] = default; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmElementWritesAllocateNothing()
    {
        const string Source = "func make() -> (i32, bool) => (42, true)\nvar a: (string, [2 of (i32, bool)]) = (\"held\", [(0, false), (0, false)])\nvar i = 0\nloop\n    defer => a.1[0] = if true => make() else => (0, false)\n    a.1[1] = a.1[0]\n    i += 1\n    if i < 3 => continue\n    exit\nif a.1[0].0 == 42 => writeLine(\"ok\")";
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
