// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SpecializationBindingTest
{
    private const string Ordinary = "func weight<T>(value?: ref/T) -> i32 => 1\n";

    [Theory]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i64>(value: ref/i64) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value => renamed: ref/i32) -> i32 => 2")]
    public void AcceptsClosedImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization + "\nfunc forward<T>(value?: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = forward<i32>(value@ref/i32)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("specialize func missing<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i64) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i64 => 2")]
    [InlineData("specialize func weight<i32>(other: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32, i64>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<T>(value: ref/T) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref{static}/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => true")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2\nspecialize func weight<i32>(value: ref/i32) -> i32 => 3")]
    public void RejectsInvalidImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void StillChecksOrdinaryBody()
    {
        var c = MinimalEmissionTest.Analyze("func weight<T>(value?: ref/T) -> i32 => true\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2\nlet value: i32 = 4\nlet result = weight<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void CannotUseResultsOrNamesToResolveTargetAmbiguity()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(value?: T) -> i32 => 1\nfunc f<T>(other?: i32) -> bool => true\nspecialize func f<i32>(value: i32) -> i32 => 2\n()");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void RebindingRechecksTheSpecializationSet()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new Kimi.Compiler.SourceDocument("duplicate.kimi", "specialize func weight<i32>(value: ref/i32) -> i32 => 3"));
        Assert.False(c.Bind().IsComplete);
    }
}
