// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SpecializationBindingTest
{
    private const string Ordinary = "func weight<T>(value: ref/T) -> i32 => 1\n";

    // SPEC 8.8: a length slot is supplied as an evaluated constant and enters the selection key (LengthKey(N)).
    private const string Lengths = "func pick<length N, T>(values: ref/[N of T]) -> i32 => 0\n";

    [Fact]
    public void LengthSlotsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationLengthSlots",
            Lengths + "specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nfunc forward<length N, T>(values: ref/[N of T]) -> i32 => pick<N, T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [2 of i32] = [1, 2]\nrequire pick<3, i32>(a@ref) == 1 and pick<2, i32>(b@ref) == 0 and forward<3, i32>(a@ref) == 1 and forward<2, i32>(b@ref) == 0 else => $abort(\"selection\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.8.2: the header inherits the original's boundary and defaults; callers still use the original omission rules.
    private const string Defaults = "func find<T>(value: T, count: i32 = 1) -> i32 => count\n";

    [Fact]
    public void InheritsDefaultsWithoutRedeclaringThem()
    {
        var c = MinimalEmissionTest.Analyze(Defaults + "specialize func find<i32>(value: i32, count: i32) -> i32 => count + 1\nrequire find<i32>(10) == 2 and find<i32>(10, count: 5) == 6 and find<bool>(true) == 1 else => $abort(\"defaults\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("specialize func find<i32>(value: i32, count: i32 = 1) -> i32 => count")]
    [InlineData("specialize func find<i32>(! value: i32, count: i32) -> i32 => count")]
    public void RejectsRedeclaredDefaultsAndBoundaries(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Defaults + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // SPEC 8.8.2: written binder names are inherited from the original; the body serves every admitted binding.
    private const string Named = "func first<T>(values: ref{source}/[3 of T]) -> ref{source}/T => values[0]@ref/T\n";

    [Fact]
    public void InheritedBindersKeepTheOriginalContract()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedBinders",
            Named + "specialize func first<i32>(values: ref{source}/[3 of i32]) -> ref{source}/i32 => values[2]@ref/i32\nfunc forward<T>(values: ref{source}/[3 of T]) -> ref{source}/T => first<T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [3 of bool] = [true, false, false]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 and forward<i32>(a@ref) == 3 and first<bool>(b@ref) == true else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void OmittedBinderNamesAreInheritedThroughTheirInputs()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationOmittedBinders",
            Named + "specialize func first<i32>(values: ref/[3 of i32]) -> ref/i32 => values[2]@ref/i32\nlet a: [3 of i32] = [1, 2, 3]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("specialize func first<i32>(values: ref{other}/[3 of i32]) -> ref{other}/i32 => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref{source}/[3 of i32]) -> ref{static}/i32 => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref{source}/[3 of i32]) -> ref{source}/i32 => values[2]@ref/i32\nspecialize func first<i32>(values: ref{source}/[3 of i32]) -> ref{source}/i32 => values[1]@ref/i32")]
    public void RejectsRenamedNarrowedOrDuplicateBinders(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Named + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func pick<2, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<i32, 3>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<N, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nspecialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 2")]
    public void RejectsInvalidLengthSlots(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Lengths + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i64>(value: ref/i64) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value => renamed: ref/i32) -> i32 => 2")]
    public void AcceptsClosedImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization + "\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = forward<i32>(value@ref/i32)");
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
        var c = MinimalEmissionTest.Analyze("func weight<T>(value: ref/T) -> i32 => true\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2\nlet value: i32 = 4\nlet result = weight<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void CannotUseResultsOrNamesToResolveTargetAmbiguity()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(value: T) -> i32 => 1\nfunc f<T>(other: i32) -> bool => true\nspecialize func f<i32>(value: i32) -> i32 => 2\n()");
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
