// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectCallableOriginTest
{
    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void BorrowSignaturesSharePerCallOriginInstantiation(string mode)
    {
        var source = Program(mode);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ObjectCallableOrigin" + mode, source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void WarmContractAndCallChecksReuseStorage(string mode)
    {
        var c = MinimalEmissionTest.Analyze(Program(mode));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }

    private static string Program(string mode)
        => "func identity(value: " + mode + "/i32) -> " + mode + "/i32 => value\nfunc apply<F>(f: ref/F, value: " + mode + "/i32) -> " + mode + "/i32\n    F is Callable<(" + mode + "/i32) -> " + mode + "/i32>\n    return f(value)\nvar n = 3" + (mode.StartsWith("obj", StringComparison.Ordinal) ? "@obj" : string.Empty) + "\nlet direct = apply(identity, n@" + mode + ")\nrequire direct@follow == 3 else => $abort(\"item\")\nlet erased: (" + mode + "/i32) -> " + mode + "/i32 = identity\nlet indirect = erased(n@" + mode + ")\nrequire indirect@follow == 3 else => $abort(\"erased\")";
}
