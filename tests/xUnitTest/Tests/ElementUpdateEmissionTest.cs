// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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
        { "RhsDo", "var a = (40, true)\na.0 += label work: do\n    exit to work 2\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Deferred", "var a = (0, \"held\")\nvar i = 0\nloop\n    defer => a.0 += 14\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "DeferredIncrement", "var a = (39, \"held\")\nvar i = 0\nloop\n    defer => ++a.0\n    i += 1\n    if i < 3 => continue\n    exit\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "Function", "func f(input: [1 of i32]) -> i32\n    var a = input\n    a[0] += 2\n    return a[0]\nlet input: [1 of i32] = [40]\nif f(input) == 42 and input[0] == 40 => Console.writeLine(\"ok\")" },
        { "Covered", "var a = (40, true)\nmatch true\n    _ => a.0 += 2\n    true => a.0 -= 1\nif a.0 == 42 => Console.writeLine(\"ok\")" },
        { "RhsTransfer", "func f() -> i32\n    var a = (0, true)\n    defer => a.0++\n    a.0 += (return 42)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "IndexTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[(return 42)] += 2\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "FieldTransfer", "struct S\n    public var n: i32 = 0\n    public init() => ()\nfunc make() -> i32 => 1\nfunc f() -> i32\n    var b: [1 of S] = [S.init()]\n    defer => if b[0].n != 0 => Console.writeLine(\"bad\")\n    b[(return 42)].n += make()\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "IncrementTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    ++a[(return 42)]\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "ShiftTransfer", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[0] <<= (return 42)\n    return 0\nif f() == 42 => Console.writeLine(\"ok\")" },
        { "TransferPriority", "func f() -> i32\n    var a: [1 of i32] = [0]\n    a[(return 42)] += (return 1)\n    return 0\nif f() == 1 => Console.writeLine(\"ok\")" }, // SPEC 13.7.2: the right-hand side transfers first.
        { "MixedShift", "var a: [1 of i8] = [-128]\nlet wide: u128 = 7\na[0] >>= wide\nvar b: [1 of u64] = [1]\nlet narrow: i8 = 63\nb[0] <<= narrow\nif a[0] == -1 and b[0] == 9223372036854775808 => Console.writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void UpdatesExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementUpdate" + name, source, "ok\n");

    // SPEC 8.7.4, 13.7: a generic integer is numeric in every instance, so a compound update of one through an Array element,
    // a followed reference or a fixed-array element verifies in the definition, as through a local or a borrowed Field, and each
    // instance updates its concrete Scalar.
    [Fact]
    public void GenericIntegerUpdatesThroughBorrowsExecute()
    {
        const string Source = """
            func bumpElement<T>(values: uniq/Array<T>, i: isize)
                T is PrimitiveInteger
                values[i] += 1
            func bumpReferent<T>(value: uniq/T)
                T is PrimitiveInteger
                value@follow += 2
            func bumpFixed<T>(values: uniq/[2 of T])
                T is PrimitiveInteger
                values[1] *= 3
            var values: Array<i32> = [1, 2]
            bumpElement(values@uniq, 1)
            var small: u8 = 250
            bumpReferent(small@uniq)
            var fixed: [2 of i64] = [1, 2]
            bumpFixed(fixed@uniq)
            require values[1] == 3 and small == 252 and fixed[1] == 6 else => $abort("bump")
            Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("ElementUpdateGenericInteger", Source, "ok\n");
    }

    // SPEC 13.3.1: a string destination has no compound update, through a raw pointer or as a local; Binding rejects it before
    // any update plan, so neither the analysis nor emission sees it.
    [Theory]
    [InlineData("func append(p: raw/string)\n    unsafe => *p += \"x\"")]
    [InlineData("func append()\n    var s = \"a\"\n    s += \"b\"")]
    public void AStringCompoundUpdateIsRejectedAtBinding(string function)
    {
        var c = MinimalEmissionTest.Analyze(function + "\npublic func main() => ()");
        Assert.False(c.Binding.Result.IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(Kimi.DiagnosticCode.NonNumericOperand_Kd, issue.Code);
        Assert.Contains("+=", issue.Node.ToString(), StringComparison.Ordinal);
        Assert.Empty(c.Problems);
    }

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
    [InlineData("RemainderMinimum", "%=", "-9223372036854775808", "-1", "0")] // SPEC 13.3.1: the remainder by -1 is 0.
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
    public void BoundsFailureFollowsTheRightSideAndPrecedesCleanup(string name, string expression)
    {
        // SPEC 13.7.2: the right-hand side runs before the element is located; Abort runs no cleanup.
        var source = $"func rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 1\nvar a: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\n{expression}";
        var column = name == "Increment" ? 3 : 1;
        ScalarEmissionTest.EmitFixture("ElementUpdateBounds" + name, source, name == "Compound" ? "rhs\n" : string.Empty, 1, $"Hello.kimi:6:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    // SPEC 13.7.2: the RHS, including its cleanup, completes before the element is located, read and written.
    [Theory]
    [InlineData("var a: [1 of i32] = [0]\na[0] += (label work: do\n    a = [1]\n    exit to work 42\n)")]
    [InlineData("var a: [1 of i32] = [0]\na[0] += a[0]++")]
    [InlineData("var a: [2 of i32] = [0, 0]\nlet i: isize = 1\na[0] += ++a[i]")]
    [InlineData("var a: [1 of i32] = [0]\na[0] += (label work: do\n    defer => a[0] = 1\n    exit to work 42\n)")]
    public void RightSideEffectsPrecedeTheAccessProtection(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, string.Join('\n', c.Problems));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // The located receiver stays protected while its index operands evaluate (SPEC 4.6.3).
    [Theory]
    [InlineData("var a: [1 of i32] = [0]\na[(label work: do\n    a[0]++\n    exit to work 0\n)]++")]
    [InlineData("var a: [1 of i32] = [21]\nlet x = a[(label work: do\n    a[0]++\n    exit to work 0\n)]")]
    public void IndexOperandsCannotMutateTheProtectedReceiver(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void TargetMovedByTheRightSideIsRejected()
    {
        var c = MinimalEmissionTest.Analyze("func take(a: (string, i32)) => ()\nvar a = (\"held\", 0)\na.1 += (label work: do\n    take(a@move)\n    exit to work 42\n)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.MovedPlace_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("Rhs", "a.1[0] += label work: do\n    defer => loop => ()\n    exit to work 42")]
    [InlineData("Index", "a.1[label work: do\n    defer => loop => ()\n    exit to work 0\n]++")]
    public void NonterminatingCleanupPreventsTheStore(string name, string expression)
    {
        var source = "var a: (string, [1 of i32]) = (\"held\", [0])\n" + expression;
        ScalarEmissionTest.EmitFixture("ElementUpdateDivergent" + name, source, string.Empty, observationMilliseconds: 300);
    }

    [Fact]
    public void UpdatesPreserveParentDestructionResponsibility()
    {
        const string Source = "var a = (\"first\", 40, \"last\")\na.1 += 1\nlet previous = a.1++\nif previous == 41 and a.1 == 42 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementUpdateLifetime", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementUpdateLifetime", ir, "ok\n", "first=1;last=1;ok=1", order: [2, 1, 0]);
    }

    [Fact]
    public void AbortedUpdateDoesNotDestroyTheParent()
    {
        const string Source = "var a: (string, u8) = (\"held\", 255)\ndefer => Console.writeLine(\"bad\")\na.1++";
        const string Error = "Hello.kimi:3:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementUpdateAbortLifetime", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementUpdateAbortLifetime", ir, string.Empty, "held=0;bad=0", 1, Error);
    }

    [Theory]
    [InlineData("var a: [1 of i32] = [21]\na[0] += a[0]")]
    [InlineData("var a: [1 of i32] = [21]\na[0] += (a[0])")]
    [InlineData("var a: [1 of i32] = [21]\nvar i: isize = 0\na[i] += a[0]")]
    [InlineData("var a = (21, true)\na.0 += a.0")]
    [InlineData("var a: [1 of [1 of i32]] = [[21]]\na[0][0] += a[0][0]")]
    [InlineData("var a: [2 of i32] = [21, 21]\na[0] += a[1 + 0]")]
    [InlineData("func count(a: [1 of i32]) -> i32 => a[0]\nvar a: [1 of i32] = [21]\na[0] += count(a)")]
    [InlineData("var a: [1 of i32] = [21]\na[0] += (label work: do\n    defer => a[0]\n    exit to work 21\n)")]
    public void SelfReadsInTheRightSidePrecedeTheExclusiveUpdate(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, string.Join('\n', c.Problems));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("IndexRead", "var a: [2 of isize] = [1, 41]\na[a[0]] += 1\nif a[1] == 42 => Console.writeLine(\"ok\")")]
    [InlineData("TransferReadCleanup", "func f() -> i32\n    var a: [1 of i32] = [42]\n    defer\n        if a[0] == 42 => Console.writeLine(\"ok\")\n    a[0] += (return 0)\n    return 1\nf()")]
    [InlineData("SimpleSelfRead", "var a: [1 of i32] = [21]\na[0] = a[0] + 21\nif a[0] == 42 => Console.writeLine(\"ok\")")]
    public void ProtectionStartsAfterIndicesAndEndsOnTransfer(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementUpdateLoan" + name, source, "ok\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmElementUpdatesAllocateNothing()
    {
        const string Source = "var a: (string, [2 of i32]) = (\"held\", [0, 2])\nvar b: [1 of isize] = [0]\nvar i = 0\nloop\n    defer => a.1[0]++\n    let amount = a.1[1]\n    a.1[b[0]++] += if true => amount else => 0\n    --b[0]\n    i += 1\n    if i < 3 => continue\n    exit";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));

        var success = true;
        var bindingBytes = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.Generate(TextWriter.Null);
        });
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
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RightSidePrecedesLocationReadAndStore()
    {
        const string Source = "func index() -> isize\n    Console.writeLine(\"index\")\n    return 0\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\nvar a: [1 of i32] = [40]\na[index()] += rhs()\nif a[0] == 42 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ElementUpdateOrder", Source, "rhs\nindex\nok\n");
        var c = MinimalEmissionTest.Analyze(Source);
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
    }
}
