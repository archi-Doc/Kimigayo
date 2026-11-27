// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using SimpleCommandLine;
using Xunit;

namespace XunitTest;

// The command line's one option table and reader (KimiOptions.Read; IMPL 20.3, 20.3.2, 20.5, 20.6.2; the owner's O-525, O-526 and
// O-527; PLAN G1236, G1237), on the words the parser hands a command for a line.
public class CommandOptionsTest
{
    // A row: the command, its line, the values read ("name=value;...", in the table's spelling), the inputs and the invalid options'
    // texts ("|" between two). Names ignore case (O-525); test reads the one table (G1236); listed values, including Booleans, ignore case
    // and keep whitespace significant (IMPL 20.3); --locked and --no-build alone mean true
    // (O-525); a value is never an option word, so a removed name after --filter leaves it without its value; a removed option is
    // invalid with any value and every other invalid option is reported where it is read; a repeated option is invalid in any spelling
    // (O-526 Q3); each command takes its rows (IMPL 20.3, 20.5, 20.6.2; restore's --Target, O-527); a word of dashes alone is an input,
    // and an option after it is read (O-526 Q2).
    [Theory]
    [InlineData("check", "T --format json", "--Format=json", "T", "")]
    [InlineData("check", "T --FORMAT JSON", "--Format=json", "T", "")]
    [InlineData("test", "T --format json --list", "--Format=json;--list=true", "T", "")]
    [InlineData("test", "T --Formats json --list", "--list=true", "T json", "Unknown option: --Formats")]
    [InlineData("test", "T --target w --Debug TRUE --list", "--Target=w;--Debug=true;--list=true", "T", "")]
    [InlineData("test", "T --Targets w", "", "T w", "Unknown option: --Targets")]
    [InlineData("check", "T --Debug \" true\"", "", "T", "Invalid value: --Debug  true")]
    [InlineData("check", "--locked \" false\"", "--locked=true", " false", "")]
    [InlineData("check", "T --Format \" json\"", "", "T", "Invalid value: --Format  json")]
    [InlineData("check", "T --Format yaml", "", "T", "Invalid value: --Format yaml")]
    [InlineData("check", "T --Debug yes", "", "T", "Invalid value: --Debug yes")]
    [InlineData("check", "T --Debug", "", "T", "Missing value: --Debug")]
    [InlineData("run", "T --no-build", "--no-build=true", "T", "")]
    [InlineData("check", "--locked T", "--locked=true", "T", "")]
    [InlineData("check", "T --locked FALSE", "--locked=false", "T", "")]
    [InlineData("build", "--locked --Target \"a target\" --Debug true --Manifest \"folder with spaces/app.link.json\"", "--locked=true;--Target=a target;--Debug=true;--Manifest=folder with spaces/app.link.json", "", "")]
    [InlineData("test", "T --filter passes --list", "--filter=passes;--list=true", "T", "")]
    [InlineData("test", "T --list true", "--list=true", "T true", "")]
    [InlineData("test", "T --filter --LlvmBin --list", "--list=true", "T", "Missing value: --filter|--LlvmBin was removed: Kimi carries its toolchain.")]
    [InlineData("check", "T --Target --LlvmBin", "", "T", "Missing value: --Target|--LlvmBin was removed: Kimi carries its toolchain.")]
    [InlineData("check", "T --LlvmBin a --LlvmBin b", "", "T a b", "--LlvmBin was removed: Kimi carries its toolchain.|--LlvmBin was removed: Kimi carries its toolchain.")]
    [InlineData("check", "T --bogus --Target=x", "", "T", "Unknown option: --bogus|Unknown option: --Target=x")]
    [InlineData("check", "T --Debug true --debug false", "--Debug=true", "T", "Repeated option: --Debug")]
    [InlineData("check", "T --Debug yes --debug true", "--Debug=true", "T", "Invalid value: --Debug yes|Repeated option: --Debug")]
    [InlineData("check", "T --Debug true --debug yes", "--Debug=true", "T", "Repeated option: --Debug|Invalid value: --Debug yes")]
    [InlineData("check", "T --Debug --Debug", "", "T", "Missing value: --Debug|Repeated option: --Debug|Missing value: --Debug")]
    [InlineData("test", "T --Format json --Format text", "--Format=json", "T", "Repeated option: --Format")]
    [InlineData("restore", "T --Target w", "--Target=w", "T", "")]
    [InlineData("build", "T --Format json", "", "T json", "Unknown option: --Format")]
    [InlineData("restore", "T --Debug true --locked", "", "T true", "Unknown option: --Debug|Unknown option: --locked")]
    [InlineData("toolchain", "verify --Report r", "--Report=r", "verify", "")]
    [InlineData("tatara", "--no-build", "", "", "Unknown option: --no-build")]
    [InlineData("run", "T -- x", "", "T -- x", "")]
    [InlineData("run", "T -- --no-build", "--no-build=true", "T --", "")]
    public void AnArgumentIsReadByTheTable(string command, string line, string values, string inputs, string errors)
    {
        var read = KimiOptions.Read(command, Words(line), out var words, out var invalid);
        Assert.Equal(values, string.Join(';', read.Select(x => x.Key + "=" + x.Value)));
        Assert.Equal(inputs, string.Join(' ', words));
        Assert.Equal(errors, string.Join('|', invalid));
    }

