// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class FunctionItemResultTest
{
    [Theory]
    [InlineData("IfItem", "let f = if true => inc else => inc")]
    [InlineData("MatchItem", "let f = match true\n    true => inc\n    false => inc")]
    [InlineData("DoItem", "let f = do => inc")]
    [InlineData("ExitItem", "let f = loop => exit inc")]
    [InlineData("IfErased", "let f: (i32) -> i32 = if true => inc else => dec")]
    [InlineData("MatchErased", "let f: (i32) -> i32 = match false\n    true => dec\n    false => inc")]
    [InlineData("DoErased", "let f: (i32) -> i32 = do => inc")]
    [InlineData("YieldErased", "let f: (i32) -> i32 = label choice: if true\n    loop => yield to choice inc\nelse => dec")]
    [InlineData("ExitErased", "let f: (i32) -> i32 = label work: do\n    exit to work inc")]
    [InlineData("StoredErased", "let a = inc\nlet b = dec\nlet f: (i32) -> i32 = if false => b else => a")]
    [InlineData("ClosureErased", "let n = 1\nlet a = func [n] (v: i32) -> i32 => v + n\nlet f: (i32) -> i32 = match true\n    true => a\n    false => dec")]
    public void ResultsPreserveItemsOrUseTheFixedFunctionExpectation(string name, string declaration)
        => ScalarEmissionTest.EmitFixture("FunctionItemResult" + name, "func inc(value: i32) -> i32 => value + 1\nfunc dec(value: i32) -> i32 => value - 1\n" + declaration + "\nrequire f(41) == 42 else => $abort(\"result\")", string.Empty);

    [Theory]
    [InlineData("let f = if true => inc else => dec")]
    [InlineData("let f = match true\n    true => inc\n    false => dec")]
    [InlineData("let f: (bool) -> i32 = if true => inc else => dec")]
    [InlineData("let common: (i32) -> i32 = inc\nlet f = if true => common@move else => inc")]
    [InlineData("let common: (i32) -> i32 = inc\nlet item = inc\nlet f = if true => item else => common@move")]
    [InlineData("let common: (i32) -> i32 = inc\nlet closure = func (v: i32) -> i32 => v\nlet f = if true => closure else => common@move")]
    public void ResultInferenceDoesNotInventAnErasureOrChangeASignature(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("func inc(value: i32) -> i32 => value + 1\nfunc dec(value: i32) -> i32 => value - 1\n" + declaration);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AReferenceToANeverFunctionStillCompletesNormally()
        => ScalarEmissionTest.EmitFixture("FunctionItemResultNever", "func fail() -> Never => $abort(\"called\")\nlet f = if true => fail else => fail\nlet saved = (f, 42)\nrequire saved.1 == 42 else => $abort(\"reference\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmResultPlansAllocateNothing()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet item = if true => inc else => inc\nlet f: (i32) -> i32 = if false => inc else => item\nrequire f(41) == 42 else => $abort(\"result\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
