// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class FunctionItemCallableTest
{
    [Theory]
    [InlineData("StoredShared", "func invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nlet item = inc\nrequire invoke(item@ref) == 42 else => $abort(\"stored\")")]
    [InlineData("TemporaryShared", "func invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nrequire invoke(inc) == 42 else => $abort(\"temporary\")")]
    [InlineData("Exclusive", "func invoke<F>(f: uniq/F) -> i32\n    F is Callable<uniq, (i32) -> i32>\n    let first = f(40)\n    return f(first)\nvar item = inc\nrequire invoke(item@uniq) == 42 else => $abort(\"exclusive\")")]
    [InlineData("Owner", "func invoke<F>(f: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return f@move(41)\nlet item = inc\nrequire invoke(item) == 42 and invoke(inc) == 42 and item(0) == 1 else => $abort(\"owner\")")]
    [InlineData("Forward", "func invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nfunc forward<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return invoke(f)\nlet item = inc\nrequire forward(item@ref) == 42 else => $abort(\"forward\")")]
    [InlineData("Distinct", "func dec(value: i32) -> i32 => value - 1\nfunc invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nrequire invoke(inc) == 42 and invoke(dec) == 40 else => $abort(\"identity\")")]
    [InlineData("Qualified", "group G\n    public func answer(value: i32) -> i32 => value + 1\nfunc invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nrequire invoke(G.answer) == 42 else => $abort(\"qualified\")")]
    [InlineData("OptionArgument", "let option = Option.Some(inc)\nmatch option\n    .Some(let f) => require f(41) == 42 else => $abort(\"option\")\n    .None => $abort(\"none\")")]
    public void GenericCallsKeepTheItemAndReceiverContract(string name, string body)
        => ScalarEmissionTest.EmitFixture("FunctionItemCallable" + name, "func inc(value: i32) -> i32 => value + 1\n" + body, string.Empty);

    [Theory]
    [InlineData("(bool) -> i32")]
    [InlineData("(i32) -> bool")]
    [InlineData("(i32, i32) -> i32")]
    [InlineData("(ref/i32) -> i32")]
    public void CallableEvidenceKeepsTheCompleteSignature(string signature)
    {
        var c = MinimalEmissionTest.Analyze("func inc(value: i32) -> i32 => value + 1\nfunc accept<F>(f: ref/F)\n    F is Callable<" + signature + ">\n    return\naccept(inc)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Failure == BindingFailure.NoApplicableCandidate);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let item = inc\ninvoke(item)")]
    [InlineData("invoke(inc)")]
    public void AnExclusiveParameterStillNeedsExplicitAcquisition(string call)
    {
        var c = MinimalEmissionTest.Analyze("func inc(value: i32) -> i32 => value + 1\nfunc invoke<F>(f: uniq/F) -> i32\n    F is Callable<uniq, (i32) -> i32>\n    return f(41)\n" + call);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmItemConstraintAndInstancePlansAllocateNothing()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nfunc invoke<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(41)\nrequire invoke(inc) == 42 else => $abort(\"item\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
