// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("Default", IsDefault = true)]
public class DefaultCommand : ISimpleCommand
{
    private readonly UnitContext unitContext;

    public DefaultCommand(UnitContext unitContext, ILogger<DefaultCommand> logger)
    {
        this.unitContext = unitContext;
        // logger.GetWriter()?.Write("Default command");
    }

    public async Task Execute(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'. Use build, run, emit, or lsp.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Kimi ({Compiler.CompilerRelease.Version}) by archi-Doc");
    }
}
