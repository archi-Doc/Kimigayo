// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ScalarEmissionTest
{
    internal const string Counter = "var count: i32 = 0\nwhile count < 3\n    writeLine(\"tick\")\n    count = count + 1\nif count == 3\n    writeLine(\"done\")\nelse\n    writeLine(\"unexpected\")";

    public static TheoryData<string, string, string, int> Fixtures => new()
    {
        { "Counter", Counter, "tick\ntick\ntick\ndone\n", 0 },
        { "Minimum", "var x: i32 = -2147483648\nif x < 0 => writeLine(\"ok\")", "ok\n", 0 },
        { "Short", "var x = 0\nif false and x++ > 0 => writeLine(\"bad\")\nif true or x++ > 0 => writeLine(\"ok\")\nif x == 0 => writeLine(\"zero\")", "ok\nzero\n", 0 },
        { "NestedShort", "var x = 1\nif (x == 1 and (x + 1 == 2 or false)) and true => writeLine(\"ok\")", "ok\n", 0 },
        { "Order", "var x = 2\nlet y = x + x++\nif y == 4 and x == 3 => writeLine(\"ok\")", "ok\n", 0 },
        { "LongLoop", "var x = 0\nwhile x < 1000000\n    x += 1\n    if x < 1000000 => continue\n    exit\n    writeLine(\"dead\")\nif x == 1000000 => writeLine(\"ok\")", "ok\n", 0 },
        { "ConditionalLocal", "var flag = true\nif flag\n    var x: i32\n    if flag => x = 2\nwriteLine(\"ok\")", "ok\n", 0 },
        { "OverflowAdd", "var x = 2147483647\nx = x + 1\nwriteLine(\"bad\")", string.Empty, 1 },
        { "OverflowSub", "var x = -2147483648\nx -= 1", string.Empty, 1 },
        { "OverflowMul", "var x = 50000\nx *= x", string.Empty, 1 },
        { "OverflowNeg", "var x = -2147483648\nx = -x", string.Empty, 1 },
        { "OverflowIncrement", "var x = 2147483647\nx++", string.Empty, 1 },
        { "SkippedOverflow", "var x = 2147483647\nif false and x + 1 > 0 => writeLine(\"bad\")\nwriteLine(\"ok\")", "ok\n", 0 },
        { "BooleanStorage", "var x = true\nlet y = x\nx = not y\nif x != y and y == true => writeLine(\"ok\")", "ok\n", 0 },
        { "Increments", "var x = 3\nlet a = --x\nlet b = x--\nlet c = ++x\nif a == 2 and b == 2 and c == 2 and x == 2 => writeLine(\"ok\")", "ok\n", 0 },
        { "Comparisons", "var x = -3\nlet y = +x\nif y <= -3 and y >= -3 and y != 0 and y > -4 => writeLine(\"ok\")", "ok\n", 0 },
        { "DeadArithmetic", "var x = 0\nwhile true\n    exit\n    x = 2147483647 + 1\nif x == 0 => writeLine(\"ok\")", "ok\n", 0 },
        { "NestedLoops", "var x = 0\nwhile x < 3\n    x += 1\n    var y = 0\n    while true\n        y += 1\n        if y == 2 => exit\n    if x == 2 => continue\n    writeLine(\"tick\")", "tick\ntick\n", 0 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsVerifiedScalarFixtures(string name, string source, string stdout, int exit)
        => EmitFixture(name, source, stdout, exit);

    [Theory]
    [InlineData("let x: u32 = 1")]
    [InlineData("let x: u32 = if true => 1 else => 2")]
    [InlineData("var x = 1\nx = x << 1")]
    [InlineData("if false\n    let x: u32 = 1")]
    [InlineData("while true\n    exit\n    let x: u32 = 1")]
    [InlineData("while true\n    continue\n    let x = 2 << 1")]
    [InlineData("if true and (\"x\" == \"x\") => writeLine(\"bad\")")]
    public void UnsupportedOperationsNeverWriteIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void WarmScalarAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Counter);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void PreoptimizationIrPreservesReadOrderChecksAndStorage()
    {
        var c = MinimalEmissionTest.Analyze("var x = 2\nlet y = x + x++\nif y == 4 => writeLine(\"ok\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        var loads = function.Instructions.Where(x => x.Opcode == EmissionOpcode.LoadScalar).ToArray();
        var adds = function.Instructions.Where(x => x.ScalarOperator == "sadd").ToArray();
        Assert.Equal(2, adds.Length);
        Assert.Equal(loads[0].Operation, function.GetOperands(adds[1])[0].Value);
        Assert.Equal(loads[1].Operation, function.GetOperands(adds[1])[1].Value);
        using var output = new StringWriter();
        module.WriteIr(output);
        var ir = output.ToString();
        var call = ir.IndexOf(" = call { i32, i1 } @llvm.sadd.with.overflow.i32", StringComparison.Ordinal);
        var branch = ir.IndexOf("  br i1 %overflow", call, StringComparison.Ordinal);
        var store = ir.IndexOf("  store i32", branch, StringComparison.Ordinal);
        Assert.True(call >= 0 && branch > call && store > branch);
        Assert.Contains("call void @__kimi_abort(i32 6", ir);
        Assert.DoesNotContain("nsw", ir);
        Assert.DoesNotContain("nuw", ir);
        var body = c.Ownership.Bodies[0];
        Assert.All(function.Slots, slot => Assert.True(body.Places[slot.Place].Kind == OwnershipPlaceKind.Local || ReferenceEquals(body.Places[slot.Place].Type, BoundType.String)));
    }

    [Theory]
    [InlineData("missing-value")]
    [InlineData("alias-cycle")]
    [InlineData("replacement")]
    [InlineData("phi-input")]
    public void IncompleteScalarPlansFailBeforeWriting(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("var x = 1\nif x > 0 and true => x = 2");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var write = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Write);
        if (mutation == "missing-value")
        {
            body.Values[write] = default;
        }
        else if (mutation == "alias-cycle")
        {
            body.ValueOperands[body.Values[write].Start] = write;
        }
        else if (mutation == "replacement")
        {
            body.OperationSteps[write] = -1;
        }
        else
        {
            var phi = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Phi);
            body.PhiInputs[body.Values[phi].Start] = body.PhiInputs[body.Values[phi].Start + 1];
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Equal(string.Empty, writer.ToString());
    }

    internal static string EmitFixture(string name, string source, string stdout, int exit = 0, string? stderr = null, int timeoutMilliseconds = 0)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString();
        // Only the failure-block Abort reason may use select; source control flow still branches.
        Assert.DoesNotMatch($@"select i1 (?!%zero\d+, i32 {WindowsLowering.IntegerDivisionZeroReason}, i32 {WindowsLowering.IntegerOverflowReason}\n)", ir[ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("store i1", ir);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/scalar-fixtures"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, name + ".ll"), ir);
        File.WriteAllText(Path.Combine(path, name + ".stdout"), stdout);
        File.WriteAllText(Path.Combine(path, name + ".exit"), exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(path, name + ".timeout"), timeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var column = name is "OverflowAdd" or "OverflowNeg" ? 5 : 1;
        File.WriteAllText(Path.Combine(path, name + ".stderr"), stderr ?? (exit == 0 ? string.Empty : $"Hello.kimi:2:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n"));
        return ir;
    }
}
