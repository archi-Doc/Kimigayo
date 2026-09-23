// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class TerminalWhileContinuationTest
{
    [Theory]
    [InlineData("return")]
    [InlineData("exit")]
    [InlineData("if c => return else => exit")]
    [InlineData("let local = \"local\"\n                exit")]
    public void TerminalBodiesPreserveTheSkippedPath(string body)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", body, "x = 4", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("var x: i32", "x = 3\n                exit", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "x = 3\n                exit", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"", "_ = s@move\n                exit", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "_ = s@move\n                return", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void ZeroIterationsAndTerminalHistoriesKeepTheirStates(string declaration, string body, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, body, after, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void ReturnOnlyEffectsDoNotEnterTheSkippedSuccessor()
    {
        var c = MinimalEmissionTest.Analyze(Source("let s = \"s\"", "_ = s@move\n                return", "Console.writeLine(s)", "()"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ContinuingBodyStillRequiresAnIterationProof()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", "if c => return else => continue", "x = 4", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("x = 3\n                exit")]
    [InlineData("if c => return else\n                    x = 3\n                    exit")]
    public void OnePassLoopsJoinOnlyCaughtExits(string body)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", body, "let y = x", "()", "loop"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void OnePassLoopResultsKeepAcquisitionAndCleanup()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", "let local = \"local\"\n                if c => exit 3 else => exit 4", "x = result", "let y = x", "let result = loop"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("exit")]
    public void StoredLoanProtectsItsOwnerInTerminalBodies(string transfer)
    {
        var source = Source("var counter = Counter.init()\n    let r = counter@ref", "counter.value = 9\n                " + transfer, "()", "let n = r.value") +
            "\nstruct Counter\n    public var value: i32 = 0";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadAndRepeatedAnalysisRebuildLoopAndMatchHistories()
    {
        var source = Source("var x = 1", "if c => return else => exit", "choice: match c\n                true => yield to choice\n                false => x = 3", "let y = x");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var syntax = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref syntax);
        Assert.NotNull(syntax);
        syntax.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }
    }

    private static string Source(string declaration, string body, string after, string use, string loopHeader = "while c")
        => "func stop() -> Never => $abort(\"terminal while\")\nfunc f(c: bool)\n    " + declaration +
            "\n    do\n        loop\n            if c => return else => exit\n            " + loopHeader + "\n                " + body +
            "\n            " + after + "\n        stop()\n    " + use + "\nf(true)";
}
