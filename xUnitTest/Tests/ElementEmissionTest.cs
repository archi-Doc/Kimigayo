// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementEmissionTest
{
    public static TheoryData<string, string> Fixtures => new()
    {
        { "Tuple", "let pair = (40, 2)\nif pair.0 + pair.1 == 42 => writeLine(\"ok\")" },
        { "Array", "let a: [3 of i32] = [7, 42, 9]\nlet i: isize = 1\nif a[i] == 42 => writeLine(\"ok\")" },
        { "Nested", "let a: [2 of [2 of i32]] = [[1, 2], [3, 42]]\nif a[1][1] == 42 => writeLine(\"ok\")" },
        { "Mixed", "let pair = (\"kept\", ((1, 42), true))\nif pair.1.0.1 == 42 and pair.1.1 => writeLine(\"ok\")" },
        { "Parameter", "func get(a: [2 of i32], i: isize) -> i32 => a[i]\nlet a: [2 of i32] = [7, 42]\nif get(a, 1) == 42 => writeLine(\"ok\")" },
        { "Temporary", "func make() -> (string, i32) => (\"kept\", 42)\nif make().1 == 42 => writeLine(\"ok\")" },
        { "Selection", "let a = (1, 42)\nlet b = (3, 4)\nif (if true => a else => b).1 == 42 => writeLine(\"ok\")" },
        { "CopyAggregate", "let a = ((1, 42), \"kept\")\nlet b = a.0\nif b.1 + a.0.0 == 43 => writeLine(\"ok\")" },
        { "Snapshot", "var a: [1 of i32] = [40]\nlet n = a[0] + (work: do\n    a = [2]\n    exit to work: a[0]\n)\nif n == 42 => writeLine(\"ok\")" },
        { "Once", "func make() -> [2 of i32]\n    writeLine(\"ok\")\n    return [1, 42]\nfunc index() -> isize => 1\nlet n = make()[index()]" },
        { "Loop", "let a: [3 of i32] = [10, 20, 12]\nvar i: isize = 0\nvar sum = 0\nwhile i < 3\n    sum += a[i]\n    i += 1\nif sum == 42 => writeLine(\"ok\")" },
        { "Unit", "let a: [2 of ()] = [(), ()]\nlet u = a[1]\nwriteLine(\"ok\")" },
        { "ShortCircuit", "let a: [0 of i32] = []\nif false and a[0] == 0 => writeLine(\"bad\")\nwriteLine(\"ok\")" },
        { "Transfer", "func f() -> i32\n    let a: [1 of i32] = [1]\n    return a[(return 42)]\nif f() == 42 => writeLine(\"ok\")" },
        { "Widths", "let a: (i8, u8, i128, f64, char) = (-7, 200, -170141183460469231731687303715884105728, 2.5, '😀')\nif a.0 == -7 and a.1 == 200 and a.2 < 0 and a.3 == 2.5 and a.4 == '😀' => writeLine(\"ok\")" },
        { "TupleCopyReturn", "func get(p: ((i32, bool), string)) -> (i32, bool) => p.0\nlet p = ((42, true), \"held\")\nlet r = get(p)\nif r.0 == 42 and r.1 => writeLine(\"ok\")" },
        { "IndexConversion", "let a: [1 of i32] = [42]\nlet i: u8 = 0\nif a[i@isize] == 42 => writeLine(\"ok\")" },
        { "IndexSelection", "let a: [2 of i32] = [1, 42]\nif a[if true => 1 else => 0] == 42 => writeLine(\"ok\")" },
        { "IndexRead", "let a: [2 of i32] = [1, 42]\nlet indices: [1 of isize] = [1]\nif a[indices[0]] == 42 => writeLine(\"ok\")" },
        { "ReceiverRead", "let a: [2 of isize] = [1, 42]\nif a[a[0]] == 42 => writeLine(\"ok\")" },
        { "BooleanArray", "let a: [3 of bool] = [false, true, false]\nif a[1] and not a[2] => writeLine(\"ok\")" },
        { "CheckedPhi", "let a: [1 of i32] = [42]\nlet n = if true => a[0] else => 1\nif n == 42 => writeLine(\"ok\")" },
        { "Deferred", "let a: [2 of i32] = [0, 42]\nvar i = 0\nloop\n    defer => if a[1] != 42 => writeLine(\"bad\")\n    i += 1\n    if i < 3 => continue\n    exit\nwriteLine(\"ok\")" },
        { "Dead", "func f()\n    return\n    let a: [1 of i32] = [42]\n    a[0]\nf()\nwriteLine(\"ok\")" },
        { "DeadArm", "let a: [1 of i32] = [42]\nlet n = match true\n    _ => a[0]\n    true => a[1]\nif n == 42 => writeLine(\"ok\")" },
        { "UnitTuple", "let a = ((), 42)\nlet u = a.0\nif a.1 == 42 => writeLine(\"ok\")" },
        { "EmptyCopy", "let a: ([0 of i32], i32) = ([], 42)\nlet b = a.0\nlet c = a.0\nif a.1 == 42 => writeLine(\"ok\")" },
        { "LoopCopy", "let a = ((1, 42), 0)\nvar i = 0\nwhile i < 3\n    let b = a.0\n    if b.1 != 42 => writeLine(\"bad\")\n    i += 1\nwriteLine(\"ok\")" },
        { "TransferCleanup", "func f() -> i32\n    var a: [1 of i32] = [1]\n    defer => a = [2]\n    return a[(return 42)]\nif f() == 42 => writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CopyReadsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("Element" + name, source, "ok\n");

    [Theory]
    [InlineData("Negative", "-1", 2)]
    [InlineData("Length", "2", 2)]
    [InlineData("Large", "9223372036854775807", 2)]
    [InlineData("Empty", "0", 0)]
    public void InvalidIndicesAbort(string name, string index, int length)
    {
        var source = $"let a: [{length} of i32] = " + (length == 0 ? "[]" : "[1, 2]") + $"\nlet n = a[{index}]\nwriteLine(\"bad\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementBounds" + name, source, string.Empty, 1, "Hello.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
        Assert.Contains("icmp uge i64", ir);
        Assert.DoesNotContain("getelementptr inbounds", ir.Split("define internal void @__kimi_body").Last());
    }

    [Theory]
    [InlineData("var a: [1 of i32] = [1]\nlet n = a[(work: do\n    a = [2]\n    exit to work: 0\n)]")]
    [InlineData("func take(a: (string, [1 of i32])) => ()\nlet a: (string, [1 of i32]) = (\"a\", [1])\nlet n = a.1[(work: do\n    take(a)\n    exit to work: 0\n)]")]
    [InlineData("var a: [1 of i32] = [1]\nlet n = a[(work: do\n    defer => a = [2]\n    exit to work: 0\n)]")]
    public void ReceiverCannotChangeDuringIndexEvaluation(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("let a = (1, 2)\nlet n = a.2")]
    [InlineData("let a: [1 of i32] = [1]\nlet i: i32 = 0\nlet n = a[i]")]
    [InlineData("let a = (\"a\", 1)\nlet text = a.0")]
    [InlineData("var a: [1 of f64] = [1.0]\na[0] %= 2.0")]
    [InlineData("let a: ([0 of string], i32) = ([], 1)\nlet n = a.0")]
    [InlineData("let a: [1 of string] = [\"a\"]\nlet i: isize = 0\nlet s = a[i]")]
    public void UnsupportedOrInvalidAccessProducesNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("loan")]
    [InlineData("root")]
    [InlineData("index")]
    [InlineData("output")]
    [InlineData("parent")]
    [InlineData("active")]
    [InlineData("source")]
    public void MalformedPlansFailBeforeWritingAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let a: [1 of i32] = [42]\nlet n = a[0]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        body.Projections[0] = defect switch
        {
            "loan" => plan with { Loan = -1 },
            "root" => plan with { Root = -1 },
            "index" => plan with { Index = -1 },
            "output" => plan with { Output = -1 },
            "parent" => plan with { Parent = 0 },
            _ => plan,
        };
        if (defect == "active")
        {
            body.LoanInputs[plan.Operation] = -1;
        }
        else if (defect == "source")
        {
            body.OperationStorage[plan.Index] = body.Operations[plan.Index] with { Source = body.Operations[plan.Operation].Source };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void IntermediateProjectionsDoNotCopyAndTheFinalAggregateCopiesOnce()
    {
        const string Source = "let a = (((42, true), 3), \"held\")\nlet b = a.0.0\nlet n = a.0.0.0";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        Assert.Equal(5, body.Projections.Count);
        Assert.Equal(2, body.Projections.Count(x => x.Output >= 0));
        var instructions = module.GetFunction(0).Instructions;
        Assert.Single(instructions, x => x.Opcode == EmissionOpcode.TransferAggregate && x.OperandCount == 1);
        Assert.Single(instructions, x => x.Opcode == EmissionOpcode.LoadElement);
        Assert.Equal(5, instructions.Count(x => x.Opcode == EmissionOpcode.ElementAddress));
    }

    [Fact]
    public void TemporaryReceiverIsDestroyedAfterCopyBeforeConditionBranch()
    {
        const string Source = "func make() -> (string, i32) => (\"held\", 42)\nif make().1 == 42 => writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementLifetime", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementLifetime", Source, ir, "ok\n", "held=1;ok=1", order: [0, 1]);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var instructions = module.GetFunction(0).Instructions;
        var load = instructions.FindIndex(x => x.Opcode == EmissionOpcode.LoadElement);
        var cleanup = instructions.FindIndex(x => x.Opcode == EmissionOpcode.DestroyAggregate);
        var branch = instructions.FindIndex(x => x.Opcode == EmissionOpcode.ConditionalBranch);
        Assert.True(load >= 0 && cleanup > load && branch > cleanup);
    }

    [Fact]
    public void BoundsAbortDoesNotDestroyTheReceiverOrStartDeferredCleanup()
    {
        const string Source = "func make() -> (string, [0 of i32]) => (\"held\", [])\ndefer => writeLine(\"bad\")\nlet n = make().1[0]";
        const string Stderr = "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementAbortedLifetime", Source, string.Empty, 1, Stderr);
        StringEmissionTest.WriteAuditedFixture("ElementAbortedLifetime", Source, ir, string.Empty, "held=0;bad=0", 1, Stderr);
    }

    [Theory]
    [InlineData("NestedEmpty", "let a: [1 of [0 of i32]] = [[]]\nlet n = a[0][0]", "Hello.kimi:2:9")]
    [InlineData("FirstCheck", "func index() -> isize\n    writeLine(\"bad\")\n    return 0\nlet a: [0 of [1 of i32]] = []\nlet n = a[0][index()]", "Hello.kimi:5:9")]
    [InlineData("TypedNegative", "let a: [1 of i32] = [42]\nlet i: isize = -1\nlet n = a[i]", "Hello.kimi:3:9")]
    [InlineData("UnitBounds", "let a: [1 of ()] = [()]\nlet u = a[1]", "Hello.kimi:2:9")]
    public void BoundsChecksPrecedeLaterIndicesAndRemainForZeroByteValues(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("ElementBounds" + name, source, string.Empty, 1, location + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void WarmElementPreparationAllocatesNothing()
    {
        const string Source = "func get(a: [2 of i32], i: isize) -> i32 => a[i]\nlet a: [2 of i32] = [1, 42]\nvar n = 0\nloop\n    defer => get(a, 1)\n    n += 1\n    if n < 3 => continue\n    exit\nlet p = ((42, true), \"held\")\nlet q = p.0\nif q.1 => get(a, 0)";
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
