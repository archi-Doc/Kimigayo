// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AssociatedForwardingTest
{
    internal static string MilestoneSource => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone19.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void MilestoneBinds()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void MilestoneOwnershipIsVerified()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("NumberSource", "i32")]
    [InlineData("Wrapper<NumberSource>", "i32")]
    [InlineData("FlagSource", "bool")]
    [InlineData("Wrapper<FlagSource>", "bool")]
    public void NormalizesAssociatedIdentityAfterEachContainerSubstitution(string inner, string result)
    {
        var source = MilestoneSource[..MilestoneSource.IndexOf("public func main", StringComparison.Ordinal)] +
            $"func project(value: Wrapper<{inner}>.Source.Element) -> {result} => value\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("wrongEquality")]
    [InlineData("missingPremise")]
    [InlineData("missingAssociated")]
    [InlineData("exclusiveReceiver")]
    [InlineData("missingDefinitionPremise")]
    public void RejectsInvalidEvidence(string mutation)
    {
        var source = MilestoneSource;
        source = mutation switch
        {
            "wrongEquality" => source.Replace("let nested =", "let invalid = readNumber(flag@ref)\n    let nested =", StringComparison.Ordinal),
            "missingPremise" => source.Replace("    Console.writeLine(\"Contract forwarding finished.\")", "    let invalid = readTwice(storageOnly@ref)", StringComparison.Ordinal),
            "missingAssociated" => source.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal),
            "exclusiveReceiver" => source.Replace("public func read(self: ref/Self) -> i32", "public func read(self: uniq/Self) -> i32", StringComparison.Ordinal),
            _ => source.Replace("    T is Source\n    let first", "    let first", StringComparison.Ordinal),
        };
        Assert.NotEqual(MilestoneSource, source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
