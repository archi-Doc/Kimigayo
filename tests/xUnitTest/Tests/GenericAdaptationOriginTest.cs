// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class GenericAdaptationOriginTest
{
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void CreationPreservesTheCompletePayloadAndItsLoan(string mode)
    {
        var source = "struct View{source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n" +
            "func box<s/T>(value: View) -> s/View{result}\n    s is object\n    origin result.source == value.source\n    return value@move@s\n" +
            $"var number = 7\nlet handle = box<{mode}/i32>(View.init(number@ref))\n" +
            "require handle.value@follow == 7 else => $abort(\"loan\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationLoan" + mode, source, 1, 1, 24);
        var invalid = MinimalEmissionTest.Analyze(source.Replace("require handle.value", "number = 8\nrequire handle.value", StringComparison.Ordinal));
        Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
        Assert.False(invalid.Ownership.Result.IsVerified);
        Assert.False(invalid.Emission.WriteIr(TextWriter.Null, out _));
    }
}
