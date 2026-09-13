// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ReferenceEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";

    [Theory]
    [InlineData("Equal", Same + "let a = \"hello\"\nlet b = \"hello\"\nif same(a, b) => writeLine(\"ok\")\nwriteLine(a)\nwriteLine(b)", "ok\nhello\nhello\n")]
    [InlineData("Alias", Same + "let a = \"a\"\nif same(a, a) => writeLine(a)", "a\n")]
    [InlineData("Forward", Same + "func forward(x: ref/string, y: ref/string) -> bool => same(x, y)\nlet a = \"a\"\nif forward(a, a) => writeLine(a)", "a\n")]
    [InlineData("Unit", "func same(a: ref/string, unit: (), b: ref/string) -> bool => a == b\nlet a = \"a\"\nif same(b: a, unit: (), a: a) => writeLine(a)", "a\n")]
    [InlineData("OuterLoan", "func independent(x: ref/string) -> string\n    return \"a\"\nlet a = \"a\"\nif a == independent(a) => writeLine(a)", "a\n")]
    [InlineData("Nested", Same + "func test(a: ref/string, flag: bool) -> bool => flag\nlet a = \"a\"\nif test(a, same(a, a)) => writeLine(a)", "a\n")]
    [InlineData("Return", "func test(a: ref/string, flag: bool) -> bool => flag\nfunc f() -> string\n    let a = \"held\"\n    test(a, (return \"ok\"))\n    return \"bad\"\nwriteLine(f())", "ok\n")]
    [InlineData("Continue", "func test(a: ref/string, flag: bool) -> bool => flag\nlet a = \"a\"\nvar n = 0\nwhile n < 2\n    n += 1\n    test(a, (if n == 1 => continue else => true))\nwriteLine(a)", "a\n")]
    [InlineData("Deferred", Same + "let a = \"a\"\ndefer => if same(a, a) => writeLine(\"done\")\nif same(a, a) => writeLine(\"ok\")", "ok\ndone\n")]
    [InlineData("Recursive", "func again(a: ref/string, count: i32) -> bool\n    if count == 0 => return a == a\n    return again(a, count - 1)\nlet a = \"a\"\nif again(a, 3) => writeLine(a)", "a\n")]
    [InlineData("Guard", Same + "let a = \"a\"\nmatch 1\n    _ if same(a, a) => writeLine(a)\n    _ => ()", "a\n")]
    [InlineData("ShortCircuit", Same + "let a = \"a\"\nif false and same(a, a) => ()\nif true or same(a, a) => writeLine(a)", "a\n")]
    [InlineData("OwnedParameter", Same + "func print(a: string)\n    if same(a, a) => writeLine(a)\nprint(\"a\")", "a\n")]
    [InlineData("ReturnCleanup", "func inspect(a: ref/string) -> string\n    defer => if a == a => writeLine(\"defer\")\n    return \"result\"\nlet a = \"a\"\nwriteLine(inspect(a))\nwriteLine(a)", "defer\nresult\na\n")]
    [InlineData("AbandonedCleanup", "func test(a: ref/string, flag: bool) -> bool => flag\nfunc run() -> string\n    let a = \"held\"\n    defer => writeLine(a)\n    test(a, (return \"ok\"))\n    return \"bad\"\nwriteLine(run())", "held\nok\n")]
    [InlineData("ScalarParameter", "func value(x: i32, flag: bool) -> i32\n    if flag => return x\n    return x\nif value(7, false) == 7 => writeLine(\"ok\")", "ok\n")]
    [InlineData("CoveredGuard", Same + "let a = \"a\"\nmatch 1\n    _ => writeLine(\"ok\")\n    _ if same(a, a) => ()\nwriteLine(a)", "ok\na\n")]
    public void Execute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture("Reference" + name, source, expected);

    [Theory]
    [InlineData("Literal", "\"a\"")]
    [InlineData("Call", "echo(\"a\")")]
    [InlineData("If", "(if true => \"a\" else => \"b\")")]
    [InlineData("Do", "(work: do\n    exit to work: \"a\"\n)")]
    [InlineData("Loop", "(loop\n    exit \"a\"\n)")]
    [InlineData("Match", "(match 1\n    1 => \"a\"\n    _ => \"b\"\n)")]
    public void TemporaryBorrowUsesAcquiredStorage(string name, string expression)
    {
        var source = Same + "func echo(a: string) -> string => a\nlet a = \"a\"\nif same(" + expression + ", a) => writeLine(\"ok\")\nwriteLine(a)";
        ScalarEmissionTest.EmitFixture("ReferenceTemporary" + name, source, "ok\na\n");
    }

    [Theory]
    [InlineData("ShortCircuit", "var flag = false\nif flag and same(\"a\", \"a\") => ()\nif true or same(\"b\", \"b\") => writeLine(\"ok\")", "ok\n", "a=0;b=0;ok=1")]
    [InlineData("Both", "if same(\"a\", \"a\") => writeLine(\"ok\")", "ok\n", "a=2;ok=1")]
    [InlineData("Transfer", "func test(a: ref/string, b: bool) -> bool => b\nfunc run() -> string\n    test(\"held\", (return \"ok\"))\n    return \"bad\"\nwriteLine(run())", "ok\n", "held=1;ok=1;bad=0")]
    [InlineData("EvaluationOrder", "func echo(s: string) -> string\n    writeLine(\"evaluate\")\n    return s\nif same(right: echo(\"b\"), left: echo(\"a\")) => ()", "evaluate\nevaluate\n", "a=1;b=1;evaluate=2")]
    public void TemporaryLoansPreserveExpressionCleanup(string name, string source, string stdout, string audit)
    {
        source = Same + source;
        // Short circuit may need conditional temporary-destruction flags.
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString();
        ScalarEmissionTest.WriteFixture("ReferenceTemporary" + name, ir, stdout);
        StringEmissionTest.WriteAuditedFixture("ReferenceTemporary" + name, source, ir, stdout, audit);
    }

    [Theory]
    [InlineData("==", "a", "a", true)]
    [InlineData("!=", "a", "b", true)]
    [InlineData("<", "a", "aa", true)]
    [InlineData("<=", "a", "a", true)]
    [InlineData(">", "日本", "a", true)]
    [InlineData(">=", "a", "b", false)]
    [InlineData("==", "", "", true)]
    [InlineData("==", "a\\0b", "a\\0c", false)]
    public void ReferentComparison(string op, string a, string b, bool result)
    {
        var source = $"func compare(a: ref/string, b: ref/string) -> bool => a {op} b\nlet a = \"{a}\"\nlet b = \"{b}\"\nif compare(a, b) => writeLine(\"true\") else => writeLine(\"false\")";
        ScalarEmissionTest.EmitFixture("ReferenceCompare" + op.Replace("=", "Eq").Replace("!", "Not").Replace("<", "Lt").Replace(">", "Gt") + (a.Length == 0 ? "Empty" : a.Contains('\\') ? "Nul" : string.Empty), source, result ? "true\n" : "false\n");
    }

    [Theory]
    [InlineData("func bad(a: ref/string) -> ref/string => a\n()")]
    [InlineData("func bad(a: ref/string)\n    let saved = a\n()")]
    [InlineData("func bad(a: ref/string, b: string) -> bool => a == b\n()")]
    [InlineData("func bad(a: ref/string)\n    writeLine(a)\n()")]
    [InlineData("let a = \"a\"\na@ref")]
    [InlineData("func unused(a: ref/string) => 1.0 + 2.0\n()")]
    public void RejectUnsupportedOrInvalidReferenceUses(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("test(left: a, right: take(a))", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("test(right: take(a), left: a)", OwnershipFailure.PossiblyMovedUse)]
    public void SourceOrderDeterminesTheConflict(string expression, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze("func test(left: ref/string, right: bool) -> bool => right\nfunc take(a: string) -> bool => true\nlet a = \"a\"\n" + expression);
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("same(a, take(a))")]
    [InlineData("same(a, (return a))")]
    public void OperandMoveStillConflicts(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Same + "func take(a: string) -> string => a\nfunc run() -> string\n    let a = \"a\"\n    " + expression + "\n    return \"bad\"\nwriteLine(run())");
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("a = \"replacement\"")]
    [InlineData("defer => writeLine(a)")]
    public void LaterArgumentEffectsSeeTheActiveLoan(string effect)
    {
        var source = "func test(a: ref/string, flag: bool) -> bool => flag\nvar a = \"a\"\ntest(a, (work: do\n    " + effect + "\n    exit to work: true\n))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("a == (return true)")]
    [InlineData("(return true) == a")]
    public void NoncompletingReferenceComparisonNeedsNoResult(string expression)
    {
        var source = "func test(a: ref/string) -> bool\n    " + expression + "\n    return false\nlet a = \"a\"\nif test(a) => writeLine(a)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        ScalarEmissionTest.EmitFixture("ReferenceAbruptComparison" + (expression.StartsWith('a') ? "Right" : "Left"), source, "a\n");
    }

    [Fact]
    public void AbandonedLoansAreNotRestoredInCheckingContinuations()
    {
        var source = "func test(a: ref/string, flag: bool) -> bool => flag\nfunc run() -> string\n    let a = \"held\"\n    test(a, (work: do\n        return \"ok\"\n        writeLine(a)\n        exit to work: true\n    ))\n    return \"bad\"\nwriteLine(run())";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        ScalarEmissionTest.EmitFixture("ReferenceUnreachableContinuation", source, "ok\n");
    }

    [Fact]
    public void BorrowedValuesNeedNoPhysicalCopyOrStorage()
    {
        const string Source = Same + "let a = \"a\"\nif same(a, a) => writeLine(a)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var function = module.GetFunction(0);
        Assert.Equal(2, body.Operations.Count(x => x.Kind == OwnershipOperationKind.Borrow));
        Assert.DoesNotContain(function.Slots, x => ReferenceTypes.IsString(body.Places[x.Place].Type));
        var borrowed = module.GetFunction(1);
        Assert.Empty(borrowed.Slots);
        Assert.DoesNotContain(borrowed.Instructions, x => x.Callee == WindowsLowering.DestroyString || x.Opcode == EmissionOpcode.MoveString);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out error), error);
        var ir = writer.ToString();
        Assert.Contains("ptr noundef nonnull align 8 dereferenceable(24) %a0", ir);
        Assert.Contains("ptr noundef nonnull align 8 dereferenceable(24) %p", ir);
        Assert.DoesNotContain("noalias", ir);
        var functionText = ir[ir.IndexOf("define internal i1 @__kimi_f0", StringComparison.Ordinal)..];
        Assert.DoesNotContain("readonly", functionText);
        StringEmissionTest.WriteAuditedFixture("ReferenceSingleDestruction", Source, ir, "a\n", "a=1");
    }

    [Fact]
    public void WarmBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "func forward(a: ref/string, b: ref/string) -> bool => same(a, b)\nlet a = \"a\"\nforward(a, a)");
        for (var i = 0; i < 100; i++)
        {
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
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

        var emissionBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, emissionBytes);
    }

    [Theory]
    [InlineData("missing_loan")]
    [InlineData("early_end")]
    [InlineData("exclusive")]
    [InlineData("source")]
    [InlineData("no_result_contract")]
    [InlineData("dependent_result")]
    [InlineData("end_position")]
    [InlineData("reference_value")]
    public void InvalidPlansNeverPublishIr(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Same + "let a = \"a\"\nsame(a, a)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var loan = body.ComparisonLoans[0];
        var call = body.CallLoans[0];
        var entry = body.Operations.Select((operation, index) => (operation, index)).First(x => x.operation.Kind == OwnershipOperationKind.CallEntry).index;
        switch (defect)
        {
            case "missing_loan": body.ComparisonLoans.Clear(); break;
            case "early_end": body.LoanInputs[call.Call] = -1; break;
            case "exclusive": body.OperationStorage[loan.Read] = body.Operations[loan.Read] with { LoanMode = LoanRequirement.Uniq }; break;
            case "source": body.OperationStorage[loan.Read] = body.Operations[loan.Read] with { Place = body.Operations[loan.Read].Input }; break;
            case "no_result_contract": body.CallLoans.Clear(); break;
            case "dependent_result": body.CallLoans[0] = call with { ResultRequirement = LoanRequirement.Ref }; break;
            case "end_position": body.CallLoans[0] = call with { End = call.Result }; break;
            case "reference_value": body.ValueOperands[body.Values[entry].Start] = -1; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void ReparseReusesThePhysicalAbi()
    {
        const string Source = Same + "let a = \"a\"\nsame(a, a)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var pool = new FunctionAbiPool();
        var abi = pool.Get(0, c.Ownership.Bodies[1].Function);
        var oldType = c.Ownership.Bodies[1].Function.Parameters[0].Type.BoundType;
        c = MinimalEmissionTest.Analyze(Source.Replace("same", "renamed", StringComparison.Ordinal));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.TryPrepare(out module, out error), error);
        Assert.NotSame(oldType, c.Ownership.Bodies[1].Function.Parameters[0].Type.BoundType);
        Assert.Same(abi, pool.Get(0, c.Ownership.Bodies[1].Function));
    }

    [Fact]
    public void PhysicalAbiPoolDoesNotRetainOriginBinders()
    {
        var pool = new FunctionAbiPool();
        var oldTree = Populate(pool);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(oldTree.IsAlive);
        GC.KeepAlive(pool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DivergentCallsKeepLoansAndDoNotUnwind(bool argument)
    {
        var source = argument
            ? "func test(a: ref/string, flag: bool) -> bool => flag\nfunc forever() -> bool\n    loop => ()\nlet a = \"held\"\ntest(a, forever())"
            : "func forever(a: ref/string) -> Never\n    loop => ()\nlet a = \"held\"\nforever(a)";
        var ir = ScalarEmissionTest.EmitFixture("ReferenceDivergent" + argument, source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Theory]
    [InlineData("Argument", "func test(a: ref/string, flag: i32) -> bool => true\nlet a = \"held\"\ntest(a, 2147483647 + 1)", 3, 9)]
    [InlineData("Body", "func test(a: ref/string) -> i32 => 2147483647 + 1\nlet a = \"held\"\ntest(a)", 1, 36)]
    public void AbortDoesNotDestroyBorrowedOwners(string name, string source, int line, int column)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ReferenceAbort" + name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture("ReferenceAbort" + name, source, ir, string.Empty, "held=0", 1, stderr);
    }

    [Fact]
    public void OverloadRankingRetainsExactOwnedAcquisition()
    {
        const string Source = "func pick(a: string) -> bool => true\nfunc pick(a: ref/string) -> bool => false\nif pick(\"a\") => writeLine(\"owned\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Empty(c.Binding.Issues);
        var call = c.Ownership.Bodies[0].Operations.First(x => x.Kind == OwnershipOperationKind.Call && x.Source is InvocationKoto { BoundCall.Target.Name: "pick" });
        Assert.Equal(ArgumentOperationKind.Value, ((InvocationKoto)call.Source).BoundCall!.ArgumentOperations[0].Kind);
        ScalarEmissionTest.EmitFixture("ReferenceOverload", Source, "owned\n");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Populate(FunctionAbiPool pool)
    {
        var c = MinimalEmissionTest.Analyze(Same + "()");
        var function = c.Ownership.Bodies[1].Function;
        pool.Get(0, function);
        return new WeakReference(function);
    }
}
