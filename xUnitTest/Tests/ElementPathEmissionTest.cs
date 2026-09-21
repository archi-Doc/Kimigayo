// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementPathEmissionTest
{
    public static TheoryData<string, string> Fixtures => new()
    {
        { "Array", "var a: [2 of i32] = [40, 2]\na[0] += a[1]\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "Tuple", "var a = (40, 2)\na.0 += a.1\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Nested", "var a: [2 of [2 of i32]] = [[40, 0], [0, 2]]\na[0][0] += a[1][1]\nif a[0][0] == 42 => Console.writeLine(\"ok\")" },
        { "SameOuter", "var a: [1 of [2 of i32]] = [[40, 2]]\na[0][0] += a[0][1]\nif a[0][0] == 42 => Console.writeLine(\"ok\")" },
        { "NestedUpdate", "var a: [2 of i32] = [40, 2]\na[0] += a[1]++\nif a[0] == 42 and a[1] == 3 => Console.writeLine(\"ok\")" },
        { "NestedPrefix", "var a: [2 of i32] = [40, 1]\na[0] += ++a[1]\nif a[0] == 42 and a[1] == 2 => Console.writeLine(\"ok\")" },
        { "RhsStore", "var a: [2 of i32] = [40, 0]\na[0] += work: do\n    a[1] = 2\n    exit to work: a[1]\nif a[0] == 42 and a[1] == 2 => Console.writeLine(\"ok\")" },
        { "RhsCleanup", "var a: [2 of i32] = [40, 2]\na[0] += work: do\n    defer => a[1]++\n    exit to work: a[1]\nif a[0] == 42 and a[1] == 3 => Console.writeLine(\"ok\")" },
        { "Selection", "var a: [2 of i32] = [40, 2]\na[0] += if true => a[1] else => 0\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "Snapshot", "var a: [2 of i32] = [40, 2]\na[0] += a[1] + (work: do\n    a[1] = 99\n    exit to work: 0\n)\nif a[0] == 42 and a[1] == 99 => Console.writeLine(\"ok\")" },
        { "DynamicPrefix", "var a: ([1 of i32], [1 of i32]) = ([40], [2])\nvar i: isize = 0\na.0[i] += a.1[i]++\nif a.0[0] == 42 and a.1[0] == 3 => Console.writeLine(\"ok\")" },
        { "DynamicDescendant", "var a: [2 of [1 of i32]] = [[40], [2]]\nvar i: isize = 0\na[0][i] += a[1][i]\nif a[0][0] == 42 => Console.writeLine(\"ok\")" },
        { "Literals", "var a: [2 of i32] = [40, 2]\na[((0x0))] += a[(0b0_1)]\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "CopyAggregate", "func get(a: [1 of i32]) -> i32 => a[0]\nvar a: (i32, [1 of i32]) = (40, [2])\na.0 += get(a.1)\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "ZeroSizeSibling", "func amount(unit: ()) -> i32 => 2\nvar a = ((), 40)\na.1 += amount(a.0)\nif a.1 == 42 => Console.writeLine(\"ok\")" },
        { "IndexRead", "var a: (i32, [1 of i32], isize) = (41, [1], 0)\na.0 += a.1[a.2]++\nif a.0 == 42 and a.1[0] == 2 => Console.writeLine(\"ok\")" },
        { "Transfer", "func f() -> i32\n    var a = (40, 2)\n    defer\n        if a.0 == 40 and a.1 == 3 => Console.writeLine(\"ok\")\n    a.0 += (work: do\n        a.1++\n        return 42\n    )\n    return 0\nf()" },
        { "Deferred", "var a: [2 of i32] = [0, 14]\nvar i = 0\nloop\n    defer => a[0] += a[1]\n    i += 1\n    if i < 3 => continue\n    exit\nif a[0] == 42 => Console.writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DisjointElementsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementPath" + name, source, "ok\n");

    [Theory]
    [InlineData("AggregateStore", "var a: (i32, [2 of i32]) = (40, [0, 0])\na.0 += work: do\n    a.1 = [1, 2]\n    exit to work: a.1[1]\nif a.0 == 42 and a.1[0] == 1 => Console.writeLine(\"ok\")", "ok\n")]
    [InlineData("UnitStore", "func mark() => Console.writeLine(\"unit\")\nvar a = (40, ())\na.0 += work: do\n    a.1 = mark()\n    exit to work: 2\nif a.0 == 42 => Console.writeLine(\"ok\")", "unit\nok\n")]
    public void DisjointCopyReplacementPreservesEvaluation(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementPathAdditional" + name, source, stdout);

    [Fact]
    public void MissingStaticElementKeepsItsRuntimeBoundsCheck()
        => ScalarEmissionTest.EmitFixture("ElementPathBounds", "var a: ([1 of i32], [0 of i32]) = ([40], [])\na.0[0] += a.1[0]", string.Empty, 1, "Hello.kimi:2:11: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void DisjointUpdatesPreserveParentDestruction()
    {
        const string Source = "var a = (\"first\", (40, 2), \"last\")\na.1.0 += a.1.1++\nif a.1.0 == 42 and a.1.1 == 3 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementPathLifetime", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementPathLifetime", Source, ir, "ok\n", "first=1;last=1;ok=1", order: [2, 1, 0]);
    }

    [Theory]
    [InlineData("a[0] += a[0]")]
    [InlineData("a[1] += a[0x1]")]
    [InlineData("a[1] += a[((0b1))]")]
    [InlineData("a[0] += a[i]")]
    [InlineData("a[i] += a[1]")]
    [InlineData("a[0] += a[+1]")]
    [InlineData("a[0] += a[1 + 0]")]
    [InlineData("a[0] += a[1@isize]")]
    [InlineData("a[0] += a[(if true => 1 else => 1)]")]
    [InlineData("if i != 1 => a[i] += a[1]")]
    [InlineData("a[0] += get(a)")]
    [InlineData("a[0] += (work: do\n    a = [1, 2]\n    exit to work: 2\n)")]
    [InlineData("a[0] += (work: do\n    defer => a[0]\n    exit to work: 2\n)")]
    [InlineData("a[0] += (work: do\n    a[1]++\n    exit to work: a[0]\n)")]
    [InlineData("a[(work: do\n    a[1]++\n    exit to work: 0\n)] += 2")]
    [InlineData("let n = a[(work: do\n    a[1] = 1\n    exit to work: 0\n)]")]
    public void OverlappingOrUnprovenPathsProduceNoIr(string expression)
    {
        var c = MinimalEmissionTest.Analyze("func get(a: [2 of i32]) -> i32 => a[0]\nvar a: [2 of i32] = [40, 2]\nvar i: isize = 0\n" + expression);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("func get(a: [2 of i32]) -> i32 => a[1]\nvar a: [1 of [2 of i32]] = [[40, 2]]\na[0][0] += get(a[0])")]
    [InlineData("var a: [2 of [2 of i32]] = [[40, 2], [0, 0]]\nvar i: isize = 0\nvar j: isize = 1\na[i][0] += a[j][1]")]
    [InlineData("func f()\n    return\n    var a: [2 of i32] = [40, 2]\n    a[0] += a[0]")]
    public void AncestorsAndUnknownPrefixesRemainProtected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("selector")]
    [InlineData("path")]
    [InlineData("depth")]
    [InlineData("parent")]
    [InlineData("operation")]
    [InlineData("copy")]
    [InlineData("store")]
    [InlineData("location")]
    public void CorruptedPathPlansFailBeforeOutputAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var a: [2 of i32] = [40, 2]\na[0] += a[1]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[1];
        var update = body.Projections[0];
        switch (defect)
        {
            case "selector": body.Projections[1] = plan with { Selector = 0 }; break;
            case "path": body.Projections[1] = plan with { Path = int.MaxValue }; break;
            case "depth": body.Projections[1] = plan with { PathDepth = int.MaxValue }; break;
            case "parent": body.Projections[1] = plan with { Parent = 1 }; break;
            case "operation": body.OperationStorage[plan.Operation] = body.Operations[plan.Operation] with { Projection = 0 }; break;
            case "copy": body.OperationStorage[plan.Output] = body.Operations[plan.Output] with { Projection = 0 }; break;
            case "store": body.OperationStorage[update.Write] = body.Operations[update.Write] with { Projection = 1 }; break;
            case "location":
                var location = body.ComparisonLoans[plan.Loan].Read;
                body.OperationStorage[location] = body.Operations[location] with { Kind = OwnershipOperationKind.Read };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmPathAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "var a: (string, [2 of i32]) = (\"held\", [0, 14])\nvar i = 0\nloop\n    defer => a.1[0] += a.1[1]\n    i += 1\n    if i < 3 => continue\n    exit\na.1[0] += a.1[1]++";
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
