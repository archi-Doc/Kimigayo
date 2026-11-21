// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using SimpleCommandLine;
using Xunit;

namespace XunitTest;

public class CommandOptionsTest
{
    [Fact]
    public void ManifestBuildPreservesPathsAndOptionValues()
    {
        Assert.True(SimpleParser.TryParseOptions<BuildCommand.Options>(["--Manifest", "folder with spaces/app.link.json", "--Target", "a target"], out var options));
        Assert.Equal("folder with spaces/app.link.json", options!.Manifest);
        Assert.Equal("a target", options.Target);
    }

    // A removed option is named by SimpleCommandLine's own option rule (Tatara B6b4, C4): each spelling the parser binds as an option,
    // here Target's, names a removed option when it carries the removed name, and a word the parser does not bind names none.
    [Theory]
    [InlineData("--Target", true)]
    [InlineData("-Target", true)]
    [InlineData("--TARGET", true)]
    [InlineData("---target", true)]
    [InlineData("Target", false)]
    [InlineData("--Target=x", false)]
    [InlineData("\"--Target\"", false)]
    public void ARemovedOptionIsNamedAsTheParserBindsAnOption(string spelling, bool binds)
    {
        Assert.True(SimpleParser.TryParseOptions<KimiOptions>(spelling + " value", out var options));
        Assert.Equal(binds, options!.Target == "value");
        string[] expected = binds ? ["--LlvmBin was removed: Kimi carries its toolchain."] : [];
        Assert.Equal(expected, KimiOptions.RemovedOptions(spelling.Replace("Target", "LlvmBin", StringComparison.OrdinalIgnoreCase) + " value"));
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
        var command = KimiOptions.ExpandFlags("--locked --Target \"folder with spaces\" --Debug true");
        Assert.True(SimpleParser.TryParseOptions<KimiOptions>(command, out var options));
        Assert.True(options!.Locked);
        Assert.True(options.Debug);
        Assert.Equal("folder with spaces", options.Target);
    }

    [Theory]
    [InlineData("--no-build --Target \"folder with spaces\" --Debug true", false)]
    [InlineData("--no-build --locked --Target \"folder with spaces\" --Debug true", true)]
    public void RunFlagsPreserveInheritedOptionsAndQuotedPaths(string commandLine, bool locked)
    {
        Assert.True(SimpleParser.TryParseOptions<RunCommand.Options>(KimiOptions.ExpandFlags(commandLine), out var options));
        Assert.True(options!.NoBuild);
        Assert.Equal(locked, options.Locked);
        Assert.True(options.Debug);
        Assert.Equal("folder with spaces", options.Target);
    }

    [Fact]
    public void NoBuildExpansionStopsAtTheArgumentSeparator()
    {
        Assert.Equal(["--no-build", "true", "--", "--no-build"], KimiOptions.ExpandFlags(["--no-build", "--", "--no-build"]));
    }
}
