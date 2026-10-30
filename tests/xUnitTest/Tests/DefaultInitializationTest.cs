// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class DefaultInitializationTest
{
    private const string NeverDefault = "func f(y: i32 = (label scope: do\n    let n: i32 = loop => continue\n    exit to scope n + 1\n)) => ()\n";

    [Theory]
    [InlineData("")]
    [InlineData("f()")]
    [InlineData("f(3)")]
    public void MissingInitializerIsCheckedIndependentlyOfOmission(string call)
        => AssertUninitialized("func f(y: i32 = (label scope: do\n    var n: i32\n    exit to scope n\n)) => ()\n" + call);

    [Theory]
    [InlineData("contract C\n    func f(y: i32 = (label scope: do\n        var n: i32\n        exit to scope n\n    ))")]
    [InlineData("func f(c: bool, y: i32 = (label scope: do\n    var n: i32\n    if c => n = 1\n    exit to scope n\n)) => ()\nf(true, 3)")]
    public void EveryDeclarationAndCheckingPathRequiresInitialization(string source)
        => AssertUninitialized(source);

    [Theory]
    [InlineData("Supplied", "func f(x: i32 = (label scope: do\n    var n: i32\n    n = 1\n    exit to scope n\n)) => ()\nf(3)\nConsole.writeLine(\"ok\")")]
    [InlineData("Sequential", "func f(x: i32, y: i32 = (label scope: do\n    var n: i32\n    n = x + 1\n    exit to scope n\n)) -> i32 => y\nif f(2) == 3 and f(9, 4) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("Branches", "func f(c: bool, y: i32 = (label scope: do\n    var n: i32\n    if c => n = 1 else => n = 2\n    exit to scope n\n)) -> i32 => y\nif f(true) == 1 and f(false) == 2 => Console.writeLine(\"ok\")")]
    [InlineData("Loop", "func f(y: i32 = (loop\n    var n: i32\n    n = 7\n    exit n\n)) -> i32 => y\nif f() == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Unit", "func f(y: () = (label scope: do\n    var n: ()\n    n = ()\n    exit to scope n\n)) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("SuppliedNever", "func f(y: i32 = (loop => continue)) -> i32 => y\nif f(3) == 3 => Console.writeLine(\"ok\")")]
    public void EmitsInitializedDefaultLocals(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + name, source, "ok\n");

    [Fact]
    public void NeverInitializerCannotReachLaterDefaultsOrCallee()
        => ScalarEmissionTest.EmitFixture(
            Prefix + "Never",
            "func f(y: i32 = (loop => continue), z: i32 = (2147483647 + 1)) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf()",
            "begin\n",
            observationMilliseconds: 200);

    private static void AssertUninitialized(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.UninitializedPlace_Kd);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        c.ReportAnalysis();
        var issue = c.Problems.First(x => x.Code == DiagnosticCode.UninitializedPlace_Kd);
        Assert.Contains(TestDiagnostics.Of(c.SyntaxOf(issue.Subject).CodeContext), x => x.Code == nameof(DiagnosticCode.UninitializedPlace_Kd) && x.Span == c.SyntaxOf(issue.Subject).Span);
        Assert.False(c.Generate(TextWriter.Null));
    }

#if DEBUG
    private const string Prefix = "DefaultInitializationDebug";
#else
    private const string Prefix = "DefaultInitializationRelease";
#endif

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReloadAndWarmAnalysisCheckUnusedDefaults(bool invalid)
        {
            var source = invalid ? NeverDefault + "f(3)" : "func f(x: i32, y: i32 = (label scope: do\n    var n: i32\n    n = x + 1\n    exit to scope n\n)) -> i32 => y\nf(2)";
            var c = MinimalEmissionTest.Analyze(source);
            c = CompilationTestHelper.Reload(c);
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal(!invalid, c.Analyze().IsVerified);
            Assert.Equal(!invalid, c.Generate(TextWriter.Null));
            if (invalid)
            {
                Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
            }

            var allocated = AllocationMeasurement.Measure(() => c.Analyze());
            Assert.Equal(0, allocated);
            Assert.Equal(!invalid, c.Analysis.IsVerified);
            c.Bind();
            Assert.False(c.Generate(TextWriter.Null));
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal(!invalid, c.Analyze().IsVerified);
            Assert.Equal(!invalid, c.Generate(TextWriter.Null));
        }
    }
}
