// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
        { "AbortOwned", "let text = \"owned\"\n$abort(text)", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: owned\n" },
        { "AbortShadow", "func abort(text: string) => Console.writeLine(text)\n$abort(\"builtin\")", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: builtin\n" },
        { "AbortNested", "$abort($abort(\"inner\"))", string.Empty, 1, "Hello.kimi:1:8: abort KIMI_E_ABORT: inner\n" },
        { "AbortOnce", "$abort((message: do\n    Console.writeLine(\"once\")\n    exit to message: \"message\"\n))", "once\n", 1, "Hello.kimi:1:1: abort KIMI_E_ABORT: message\n" },
        { "AbortSkipCleanup", "defer => Console.writeLine(\"cleanup\")\n$abort(\"stop\")\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: stop\n" },
        { "AbortArgumentReturn", "func f()\n    defer => Console.writeLine(\"cleanup\")\n    $abort((message: do\n        return\n        exit to message: \"unused\"\n    ))\nf()\nConsole.writeLine(\"after\")", "cleanup\nafter\n", 0, string.Empty },
        { "AbortCondition", "if $abort(\"condition\") => Console.writeLine(\"bad\")", string.Empty, 1, "Hello.kimi:1:4: abort KIMI_E_ABORT: condition\n" },
        { "AbortArgumentOverflow", "$abort((message: do\n    var x: i32 = 2147483647\n    x = x + 1\n    exit to message: \"outer\"\n))", string.Empty, 1, "Hello.kimi:3:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n" },
        { "AbortUnreachableLocal", "let text = \"kept\"\n$abort(\"stop\")\nConsole.writeLine(text)", string.Empty, 1, "Hello.kimi:2:1: abort KIMI_E_ABORT: stop\n" },
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
    [InlineData("let message = \"failed\"\n$abort(message)")]
    [InlineData("if false\n    $abort(\"failed\")\nConsole.writeLine(\"ok\")")]
    [InlineData("let value: i32 = if true => 1 else => $abort(\"failed\")\nif value == 1 => Console.writeLine(\"ok\")")]
    public void ExplicitAbortPassesEveryCompilerStage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out error), error);
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
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let text: string\n$abort(text)", OwnershipFailure.UninitializedUse)]
    [InlineData("let text = \"x\"\nConsole.writeLine(text)\n$abort(text)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let text = \"x\"\n$abort(text)\nConsole.writeLine(text)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32\n$abort(\"stop\")\nlet y = x", OwnershipFailure.UninitializedUse)]
    public void RejectsOwnershipViolationsIncludingUnreachableUses(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AbortRuntimeCannotReturnOrDestroyItsMessage()
    {
        var c = MinimalEmissionTest.Analyze("$abort(\"x\")");
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        var ir = writer.ToString();
        var start = ir.IndexOf("define internal void @__kimi_abort_message(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var runtime = ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        Assert.Contains("noreturn", runtime);
        Assert.Contains("call void @__kimi_exit(i32 1)", runtime);
        Assert.DoesNotContain("@__kimi_destroy_string", runtime);
        Assert.DoesNotContain("@__kimi_alloc", runtime);
        Assert.DoesNotContain("ret void", runtime);
    }

    [Fact]
    public void RebindReloadAndWarmPassesPreserveTheBuiltin()
    {
        var c = MinimalEmissionTest.Analyze("let text = \"x\"\n$abort(text)");
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, allocated);
    }
}
