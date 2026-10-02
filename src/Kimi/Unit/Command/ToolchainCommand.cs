// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("toolchain")]
public sealed class ToolchainCommand : ISimpleCommand<ToolchainCommand.Options>
{
    public sealed class Options
    {
        [SimpleOption("ToolchainRoot")]
        public string? ToolchainRoot { get; set; }

        [SimpleOption("LlvmBin")]
        public string? LlvmBin { get; set; }

        [SimpleOption("Report")]
        public string? Report { get; set; }
    }

    private readonly Kimigayo kimigayo;

    public ToolchainCommand(Kimigayo kimigayo) => this.kimigayo = kimigayo;

    public async Task Execute(Options options, string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, async () =>
        {
            if (args.Length != 1 || args[0] != "verify")
            {
                throw new InvalidDataException("Use kimi toolchain verify [--ToolchainRoot <path>] [--LlvmBin <path>] [--Report <path>].");
            }

            await NativeToolchain.Verify(options.ToolchainRoot, options.LlvmBin, options.Report, this.kimigayo.WriteLine, cancellationToken);
            this.kimigayo.WriteLine(DiagnosticSeverity.Information, "Toolchain verification passed.");
            return 0;
        });
    }
}
