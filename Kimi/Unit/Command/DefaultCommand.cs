// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimigayo.Command;

[SimpleCommand("Default", Default = true)]
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
        Console.WriteLine($"Kimigayo ({Arc.VersionHelper.VersionString}) by archi-Doc");
    }
}
