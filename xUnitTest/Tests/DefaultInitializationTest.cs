// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class DefaultInitializationTest
{
    private const string NeverDefault = "func f(y?: i32 = (scope: do\n    let n: i32 = loop => continue\n    exit to scope: n + 1\n)) => ()\n";

    [Theory]
    [InlineData("")]
    [InlineData("f()")]
    [InlineData("f(3)")]
    public void NeverInitializerIsCheckedIndependentlyOfOmission(string call)
        => AssertUninitialized(NeverDefault + call);

    [Theory]
    [InlineData("")]
    [InlineData("f()")]
    [InlineData("f(3)")]
    public void MissingInitializerIsCheckedIndependentlyOfOmission(string call)
        => AssertUninitialized("func f(y?: i32 = (scope: do\n    var n: i32\n    exit to scope: n\n)) => ()\n" + call);

    [Theory]
    [InlineData("contract C\n    func f(y?: i32 = (scope: do\n        var n: i32\n        exit to scope: n\n    ))")]
    [InlineData("contract C\n    func f(y?: i32 = (scope: do\n        let n: i32 = loop => continue\n        exit to scope: n\n    ))")]
    [InlineData("func f(c: bool, y?: i32 = (scope: do\n    var n: i32\n    if c => n = 1\n    exit to scope: n\n)) => ()\nf(true, 3)")]
    [InlineData("func f(y?: i32 = (scope: do\n    var n: i32\n    exit to scope: 1\n    exit to scope: n\n)) => ()\nf(3)")]
    public void EveryDeclarationAndCheckingPathRequiresInitialization(string source)
        => AssertUninitialized(source);

    [Theory]
    [InlineData("Supplied", "func f(x?: i32 = (scope: do\n    var n: i32\n    n = 1\n    exit to scope: n\n)) => ()\nf(3)\nConsole.writeLine(\"ok\")")]
    [InlineData("Sequential", "func f(x: i32, y?: i32 = (scope: do\n    var n: i32\n    n = x + 1\n    exit to scope: n\n)) -> i32 => y\nif f(2) == 3 and f(9, 4) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("Branches", "func f(c: bool, y?: i32 = (scope: do\n    var n: i32\n    if c => n = 1 else => n = 2\n    exit to scope: n\n)) -> i32 => y\nif f(true) == 1 and f(false) == 2 => Console.writeLine(\"ok\")")]
    [InlineData("Loop", "func f(y?: i32 = (loop\n    var n: i32\n    n = 7\n    exit n\n)) -> i32 => y\nif f() == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Unit", "func f(y?: () = (scope: do\n    var n: ()\n    n = ()\n    exit to scope: n\n)) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("SuppliedNever", "func f(y?: i32 = (scope: do\n    var n: i32 = loop => continue\n    n = 7\n    exit to scope: n\n)) -> i32 => y\nif f(3) == 3 => Console.writeLine(\"ok\")")]
    public void EmitsInitializedDefaultLocals(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + name, source, "ok\n");

    [Fact]
    public void NeverInitializerCannotReachLaterDefaultsOrCallee()
        => ScalarEmissionTest.EmitFixture(
            Prefix + "Never",
            "func f(y?: i32 = (scope: do\n    var n: i32 = loop => continue\n    n = 7\n    exit to scope: n\n), z?: i32 = (2147483647 + 1)) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf()",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadAndWarmAnalysisCheckUnusedDefaults(bool invalid)
    {
        var source = invalid ? NeverDefault + "f(3)" : "func f(x: i32, y?: i32 = (scope: do\n    var n: i32\n    n = x + 1\n    exit to scope: n\n)) -> i32 => y\nf(2)";
        var c = MinimalEmissionTest.Analyze(source);
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(!invalid, c.Ownership.Analyze().IsVerified);
        Assert.Equal(!invalid, c.Emission.Validate(out _));
        if (invalid)
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        }

        var allocated = AllocationMeasurement.Measure(() => c.Ownership.Analyze());
        Assert.Equal(0, allocated);
        Assert.Equal(!invalid, c.Ownership.Result.IsVerified);
        c.Bind();
        Assert.False(c.Emission.Validate(out _));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(!invalid, c.Ownership.Analyze().IsVerified);
        Assert.Equal(!invalid, c.Emission.Validate(out _));
    }

    private static void AssertUninitialized(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        c.Ownership.ReportDiagnostics();
        var issue = c.Ownership.Issues.First(x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.Contains(issue.Source.CodeContext.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.UninitializedPlace_Kd) && x.Span == issue.Source.Span);
        Assert.False(c.Emission.Validate(out _));
    }

#if DEBUG
    private const string Prefix = "DefaultInitializationDebug";
#else
    private const string Prefix = "DefaultInitializationRelease";
#endif
}
