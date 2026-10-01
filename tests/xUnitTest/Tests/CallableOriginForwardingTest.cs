// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CallableOriginForwardingTest
{
    [Theory]
    [InlineData("i32", "42", "value")]
    [InlineData("(i32, i32)", "(20, 22)", "value.0 + value.1")]
    public void EquivalentPerCallContractsForward(string type, string value, string read)
    {
        var source = "func invoke(f: (ref/" + type + ") -> i32, value: ref/" + type + ") -> i32 => f(value)\n" +
            "func forward(f: (ref/" + type + ") -> i32, value: ref/" + type + ") -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: ref/" + type + ") -> i32 => " + read + "\n" +
            "let f: (ref/" + type + ") -> i32 = callback\nlet value = " + value + "\n" +
            "require forward(f@move, value@ref) == 42 else => $abort(\"forward\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForward" + (type == "i32" ? "Scalar" : "Tuple"), source, string.Empty);
    }

    [Fact]
    public void GenericForwardingKeepsThePerCallBinderLocal()
    {
        const string Source = "func invoke<T>(f: (ref/T) -> i32, value: ref/T) -> i32 => f(value)\n" +
            "func forward<T>(f: (ref/T) -> i32, value: ref/T) -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: ref/(i32, i32)) -> i32 => value.0 + value.1\n" +
            "let f: (ref/(i32, i32)) -> i32 = callback\nlet value = (20, 22)\n" +
            "require forward(f@move, value@ref) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForwardGeneric", Source, string.Empty);
    }

    [Fact]
    public void ExclusiveInputsKeepTheirAccessRequirement()
    {
        const string Source = "func invoke(f: (uniq/(i32, i32)) -> i32, value: uniq/(i32, i32)) -> i32 => f(value)\n" +
            "func forward(f: (uniq/(i32, i32)) -> i32, value: uniq/(i32, i32)) -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: uniq/(i32, i32)) -> i32\n    value.0 += 1\n    return value.0 + value.1\n" +
            "let f: (uniq/(i32, i32)) -> i32 = callback\nvar value = (20, 21)\n" +
            "require forward(f@move, value@uniq) == 42 and value.0 == 21 else => $abort(\"exclusive\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForwardExclusive", Source, string.Empty);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("uniq/i32")]
    [InlineData("ref/bool")]
    [InlineData("ref/i32, ref/i32")]
    public void ForwardingCannotEraseFixedOriginsModesOrParameterShapes(string input)
    {
        var c = MinimalEmissionTest.Analyze("func accept(f: (ref/i32) -> i32) => ()\nfunc forward(f: (" + input + ") -> i32) => accept(f@move)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Failure == BindingFailure.NoApplicableCandidate);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmForwardingDoesNotAllocate()
    {
        const string Source = "func invoke(f: (ref/i32) -> i32, value: ref/i32) -> i32 => f(value)\n" +
            "let callback = func (value: ref/i32) -> i32 => value\n" +
            "let f: (ref/i32) -> i32 = callback\nlet value = 42\ninvoke(f@move, value@ref)";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, allocated);
    }
}
