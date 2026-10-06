// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using SimpleCommandLine;
using Xunit;

namespace XunitTest;

public class CommandOptionsTest
{
    [Fact]
    public void ManifestBuildPreservesPathsAndToolOverrides()
    {
        Assert.True(SimpleParser.TryParseOptions<BuildCommand.Options>(["--Manifest", "folder with spaces/app.link.json", "--ToolchainRoot", "tools", "--LlvmBin", "LLVM tools"], out var options));
        Assert.Equal("folder with spaces/app.link.json", options!.Manifest);
        Assert.Equal("tools", options.ToolchainRoot);
        Assert.Equal("LLVM tools", options.LlvmBin);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true, "--no-build")]
    [InlineData(true, "-no-build")]
    [InlineData(true, "--NO-BUILD")]
    [InlineData(true, "--no-build", "true")]
    [InlineData(false, "--no-build", "false")]
    public void NoBuildSpellingReachesTheActualOptionParser(bool expected, params string[] arguments)
    {
        Assert.True(SimpleParser.TryParseOptions<RunCommand.Options>(KimiOptions.ExpandFlags(arguments), out var options));
        Assert.Equal(expected, options!.NoBuild);
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

    [Theory]
    [InlineData("--no-build --ToolchainRoot \"folder with spaces\" --Debug true", false)]
    [InlineData("--no-build --locked --ToolchainRoot \"folder with spaces\" --Debug true", true)]
    public void RunFlagsPreserveInheritedOptionsAndQuotedPaths(string commandLine, bool locked)
    {
        Assert.True(SimpleParser.TryParseOptions<RunCommand.Options>(KimiOptions.ExpandFlags(commandLine), out var options));
        Assert.True(options!.NoBuild);
        Assert.Equal(locked, options.Locked);
        Assert.True(options.Debug);
        Assert.Equal("folder with spaces", options.ToolchainRoot);
    }

    [Fact]
    public void NoBuildExpansionStopsAtTheArgumentSeparator()
    {
        Assert.Equal(["--no-build", "true", "--", "--no-build"], KimiOptions.ExpandFlags(["--no-build", "--", "--no-build"]));
    }
}
