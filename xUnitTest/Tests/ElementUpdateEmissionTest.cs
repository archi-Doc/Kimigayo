// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ElementUpdateEmissionTest
{
    public static TheoryData<string, string> Fixtures => new()
    {
        { "Compound", "var a: [1 of i32] = [10]\nlet done: () = (a[0] += 32)\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "Increment", "var a = (42, true)\nlet before = a.0++\nlet after = --a.0\nlet next = ++a.0\nlet last = a.0--\nif before == 42 and after == 42 and next == 43 and last == 43 and a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Mixed", "var a: (string, [2 of i32]) = (\"held\", [40, 2])\nlet amount = a.1[1]\na.1[0] += amount\nif a.1[0] == 42 => Console.writeLine(\"ok\")" },
        { "Nested", "var a: [1 of [1 of i32]] = [[40]]\na[0][0] += 2\nif a[0][0] == 42 => Console.writeLine(\"ok\")" },
        { "SelfRead", "var a: [1 of i32] = [21]\na[0] += a[0]\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "SelfReadSnapshot", "var a: [1 of i32] = [21]\nlet amount = a[0]\na[0] += amount\nif a[0] == 42 => Console.writeLine(\"ok\")" },
        { "RhsUpdate", "var a = (40, true)\nvar b = (2, false)\na.0 += b.0++\nif a.0 == 42 and b.0 == 3 => Console.writeLine(\"ok\")" },
        { "IndexUpdate", "var a: [2 of i32] = [40, 0]\nvar i: [1 of isize] = [0]\na[i[0]++] += 2\nif a[0] == 42 and i[0] == 1 => Console.writeLine(\"ok\")" },
        { "IndexSnapshot", "var a: [2 of i32] = [40, 0]\nvar i: isize = 0\na[i++] += 2\nif a[0] == 42 and a[1] == 0 and i == 1 => Console.writeLine(\"ok\")" },
        { "RhsSelection", "var a = (40, true)\nlet flag = a.1\na.0 += if flag => 2 else => 0\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "RhsDo", "var a = (40, true)\na.0 += work: do\n    exit to work: 2\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Deferred", "var a = (0, \"held\")\nvar i = 0\nloop\n    defer => a.0 += 14\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "DeferredIncrement", "var a = (39, \"held\")\nvar i = 0\nloop\n    defer => ++a.0\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Function", "func f(input: [1 of i32]) -> i32\n    var a = input\n    a[0] += 2\n    return a[0]\nlet input: [1 of i32] = [40]\nif f(input) == 42 and input[0] == 40 => Console.writeLine(\"ok\")" },
        { "Dead", "func f()\n    return\n    var a = (40, true)\n    a.0 += 2\n    a.0++\nf()\nConsole.writeLine(\"ok\")" },
        { "Covered", "var a = (40, true)\nmatch true\n    _ => a.0 += 2\n    true => a.0 -= 1\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "RhsTransfer", "func f() -> i32\n    var a = (0, true)\n    defer => a.0++\n    a.0 += (return 42)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "IndexTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[(return 42)] += 2\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "IncrementTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    ++a[(return 42)]\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "ShiftTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[0] <<= (return 42)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "TransferPriority", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[(return 42)] += (return 1)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "MixedShift", "var a: [1 of i8] = [-128]\nlet wide: u128 = 7\na[0] >>= wide\nvar b: [1 of u64] = [1]\nlet narrow: i8 = 63\nb[0] <<= narrow\nif a[0] == -1 and b[0] == 9223372036854775808 => Console.writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void UpdatesExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementUpdate" + name, source, "ok\n");

    [Theory]
    [InlineData("i8")]
    [InlineData("u8")]
    [InlineData("i16")]
    [InlineData("u16")]
    [InlineData("i32")]
    [InlineData("u32")]
    [InlineData("i64")]
    [InlineData("u64")]
    [InlineData("i128")]
    [InlineData("u128")]
    [InlineData("isize")]
    [InlineData("usize")]
    public void IntegerUpdatesPreserveEveryWidth(string type)
    {
        var source = $"var a: [1 of {type}] = [10]\na[0] += 11\na[0] *= 2\na[0] -= 2\na[0] |= 3\na[0] &= 63\na[0] ^= 1\na[0] <<= 1\na[0] >>= 1\nlet old = a[0]++\nlet now = --a[0]\nif old == 42 and now == 42 and a[0] == 42 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementUpdate" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("Divide", "/=", "84", "2", "42")]
    [InlineData("Remainder", "%=", "85", "43", "42")]
    [InlineData("NegativeDivide", "/=", "-85", "2", "-42")]
    [InlineData("NegativeRemainder", "%=", "-85", "43", "-42")]
    public void DivisionAndRemainderUseCheckedIntegerRules(string name, string op, string initial, string rhs, string expected)
        => ScalarEmissionTest.EmitFixture("ElementUpdate" + name, $"var a: [1 of i64] = [{initial}]\na[0] {op} {rhs}\nif a[0] == {expected} => Console.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void FloatUpdatesUseIeeeArithmetic(string type)
    {
        var source = $"var a: [1 of {type}] = [10.0]\na[0] += 11.0\na[0] *= 4.0\na[0] /= 2.0\na[0] -= 0.0\nif a[0] == 42.0 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementUpdate" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void FloatSpecialValuesSurviveUpdates(string type)
    {
        var source = $"var a: [3 of {type}] = [0.0, 1.0, -0.0]\na[0] /= 0.0\na[1] /= 0.0\na[2] *= 2.0\nif a[0] != a[0] and a[1] > 1.0 and a[1] - a[1] != 0.0 and 1.0 / a[2] < 0.0 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementUpdateSpecial" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("Add", "i8", "127", "a[0] += 1", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("Subtract", "u8", "0", "a[0] -= 1", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("Multiply", "i128", "170141183460469231731687303715884105727", "a[0] *= 2", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("PostIncrement", "u128", "340282366920938463463374607431768211455", "a[0]++", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("PreIncrement", "i8", "127", "++a[0]", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("PostDecrement", "i8", "-128", "a[0]--", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("PreDecrement", "u8", "0", "--a[0]", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("DivideZero", "i64", "42", "a[0] /= 0", "INT_DIV_ZERO", "Integer division or remainder by zero")]
    [InlineData("RemainderZero", "u64", "42", "a[0] %= 0", "INT_DIV_ZERO", "Integer division or remainder by zero")]
    [InlineData("DivideMinimum", "i64", "-9223372036854775808", "a[0] /= -1", "INT_OVERFLOW", "Integer overflow")]
    [InlineData("RemainderMinimum", "i64", "-9223372036854775808", "a[0] %= -1", "INT_OVERFLOW", "Integer overflow")]
    public void ArithmeticFailurePreventsStoreResultAndCleanup(string name, string type, string initial, string expression, string code, string reason)
    {
        var source = $"var a: [1 of {type}] = [{initial}]\ndefer => Console.writeLine(\"bad\")\n{expression}\nConsole.writeLine(\"bad\")";
        ScalarEmissionTest.EmitFixture("ElementUpdateAbort" + name, source, string.Empty, 1, $"Hello.kimi:3:1: abort KIMI_E_{code}: {reason}\n");
    }

    [Theory]
    [InlineData("u128", "256")]
    [InlineData("u128", "340282366920938463463374607431768211455")]
    [InlineData("i8", "-1")]
    [InlineData("u8", "8")]
    public void ShiftCountIsCheckedBeforeNarrowing(string type, string count)
        => ScalarEmissionTest.EmitFixture("ElementUpdateShiftFail" + type + count, $"var a: [1 of u8] = [1]\nlet n: {type} = {count}\na[0] <<= n", string.Empty, 1, "Hello.kimi:3:1: abort KIMI_E_INT_SHIFT_COUNT: Shift count out of range\n");

    [Theory]
    [InlineData("Compound", "a[0] += rhs()")]
    [InlineData("Increment", "++a[0]")]
    public void BoundsFailurePrecedesTheRightSideAndCleanup(string name, string expression)
    {
        var source = $"func rhs() -> i32\n    Console.writeLine(\"bad\")\n    return 1\nvar a: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\n{expression}";
        var column = name == "Increment" ? 3 : 1;
        ScalarEmissionTest.EmitFixture("ElementUpdateBounds" + name, source, string.Empty, 1, $"Hello.kimi:6:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    // SPEC 13.7.2: the RHS, including its cleanup, completes before the element is located, read and written.
    [Theory]
    [InlineData("var a: [1 of i32] = [0]\na[0] += (work: do\n    a = [1]\n    exit to work: 42\n)")]
    [InlineData("var a: [1 of i32] = [0]\na[0] += a[0]++")]
    [InlineData("var a: [2 of i32] = [0, 0]\nlet i: isize = 1\na[0] += ++a[i]")]
    [InlineData("var a: [1 of i32] = [0]\na[0] += (work: do\n    defer => a[0] = 1\n    exit to work: 42\n)")]
    public void RightSideEffectsPrecedeTheAccessProtection(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    // The located receiver stays protected while its index operands evaluate (SPEC 4.6.4).
    [Theory]
    [InlineData("var a: [1 of i32] = [0]\na[(work: do\n    a[0]++\n    exit to work: 0\n)]++")]
    [InlineData("var a: [1 of i32] = [21]\nlet x = a[(work: do\n    a[0]++\n    exit to work: 0\n)]")]
    public void IndexOperandsCannotMutateTheProtectedReceiver(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void TargetMovedByTheRightSideIsRejected()
    {
        var c = MinimalEmissionTest.Analyze("func take(a: (string, i32)) => ()\nvar a = (\"held\", 0)\na.1 += (work: do\n    take(a@move)\n    exit to work: 42\n)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("Rhs", "a.1[0] += work: do\n    defer => loop => ()\n    exit to work: 42")]
    [InlineData("Index", "a.1[work: do\n    defer => loop => ()\n    exit to work: 0\n]++")]
    public void NonterminatingCleanupPreventsTheStore(string name, string expression)
    {
        var source = "var a: (string, [1 of i32]) = (\"held\", [0])\n" + expression;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode is EmissionOpcode.StoreElement or EmissionOpcode.DestroyAggregate);
        ScalarEmissionTest.EmitFixture("ElementUpdateDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);
    }

    [Fact]
    public void UpdatesPreserveParentDestructionResponsibility()
    {
        const string Source = "var a = (\"first\", 40, \"last\")\na.1 += 1\nlet previous = a.1++\nif previous == 41 and a.1 == 42 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementUpdateLifetime", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementUpdateLifetime", Source, ir, "ok\n", "first=1;last=1;ok=1", order: [2, 1, 0]);
    }

    [Fact]
    public void AbortedUpdateDoesNotDestroyTheParent()
    {
        const string Source = "var a: (string, u8) = (\"held\", 255)\ndefer => Console.writeLine(\"bad\")\na.1++";
        const string Error = "Hello.kimi:3:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementUpdateAbortLifetime", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementUpdateAbortLifetime", Source, ir, string.Empty, "held=0;bad=0", 1, Error);
    }

    [Theory]
    [InlineData("var a: [1 of i32] = [21]\na[0] += a[0]")]
    [InlineData("var a: [1 of i32] = [21]\na[0] += (a[0])")]
    [InlineData("var a: [1 of i32] = [21]\nvar i: isize = 0\na[i] += a[0]")]
    [InlineData("var a = (21, true)\na.0 += a.0")]
    [InlineData("var a: [1 of [1 of i32]] = [[21]]\na[0][0] += a[0][0]")]
    [InlineData("var a: [2 of i32] = [21, 21]\na[0] += a[1 + 0]")]
    [InlineData("func count(a: [1 of i32]) -> i32 => a[0]\nvar a: [1 of i32] = [21]\na[0] += count(a)")]
    [InlineData("var a: [1 of i32] = [21]\na[0] += (work: do\n    defer => a[0]\n    exit to work: 21\n)")]
    [InlineData("func f()\n    return\n    var a: [1 of i32] = [21]\n    a[0] += a[0]\nf()")]
    public void SelfReadsInTheRightSidePrecedeTheExclusiveUpdate(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("IndexRead", "var a: [2 of isize] = [1, 41]\na[a[0]] += 1\nif a[1] == 42 => Console.writeLine(\"ok\")")]
    [InlineData("TransferReadCleanup", "func f() -> i32\n    var a: [1 of i32] = [42]\n    defer\n        if a[0] == 42 => Console.writeLine(\"ok\")\n    a[0] += (return 0)\n    return 1\nf()")]
    [InlineData("SimpleSelfRead", "var a: [1 of i32] = [21]\na[0] = a[0] + 21\nif a[0] == 42 => Console.writeLine(\"ok\")")]
    public void ProtectionStartsAfterIndicesAndEndsOnTransfer(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementUpdateLoan" + name, source, "ok\n");

    [Theory]
    [InlineData("update")]
    [InlineData("output")]
    [InlineData("write")]
    [InlineData("owner")]
    [InlineData("duplicate")]
    [InlineData("right")]
    [InlineData("computation")]
    [InlineData("result")]
    [InlineData("operator")]
    [InlineData("constant")]
    [InlineData("postfix")]
    [InlineData("release")]
    [InlineData("exclusive")]
    [InlineData("mode")]
    [InlineData("anchor")]
    [InlineData("parentLoan")]
    public void InvalidUpdatePlansFailBeforeWritingAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var a: [1 of i32] = [40]\nlet old = a[0]++");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var update = Assert.Single(body.ElementUpdates);
        var plan = body.Projections[update.Projection];
        switch (defect)
        {
            case "update": body.Projections[update.Projection] = plan with { Update = -1 }; break;
            case "output": body.Projections[update.Projection] = plan with { Output = -1 }; break;
            case "write": body.Projections[update.Projection] = plan with { Write = -1 }; break;
            case "owner": body.ElementUpdates[plan.Update] = update with { Projection = int.MaxValue }; break;
            case "duplicate": body.ElementUpdates.Add(update); break;
            case "right": body.ElementUpdates[plan.Update] = update with { Right = int.MaxValue }; break;
            case "computation": body.ElementUpdates[plan.Update] = update with { Computation = plan.Output }; break;
            case "result": body.ElementUpdates[plan.Update] = update with { Result = plan.Output }; break;
            case "operator": body.Values[update.Computation] = body.Values[update.Computation] with { Operator = KotoKind.Minus }; break;
            case "constant": body.Values[update.Right] = body.Values[update.Right] with { Constant = 2 }; break;
            case "postfix": body.ValueOperands[body.Values[update.Result].Start] = update.Computation; break;
            case "release": body.LoanStates[plan.Write - 1] = plan.Loan; break;
            case "exclusive": body.Projections[update.Projection] = plan with { Exclusive = -1 }; break;
            case "mode": body.ComparisonLoans[plan.Exclusive] = body.ComparisonLoans[plan.Exclusive] with { Mode = LoanRequirement.Ref }; break;
            case "anchor": body.ComparisonLoans[plan.Exclusive] = body.ComparisonLoans[plan.Exclusive] with { Projection = int.MaxValue }; break;
            case "parentLoan": body.ComparisonLoans[plan.Exclusive] = body.ComparisonLoans[plan.Exclusive] with { Parent = plan.Loan }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmElementUpdatesAllocateNothing()
    {
        const string Source = "var a: (string, [2 of i32]) = (\"held\", [0, 2])\nvar b: [1 of isize] = [0]\nvar i = 0\nloop\n    defer => a.1[0]++\n    let amount = a.1[1]\n    a.1[b[0]++] += if true => amount else => 0\n    --b[0]\n    i += 1\n    if i < 3 => continue\n    exit";
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

    [Theory]
    [InlineData("let a = (1, true)\na.0 += 2")]
    [InlineData("let a = (1, true)\na.0++")]
    [InlineData("var a: [1 of i32]\na[0]++")]
    [InlineData("var a = (\"held\", 1)\nlet b = a\na.1 += 2")]
    [InlineData("var a = (1.0, true)\na.0++")]
    [InlineData("var a = ('a', true)\na.0++")]
    [InlineData("var a = (true, 1)\na.0 &= false")]
    [InlineData("var a = ((), 1)\na.0 += ()")]
    [InlineData("var a = ((1, 2), true)\na.0 += (3, 4)")]
    [InlineData("var a = (\"a\", true)\na.0 += \"b\"")]
    [InlineData("var a: [1 of i128] = [42]\na[0] /= 2")]
    [InlineData("var a: [1 of u128] = [42]\na[0] %= 2")]
    [InlineData("func f(a: [1 of i32])\n    a[0]++")]
    [InlineData("func f() -> [1 of i32] => [0]\nf()[0]++")]
    [InlineData("func f()\n    return\n    var a: [1 of i32]\n    a[0] += 2")]
    public void InvalidUpdatesProduceNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RightSidePrecedesLocationReadAndStore()
    {
        const string Source = "func index() -> isize\n    Console.writeLine(\"index\")\n    return 0\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\nvar a: [1 of i32] = [40]\na[index()] += rhs()\nif a[0] == 42 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementUpdateOrder", Source, "rhs\nindex\nok\n");
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var update = Assert.Single(body.ElementUpdates);
        var plan = body.Projections[update.Projection];
        Assert.True(update.Right < plan.Operation && plan.Operation < plan.Output && plan.Output < update.Computation && update.Computation < plan.Write);
        Assert.Equal(LoanRequirement.Ref, body.ComparisonLoans[plan.Loan].Mode);
        Assert.Equal(LoanRequirement.Uniq, body.ComparisonLoans[plan.Exclusive].Mode);
        Assert.Equal(plan.Loan, body.LoanInputs[plan.Operation]);
        Assert.Equal(plan.Exclusive, body.LoanStates[plan.Operation]);
        Assert.True(body.HasComparisonLoan(plan.Output, plan.Exclusive));
        Assert.True(body.HasComparisonLoan(update.Computation, plan.Exclusive));
        Assert.True(body.HasComparisonLoan(plan.Write, plan.Exclusive));
        Assert.False(body.HasComparisonLoan(update.Result, plan.Exclusive));
        var instructions = module.GetFunction(0).Instructions;
        Assert.Single(instructions, x => x.Opcode == EmissionOpcode.LoadElement && x.Operation == plan.Output);
        Assert.Single(instructions, x => x.Opcode == EmissionOpcode.StoreElement && x.Operation == plan.Write);
    }
}
