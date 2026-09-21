// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class RequireContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";
    private const string Default = "func value(c: bool, y: i32 = (do\n    var n: i32\n    do\n        require c else\n            n = 1\n            loop => continue\n        n = 2\n        loop => continue\n    n\n)) -> i32 => y\n";

    [Theory]
    [InlineData("Return", "var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        stop()", "let y = x", false, "done\n")]
    [InlineData("Success", "var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        return", "let y = x", true, "done\n")]
    [InlineData("Direct", "var x = 1", "require c else => return", "x = 2\n        return", "let y = x", false, "done\n")]
    [InlineData("Nested", "var x: i32", "require c else\n            x = 1\n            if c => return\n            return", "x = 2\n        return", "let y = x", false, "done\n")]
    [InlineData("Chain", "var x = 1", "require c else => return\n        require c else => return", "return", "let y = x", true, "done\n")]
    [InlineData("Move", "let s = \"s\"", "require c else\n            Console.writeLine(s)\n            return", "return", "()", false, "s\ndone\n")]
    [InlineData("Loan", "var counter = Counter.init()\n    let r = counter@ref", "require c else => return", "return", "let n = r.value", false, "done\n")]
    public void FailureAndSuccessKeepTheirTerminalFacts(string name, string declaration, string require, string tail, string use, bool condition, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + name, Source(declaration, require, tail, use, condition) + "\nConsole.writeLine(\"done\")" + Counter, stdout);

    [Fact]
    public void SuccessAbortsWithoutExecutingTheCheckingContinuation()
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "Abort", Source("var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        stop()", "let y = x\n    Console.writeLine(\"bad\")", true), string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "require c else => return", "x = 2\n        stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "require c else\n            x = 1\n            return", "stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "require c else\n            if c => return\n            x = 1\n            return", "x = 2\n        stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "require c else\n            Console.writeLine(s)\n            return", "stop()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "require c else => return", "Console.writeLine(s)\n        stop()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "require c else\n            x = 1\n            return", "stop()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("var counter = Counter.init()\n    let r = counter@ref", "require c else\n            counter.value = 9\n            return", "stop()", "let n = r.value", OwnershipFailure.ComparisonLoanConflict)]
    public void FailurePathsDoNotRestoreOwnership(string declaration, string require, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, require, tail, use) + Counter);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Exit", "exit", false)]
    [InlineData("Return", "return", true)]
    public void FailureTransfersKeepTheirOriginalTarget(string name, string transfer, bool condition)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + name + "Target", Stop + "func f(c: bool)\n    var x: i32\n    do\n        loop\n            require c else\n                x = 1\n                " + transfer + "\n            exit\n        x = 2\n        return\n    let y = x\nf(" + (condition ? "true" : "false") + ")\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("exit", true)]
    [InlineData("return", false)]
    public void OnlyTransfersEscapingTheScopeContributeToItsJoin(string transfer, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool)\n    var x: i32\n    do\n        loop\n            require c else => " + transfer + "\n            exit\n        x = 2\n        return\n    let y = x\nf(true)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        if (accepted)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
        else
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void DefaultRequireDoesNotCreateRuntimeCompletion(string condition)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "Default" + condition, Default + "Console.writeLine(\"begin\")\nvalue(" + condition + ")", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void SuppliedDefaultStillChecksTheFailurePath()
    {
        var c = MinimalEmissionTest.Analyze(Default.Replace("            n = 1\n", string.Empty, StringComparison.Ordinal) + "value(true, 3)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void DefaultFailureCanDeliverToItsEnclosingScope()
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "DefaultResult", "func value(c: bool, y: i32 = (choice: do\n    require c else => exit to choice: 4\n    exit to choice: 7\n)) -> i32 => y\nrequire value(false) == 4 else => $abort(\"failure\")\nrequire value(true) == 7 else => $abort(\"success\")\nrequire value(false, 9) == 9 else => $abort(\"supplied\")\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("let x: i32\nvalue(true)\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"\nConsole.writeLine(s)\nvalue(false)\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void DivergentDefaultPreservesCallerFacts(string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Default + tail);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("require c else\n            defer => x = 3\n            return")]
    [InlineData("require c else => loop => x = 3")]
    public void CleanupAndEffectfulDivergenceStayGuarded(string require)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", require, "return", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void FailureFactsDoNotFlowIntoTheSuccessor()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool)\n    var x: i32\n    require c else\n        x = 1\n        return\n    let y = x\nf(true)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadedRequireCheckingAllocatesNothingWhenWarm()
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "require c else => return", "return", "let n = r.value") + Counter);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
    }

    private static string Source(string declaration, string require, string tail, string use, bool condition = false)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        " + require + "\n        " + tail + "\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
