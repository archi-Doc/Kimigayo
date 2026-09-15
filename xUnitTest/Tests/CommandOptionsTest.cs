// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using SimpleCommandLine;
using Xunit;

namespace XunitTest;

public class CommandOptionsTest
{
    [Theory]
    [InlineData(true, "--locked")]
    [InlineData(true, "-locked")]
    [InlineData(true, "--LOCKED")]
    [InlineData(true, "--locked", "true")]
    [InlineData(false, "--locked", "false")]
    public void LockedSpellingReachesTheActualOptionParser(bool expected, params string[] arguments)
    {
        Assert.True(SimpleParser.TryParseOptions<KimiOptions>(KimiOptions.ExpandFlags(arguments), out var options));
        Assert.Equal(expected, options!.Locked);
    }

    [Fact]
    public void BareFlagDoesNotConsumeTheFollowingOption()
    {
        Assert.True(SimpleParser.TryParseOptions<KimiOptions>(KimiOptions.ExpandFlags(["--locked", "--Target", "target", "--Debug", "true"]), out var options));
        Assert.True(options!.Locked);
        Assert.True(options.Debug);
        Assert.Equal("target", options.Target);
    }

    [Fact]
    public void RawCommandLinePreservesQuotedPaths()
    {
        var command = KimiOptions.ExpandFlags("--locked --ToolchainRoot \"folder with spaces\" --Debug true");
        Assert.True(SimpleParser.TryParseOptions<KimiOptions>(command, out var options));
        Assert.True(options!.Locked);
        Assert.True(options.Debug);
        Assert.Equal("folder with spaces", options.ToolchainRoot);
    }
}