    // An option and a removed option are named by one rule (T-M15's Reading, now the reader's own): an option word, after its leading
    // dashes, equals the row's name ignoring case (O-525, O-526 Q1). A quoted word arrives unwrapped, as the parser hands it on; a word
    // without a dash is an input, and --Target=x names no row.
    [Theory]
    [InlineData("--Target", true)]
    [InlineData("-Target", true)]
    [InlineData("--TARGET", true)]
    [InlineData("---target", true)]
    [InlineData("\"--Target\"", true)]
    [InlineData("Target", false)]
    [InlineData("--Target=x", false)]
    public void ARemovedOptionIsNamedAsTheReaderBindsAnOption(string spelling, bool binds)
    {
        var values = KimiOptions.Read(CheckCommand.Name, Words(spelling + " value"), out _, out _);
        Assert.Equal(binds, values.GetValueOrDefault("--Target") == "value");
        var removed = spelling.Replace("Target", "LlvmBin", StringComparison.OrdinalIgnoreCase);
        _ = KimiOptions.Read(CheckCommand.Name, Words(removed + " value"), out _, out var errors);
        Assert.Equal(binds, errors.Contains("--LlvmBin was removed: Kimi carries its toolchain."));
    }

    // The compiler options among a command's values (KimiOptions.From): each row's property, its default where it is absent.
    [Fact]
    public void TheCompilerOptionsAreTheValuesRead()
    {
        var options = KimiOptions.From(KimiOptions.Read(RunCommand.Name, ["--no-build", "--locked", "--Target", "folder with spaces", "--Debug", "true"], out _, out _));
        Assert.Equal((true, true, true, "folder with spaces", "text", null), (options.NoBuild, options.Locked, options.Debug, options.Target, options.Format, options.Manifest));
        options = KimiOptions.From(KimiOptions.Read(BuildCommand.Name, ["--Manifest", "app.link.json"], out _, out _));
        Assert.Equal((false, false, false, string.Empty, "app.link.json"), (options.NoBuild, options.Locked, options.Debug, options.Target, options.Manifest));
    }

    // The words the parser hands a command for the line (CommandUnit's options: split, and a quoted word unwrapped).
    private static string[] Words(string line)
    {
        var parser = new SimpleParser([typeof(ProbeCommand)], SimpleParserOptions.Standard with { RejectUnknownOptionNames = false, SuppressConsoleOutput = true });
        Assert.True(parser.Parse("probe " + line));
        return parser.CurrentCommand!.OptionSet.RemainingArguments!;
    }

    // A command that takes its arguments as every Kimi command does, with no dependency, so a parser built without a service provider
    // can create it.
    [SimpleCommand("probe")]
    public sealed class ProbeCommand : ISimpleCommand
    {
        public Task Execute(string[] args, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
