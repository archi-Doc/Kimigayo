// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Build;
using Kimi.Diagnostics;
using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand(ToolchainCommand.Name)]
public sealed class ToolchainCommand : ISimpleCommand
{
    public const string Name = "toolchain";

    private readonly Kimigayo kimigayo;

    public ToolchainCommand(Kimigayo kimigayo) => this.kimigayo = kimigayo;

    public async Task Execute(string[] arguments, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, Name, arguments, async (options, args) =>
        {
            if (args.Length != 1 || args[0] != "verify")
            {
                throw new CommandInputException("Use kimi toolchain verify [--Report <path>].");
            }

            if (await CommandExecution.Toolchain(this.kimigayo, () =>
            {
                NativeToolchain.Verify(options.Report, this.kimigayo.WriteLine, cancellationToken);
                return Task.FromResult(0);
            }) != 0)
            {
                return 1;
            }

            this.kimigayo.WriteLine(DiagnosticSeverity.Information, "Toolchain verification passed.");
            return 0;
        });
    }
}
