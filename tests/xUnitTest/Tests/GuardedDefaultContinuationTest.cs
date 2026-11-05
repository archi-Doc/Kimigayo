// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GuardedDefaultContinuationTest
{
    [Theory]
    [InlineData("let n => n + 2", "7", 9)]
    [InlineData("let n if n > 0 => n + 1\n    let n => -n", "-3", 3)]
    [InlineData("let n if n > 0\n        var total: i32 = n\n        total += 2\n        yield total\n    _ => 0", "7", 9)]
    [InlineData("let n if n > 0 => n + 1\n    _ => 0", "2147483647, 17", 17)]
    public void DefaultsReadCandidatesAndAcquireBodyBindings(string arms, string arguments, int expected)
    {
        var source = "func f(x: i32, y: i32 = (match x\n    " + arms + "\n)) -> i32 => y\n" +
            "if f(" + arguments + ") != " + expected + " => $abort(\"default mismatch\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void FalseGuardKeepsDefaultLocalEffectsAndCandidateSnapshot()
    {
        const string Source = "func f(x: i32, y: i32 = (label scope: do\n    var total = 1\n    let value = match x\n        let n if (label check: do\n            total += n\n            exit to check false\n        ) => 0\n        let n => n + total\n    exit to scope value\n)) -> i32 => y\nif f(3) != 7 => $abort(\"effects lost\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void SuppliedDefaultStillChecksItsDeclaration()
    {
        const string Source = "func f(x: i32, y: i32 = (match x\n    var n if n > 0\n        let value: i32\n        yield value + n\n    _ => 0\n)) -> i32 => y\nf(3, 7)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.UninitializedPlace_Kd);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
    }

    [Theory]
    [InlineData("pair", "(let n, true) if n > 0 => n + 2\n    (let n, _) => -n")]
    [InlineData("(pair.0, pair.1)@move", "(var n, true)\n        n += 2\n        yield n\n    (let n, _) => -n")]
    [InlineData("(pair, 2)@move", "((let n, true), let extra) => n + extra\n    ((let n, _), _) => -n")]
    [InlineData("pair", "let saved => if saved.1 => saved.0 + 2 else => -saved.0")]
    [InlineData("pair", "let saved\n        var first: i32 = saved.0\n        if saved.1 => first += 2 else => first = -first\n        yield first")]
    public void ScalarTupleSubjectsUseCopyPreparedStorage(string subject, string arms)
    {
        var source = "func f(pair: (i32, bool), result: i32 = (match " + subject + "\n    " + arms + "\n)) -> i32 => result\n" +
            "if f((7, true)) != 9 or f((-3, false)) != 3 => $abort(\"tuple default\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void TupleDefaultLocalPreservesThePreparedSnapshot()
    {
        const string Source = "func f(x: i32, result: i32 = (label scope: do\n    var pair = (x, 2)\n    pair.0 += pair.1\n    let selected = match pair\n        let saved => saved.0\n    exit to scope selected\n)) -> i32 => result\nvar input = 7\nif f(input) != 9 or input != 7 => $abort(\"local tuple\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ImmutableTuplePatternCannotBeUpdated()
    {
        var c = MinimalEmissionTest.Analyze("match (1, true)\n    let saved => saved.0 = 2");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }
}
