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

    // A removed option is named by SimpleCommandLine's own option rule (Tatara B6b4, C4) among the arguments the parser hands its
    // command (C-449 B1): each spelling the parser binds as an option, here Target's, names a removed option when it carries the
    // removed name, and a word the parser does not bind names none, except a quoted one: the parser hands it to the command unwrapped.
    [Theory]
    [InlineData("--Target", true, true)]
    [InlineData("-Target", true, true)]
    [InlineData("--TARGET", true, true)]
    [InlineData("---target", true, true)]
    [InlineData("Target", false, false)]
    [InlineData("--Target=x", false, false)]
    [InlineData("\"--Target\"", false, true)]
    public void ARemovedOptionIsNamedAsTheParserBindsAnOption(string spelling, bool binds, bool named)
    {
        var parser = new SimpleParser([typeof(ProbeCommand)], SimpleParserOptions.Standard with { SuppressConsoleOutput = true });
        Assert.True(parser.Parse($"probe {spelling} value"));
        Assert.Equal(binds, ((KimiOptions)parser.CurrentCommand!.OptionSet.Instance!).Target == "value");
        Assert.True(parser.Parse($"probe {spelling.Replace("Target", "LlvmBin", StringComparison.OrdinalIgnoreCase)} value"));
        string[] expected = named ? ["--LlvmBin was removed: Kimi carries its toolchain."] : [];
        Assert.Equal(expected, parser.CurrentCommand!.OptionSet.RemainingArguments!.Select(KimiOptions.RemovedOption).OfType<string>());
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

    // A command with the commands' options and no dependency, so a parser built without a service provider can create it.
    [SimpleCommand("probe")]
    public sealed class ProbeCommand : ISimpleCommand<KimiOptions>
    {
        public Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
