// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class AbortEmissionTest
{
    public static TheoryData<string, string, string, int, string> Fixtures => new()
    {
        { "AbortLiteral", "$abort(\"failed\")", string.Empty, 1, "Hello.kimi:1:1: abort KIMI_E_ABORT: failed\n" },
        { "AbortEmpty", "$abort(\"\")", string.Empty, 1, "Hello.kimi:1:1: abort KIMI_E_ABORT: \n" },
        { "AbortUnicode", "$abort(\"日本語\\0x\")", string.Empty, 1, "Hello.kimi:1:1: abort KIMI_E_ABORT: 日本語\0x\n" },
        { "AbortOwned", "let text = \"owned\"\n$abort(text@move)", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: owned\n" },
        { "AbortShadow", "func abort(text: string) => Console.writeLine(text)\n$abort(\"builtin\")", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: builtin\n" },
        { "AbortNested", "$abort($abort(\"inner\"))", string.Empty, 1, "Hello.kimi:1:8: abort KIMI_E_ABORT: inner\n" },
        { "AbortOnce", "$abort((label message: do\n    Console.writeLine(\"once\")\n    exit to message \"message\"\n))", "once\n", 1, "Hello.kimi:1:1: abort KIMI_E_ABORT: message\n" },
        { "AbortSkipCleanup", "defer => Console.writeLine(\"cleanup\")\n$abort(\"stop\")\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: stop\n" },
        { "AbortArgumentReturn", "func f()\n    defer => Console.writeLine(\"cleanup\")\n    $abort((label message: do\n        return\n        exit to message \"unused\"\n    ))\nf()\nConsole.writeLine(\"after\")", "cleanup\nafter\n", 0, string.Empty },
        { "AbortCondition", "if $abort(\"condition\") => Console.writeLine(\"bad\")", string.Empty, 1, "Hello.kimi:1:4: abort KIMI_E_ABORT: condition\n" },
        { "AbortArgumentOverflow", "$abort((label message: do\n    var x: i32 = 2147483647\n    x = x + 1\n    exit to message \"outer\"\n))", string.Empty, 1, "Hello.kimi:3:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n" },
        { "AbortConditionalResult", "var flag = false\nlet text = if flag => $abort(\"bad\") else => \"ok\"\nConsole.writeLine(text)", "ok\n", 0, string.Empty },
        { "AbortWhileCondition", "while $abort(\"condition\") => Console.writeLine(\"bad\")", string.Empty, 1, "Hello.kimi:1:7: abort KIMI_E_ABORT: condition\n" },
        { "AbortRequireCondition", "require $abort(\"condition\") else => $abort(\"bad\")", string.Empty, 1, "Hello.kimi:1:9: abort KIMI_E_ABORT: condition\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsNativeFixtures(string name, string source, string stdout, int exit, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, exit, stderr);

    [Theory]
    [InlineData("$abort(\"failed\")")]
    [InlineData("let message = \"failed\"\n$abort(message@move)")]
    [InlineData("if false\n    $abort(\"failed\")\nConsole.writeLine(\"ok\")")]
    [InlineData("let value: i32 = if true => 1 else => $abort(\"failed\")\nif value == 1 => Console.writeLine(\"ok\")")]
    public void ExplicitAbortPassesEveryCompilerStage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
        Assert.Contains("@__kimi_abort_message", writer.ToString());
    }

    [Theory]
    [InlineData("$abort(1)")]
    [InlineData("$abort(true)")]
    [InlineData("$abort(())")]
    [InlineData("$abort(missing)")]
    [InlineData("let x = \"text\"\n$abort(x@ref)")]
    [InlineData("abort(\"not declared\")")]
    [InlineData("::Kimi.abort(\"not an API\")")]
    [InlineData("$abort()")]
    [InlineData("$abort(\"a\", \"b\")")]
    [InlineData("$abort(text: \"a\")")]
    [InlineData("$abort(\"a\",)")]
    [InlineData("$other(\"a\")")]
    [InlineData("$abort(\"stop\")\nlet invalid: i32 = true")]
    public void RejectsInvalidInput(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("let text: string\n$abort(text@move)", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("let text = \"x\"\n_ = text@move\n$abort(text@move)", DiagnosticCode.MovedPlace_Kd)]
    public void RejectsOwnershipViolations(string source, DiagnosticCode failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Problems, x => x.Code == failure);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // Code after $abort is unreachable: its first use of a Local is one location-only Unsupported at that use.
    [Theory]
    [InlineData("let text = \"x\"\n$abort(text@move)\nConsole.writeLine(text)", "text")]
    [InlineData("let x: i32\n$abort(\"stop\")\nlet y = x", "x")]
    public void UnreachableLocalUseIsUnsupported(string source, string at)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, source.LastIndexOf(at, StringComparison.Ordinal), at.Length), (issue.Code, c.SyntaxOf(issue.Subject).Span.Start, c.SyntaxOf(issue.Subject).Span.Length));
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void AbortRuntimeCannotReturnOrDestroyItsMessage()
    {
        var c = MinimalEmissionTest.Analyze("$abort(\"x\")");
        var ir = CompilationTestHelper.WriteIr(c);
        var start = ir.IndexOf("define internal void @__kimi_abort_message(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var runtime = ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        Assert.Contains("noreturn", runtime);
        Assert.Contains("call void @__kimi_exit(i32 1)", runtime);
        Assert.DoesNotContain("@__kimi_destroy_string", runtime);
        Assert.DoesNotContain("@__kimi_alloc", runtime);
        Assert.DoesNotContain("ret void", runtime);
    }

    // IMPL 20.1, 21.4.2: a location names its source by the logical name, the project-relative path with `/` separators; a project
    // without a directory is the current directory's, so a fully qualified source path is displayed relative to it, never as the
    // checkout's path.
    [Theory]
    [InlineData("", "Hello.kimi:1:1")]
    [InlineData("sub", "sub/Hello.kimi:1:1")]
    public void LocationsNameTheLogicalSource(string directory, string location)
    {
        var c = MinimalEmissionTest.Analyze("$abort(\"x\")", Path.GetFullPath(Path.Combine(directory, "Hello.kimi")));
        Assert.Equal(location, c.Locations.Text(SourceRef.Of(Abort(c))));
    }

    // The location memo holds the current program only: an edit releases the texts of the documents it replaced.
    [Fact]
    public void AnEditReleasesTheReplacedLocationTexts()
    {
        var c = MinimalEmissionTest.Analyze("$abort(\"before\")");
        var replaced = LocationText(c);
        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "\n$abort(\"after\")");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.EndsWith(":2:1", c.Locations.Text(SourceRef.Of(Abort(c))));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(replaced.TryGetTarget(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindReloadAndWarmPassesPreserveTheBuiltin()
    {
        var c = MinimalEmissionTest.Analyze("let text = \"x\"\n$abort(text@move)");
        var macro = Abort(c);
        var invocation = Assert.IsType<InvocationKoto>(macro.Operand);
        var call = Assert.IsType<CallPlan>(CallPlan.Of(macro));
        var version = HirTables.Publish(c);
        c.InvalidateSourceAnalysis();
        Assert.Null(invocation.CallOf()); // The current query revokes the call; raw views retain the preceding pass.
        Assert.True(c.Bind().IsComplete);
        Assert.Same(call, CallPlan.Of(macro));
        Assert.Same(call, invocation.CallOf());
        Assert.True(HirTables.Publish(c) > version);
        c = CompilationTestHelper.Reload(c);
        Assert.True(c.Bind().IsComplete);
        Assert.Same(Assert.IsType<InvocationKoto>(Abort(c).Operand).CallOf(), CallPlan.Of(Abort(c)));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null));

        var success = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.Generate(TextWriter.Null);
        });
        Assert.True(success);
        Assert.Equal(0, allocated);

        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "$abort(1)");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(CallPlan.Of(Abort(c)));
        Assert.Null(Assert.IsType<InvocationKoto>(Abort(c).Operand).CallOf());
        version = HirTables.Publish(c);
        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "$abort(\"after\")");
        Assert.True(c.Bind().IsComplete);
        macro = Abort(c);
        invocation = Assert.IsType<InvocationKoto>(macro.Operand);
        call = Assert.IsType<CallPlan>(CallPlan.Of(macro));
        Assert.Same(invocation.CallOf(), call);
        Assert.Same(invocation.ArgumentNodes[0], call.ArgumentOperations[0].Source);
        Assert.Equal("\"after\"", call.ArgumentOperations[0].Source!.ToString());
        Assert.True(HirTables.Publish(c) > version);
        Assert.Equal((HirOp.Call, HirOp.Syntax), (HirTables.Op(macro), HirTables.Op(invocation)));
    }

    private static MacroKoto Abort(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MacroKoto>().Single();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<string> LocationText(Compilation c) => new(c.Locations.Text(SourceRef.Of(Abort(c)))!);
}
