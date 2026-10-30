// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ReferenceEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";

    [Theory]
    [InlineData("Equal", Same + "let a = \"hello\"\nlet b = \"hello\"\nif same(a, b) => Console.writeLine(\"ok\")\nConsole.writeLine(a)\nConsole.writeLine(b)", "ok\nhello\nhello\n")]
    [InlineData("Alias", Same + "let a = \"a\"\nif same(a, a) => Console.writeLine(a)", "a\n")]
    [InlineData("Forward", Same + "func forward(x: ref/string, y: ref/string) -> bool => same(x, y)\nlet a = \"a\"\nif forward(a, a) => Console.writeLine(a)", "a\n")]
    [InlineData("Unit", "func same(a: ref/string, unit: (), b: ref/string) -> bool => a == b\nlet a = \"a\"\nif same(b: a, unit: (), a: a) => Console.writeLine(a)", "a\n")]
    [InlineData("OuterLoan", "func independent(x: ref/string) -> string\n    return \"a\"\nlet a = \"a\"\nif a == independent(a) => Console.writeLine(a)", "a\n")]
    [InlineData("Nested", Same + "func test(a: ref/string, flag: bool) -> bool => flag\nlet a = \"a\"\nif test(a, same(a, a)) => Console.writeLine(a)", "a\n")]
    [InlineData("Return", "func test(a: ref/string, flag: bool) -> bool => flag\nfunc f() -> string\n    let a = \"held\"\n    test(a, (return \"ok\"))\n    return \"bad\"\nConsole.writeLine(f())", "ok\n")]
    [InlineData("Continue", "func test(a: ref/string, flag: bool) -> bool => flag\nlet a = \"a\"\nvar n = 0\nwhile n < 2\n    n += 1\n    test(a, (if n == 1 => continue else => true))\nConsole.writeLine(a)", "a\n")]
    [InlineData("Deferred", Same + "let a = \"a\"\ndefer => if same(a, a) => Console.writeLine(\"done\")\nif same(a, a) => Console.writeLine(\"ok\")", "ok\ndone\n")]
    [InlineData("Recursive", "func again(a: ref/string, count: i32) -> bool\n    if count == 0 => return a == a\n    return again(a, count - 1)\nlet a = \"a\"\nif again(a, 3) => Console.writeLine(a)", "a\n")]
    [InlineData("Guard", Same + "let a = \"a\"\nmatch 1\n    _ if same(a, a) => Console.writeLine(a)\n    _ => ()", "a\n")]
    [InlineData("ShortCircuit", Same + "let a = \"a\"\nif false and same(a, a) => ()\nif true or same(a, a) => Console.writeLine(a)", "a\n")]
    [InlineData("OwnedParameter", Same + "func print(a: string)\n    if same(a, a) => Console.writeLine(a)\nprint(\"a\")", "a\n")]
    [InlineData("ReturnCleanup", "func inspect(a: ref/string) -> string\n    defer => if a == a => Console.writeLine(\"defer\")\n    return \"result\"\nlet a = \"a\"\nConsole.writeLine(inspect(a))\nConsole.writeLine(a)", "defer\nresult\na\n")]
    [InlineData("AbandonedCleanup", "func test(a: ref/string, flag: bool) -> bool => flag\nfunc run() -> string\n    let a = \"held\"\n    defer => Console.writeLine(a)\n    test(a, (return \"ok\"))\n    return \"bad\"\nConsole.writeLine(run())", "held\nok\n")]
    [InlineData("ScalarParameter", "func value(x: i32, flag: bool) -> i32\n    if flag => return x\n    return x\nif value(7, false) == 7 => Console.writeLine(\"ok\")", "ok\n")]
    [InlineData("CoveredGuard", Same + "let a = \"a\"\nmatch 1\n    _ => Console.writeLine(\"ok\")\n    _ if same(a, a) => ()\nConsole.writeLine(a)", "ok\na\n")]
    public void Execute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture("Reference" + name, source, expected);

    [Theory]
    [InlineData("Literal", "\"a\"")]
    [InlineData("Call", "echo(\"a\")")]
    [InlineData("If", "(if true => \"a\" else => \"b\")")]
    [InlineData("Do", "(label work: do\n    exit to work \"a\"\n)")]
    [InlineData("Loop", "(loop\n    exit \"a\"\n)")]
    [InlineData("Match", "(match 1\n    1 => \"a\"\n    _ => \"b\"\n)")]
    public void TemporaryBorrowUsesAcquiredStorage(string name, string expression)
    {
        var source = Same + "func echo(a: string) -> string => a@move\nlet a = \"a\"\nif same(" + expression + ", a) => Console.writeLine(\"ok\")\nConsole.writeLine(a)";
        ScalarEmissionTest.EmitFixture("ReferenceTemporary" + name, source, "ok\na\n");
    }

    [Theory]
    [InlineData("ShortCircuit", "var flag = false\nif flag and same(\"a\", \"a\") => ()\nif true or same(\"b\", \"b\") => Console.writeLine(\"ok\")", "ok\n", "a=0;b=0;ok=1")]
    [InlineData("Both", "if same(\"a\", \"a\") => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1")]
    [InlineData("Transfer", "func test(a: ref/string, b: bool) -> bool => b\nfunc run() -> string\n    test(\"held\", (return \"ok\"))\n    return \"bad\"\nConsole.writeLine(run())", "ok\n", "held=1;ok=1;bad=0")]
    [InlineData("EvaluationOrder", "func echo(s: string) -> string\n    Console.writeLine(\"evaluate\")\n    return s@move\nif same(right: echo(\"b\"), left: echo(\"a\")) => ()", "evaluate\nevaluate\n", "a=1;b=1;evaluate=2")]
    public void TemporaryLoansPreserveExpressionCleanup(string name, string source, string stdout, string audit)
    {
        source = Same + source;
        // Short circuit may need conditional temporary-destruction flags.
        var c = MinimalEmissionTest.Analyze(source);
        var ir = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("ReferenceTemporary" + name, ir, stdout);
        StringEmissionTest.WriteAuditedFixture("ReferenceTemporary" + name, ir, stdout, audit);
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
        var source = $"func compare(a: ref/string, b: ref/string) -> bool => a {op} b\nlet a = \"{a}\"\nlet b = \"{b}\"\nif compare(a, b) => Console.writeLine(\"true\") else => Console.writeLine(\"false\")";
        ScalarEmissionTest.EmitFixture("ReferenceCompare" + op.Replace("=", "Eq").Replace("!", "Not").Replace("<", "Lt").Replace(">", "Gt") + (a.Length == 0 ? "Empty" : a.Contains('\\') ? "Nul" : string.Empty), source, result ? "true\n" : "false\n");
    }

    [Theory]
    [InlineData("func identity(a: ref/string) -> ref/string => a\n()", true)]
    [InlineData("func save(a: ref/string)\n    let saved = a\n()", true)]
    [InlineData("func compare(a: ref/string, b: string) -> bool => a == b\n()", true)]
    [InlineData("func good(a: ref/string)\n    Console.writeLine(a)\n()", true)] // SPEC 22.1: writeLine accepts an existing shared reference.
    [InlineData("let a = \"a\"\na@ref", true)]
    [InlineData("func unused(a: ref/string) => 1.0 + 2.0\n()", true)]
    public void ReferenceUsesAndIndependentFloatResultsRespectSupport(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Theory]
    [InlineData("test(left: a, right: take(a@move))", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("test(right: take(a@move), left: a)", DiagnosticCode.MovedPlace_Kd)]
    public void SourceOrderDeterminesTheConflict(string expression, DiagnosticCode failure)
    {
        var c = MinimalEmissionTest.Analyze("func test(left: ref/string, right: bool) -> bool => right\nfunc take(a: string) -> bool => true\nlet a = \"a\"\n" + expression);
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Problems, x => x.Code == failure);
        Assert.False(c.Validate(out _));
    }

    [Theory]
    [InlineData("same(a, take(a@move))")]
    [InlineData("same(a, (return a@move))")]
    public void OperandMoveStillConflicts(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Same + "func take(a: string) -> string => a\nfunc run() -> string\n    let a = \"a\"\n    " + expression + "\n    return \"bad\"\nConsole.writeLine(run())");
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
    }

    [Theory]
    [InlineData("a = \"replacement\"")]
    [InlineData("defer => _ = a@move")]
    public void LaterArgumentEffectsSeeTheActiveLoan(string effect)
    {
        var source = "func test(a: ref/string, flag: bool) -> bool => flag\nvar a = \"a\"\ntest(a, (label work: do\n    " + effect + "\n    exit to work true\n))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Binding.Issues);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Validate(out _));
    }

    [Theory]
    [InlineData("a == (return true)")]
    [InlineData("(return true) == \"a\"")]
    public void NoncompletingReferenceComparisonNeedsNoResult(string expression)
    {
        var source = "func test(a: ref/string) -> bool\n    " + expression + "\n    return false\nlet a = \"a\"\nif test(a) => Console.writeLine(a)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        ScalarEmissionTest.EmitFixture("ReferenceAbruptComparison" + (expression.StartsWith('a') ? "Right" : "Left"), source, "a\n");
    }

    // IMPL 21.3.1 (AbiRecords; DECISIONS R5-M145 under R5-M12): a shared borrow of a string is passed as its address, noundef nonnull align 8
    // dereferenceable(24) and never noalias, at the callee's parameters and at its call alike, and the callee is not readonly; the string is
    // neither copied nor destroyed by the borrow, which the native audit's single destruction shows.
    [Fact]
    public void BorrowedValuesNeedNoPhysicalCopyOrStorage()
    {
        const string Source = Same + "let a = \"a\"\nif same(a, a) => Console.writeLine(a)";
        var c = MinimalEmissionTest.Analyze(Source);
        using var writer = new StringWriter();
        Assert.True(c.WriteIr(writer, out var error), error);
        var ir = writer.ToString();
        const string Shared = @"ptr noundef nonnull align 8 dereferenceable\(24\) %\w+";
        var same = Assert.Single(Regex.Matches(ir, @"(?ms)^define internal i1 @([\w.]+)\(" + Shared + ", " + Shared + @"\) #0 \{\n.*?^\}"));
        Assert.Single(Regex.Matches(ir, @"call i1 @" + Regex.Escape(same.Groups[1].Value) + @"\(" + Shared + ", " + Shared + @"\)"));
        Assert.DoesNotContain("noalias", ir);
        Assert.DoesNotContain("readonly", same.Value);
        StringEmissionTest.WriteAuditedFixture("ReferenceSingleDestruction", ir, "a\n", "a=1");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "func forward(a: ref/string, b: ref/string) -> bool => same(a, b)\nlet a = \"a\"\nforward(a, a)");
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        var valid = true;
        var bindingBytes = AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var emissionBytes = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, emissionBytes);
    }

    [Fact]
    public void ReparseReusesThePhysicalAbi()
    {
        const string Source = Same + "let a = \"a\"\nsame(a, a)";
        var c = MinimalEmissionTest.Analyze(Source);
        var pool = new FunctionAbiPool();
        var same = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "same");
        var abi = pool.Get(0, same, c.Layout);
        var oldType = same.Parameters[0].Type.TypeOf();
        c = MinimalEmissionTest.Analyze(Source.Replace("same", "renamed", StringComparison.Ordinal));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        var renamed = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "renamed");
        Assert.NotSame(oldType, renamed.Parameters[0].Type.TypeOf());
        Assert.Same(abi, pool.Get(0, renamed, c.Layout));
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
        var ir = ScalarEmissionTest.EmitFixture("ReferenceDivergent" + argument, source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Theory]
    [InlineData("Argument", "func test(a: ref/string, flag: i32) -> bool => true\nlet a = \"held\"\ntest(a, 2147483647 + 1)", 3, 9)]
    [InlineData("Body", "func test(a: ref/string) -> i32 => 2147483647 + 1\nlet a = \"held\"\ntest(a)", 1, 36)]
    public void AbortDoesNotDestroyBorrowedOwners(string name, string source, int line, int column)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ReferenceAbort" + name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture("ReferenceAbort" + name, ir, string.Empty, "held=0", 1, stderr);
    }

    [Fact]
    public void OverloadRankingRetainsExactOwnedAcquisition()
    {
        // SPEC 7.4: the borrowing candidate at the same position has a disjoint Type; the owned temporary takes the by-value one exactly.
        const string Source = "func pick(a: string) -> bool => true\nfunc pick(a: ref/i64) -> bool => false\nif pick(\"a\") => Console.writeLine(\"owned\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Empty(c.Binding.Issues);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First(static x => x.CallOf()?.Target.Name == "pick");
        Assert.Equal(ArgumentOperationKind.Value, call.CallOf()!.ArgumentOperations[0].Kind);
        ScalarEmissionTest.EmitFixture("ReferenceOverload", Source, "owned\n");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Populate(FunctionAbiPool pool)
    {
        var c = MinimalEmissionTest.Analyze(Same + "()");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "same");
        pool.Get(0, function, c.Layout);
        return new WeakReference(function);
    }
}
