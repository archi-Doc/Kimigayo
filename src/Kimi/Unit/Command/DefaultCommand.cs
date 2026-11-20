// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("Default", IsDefault = true)]
public class DefaultCommand(Kimigayo kimigayo) : ISimpleCommand
{
    public async Task Execute(string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(kimigayo, () =>
        {
            if (args.Length != 0)
            {
                throw new CommandInputException($"Unknown command '{args[0]}'. Use restore, check, build, run, test, emit, toolchain verify, tatara, or lsp.");
            }

            Console.WriteLine($"Kimi ({Compiler.CompilerRelease.Version}) by archi-Doc");
            return Task.FromResult(0);
        });
    }
}
