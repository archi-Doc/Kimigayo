// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimigayo.Command;

[SimpleCommand("build")]
public class BuildCommand : ISimpleCommand<BuildCommand.Options>
{
    public class Options
    {
        [SimpleOption("DumpToken")]
        public bool DumpToken { get; set; } = false;
    }

    private readonly UnitContext unitContext;
    private readonly ILogger logger;

    public BuildCommand(UnitContext unitContext, ILogger<BuildCommand> logger)
    {
        this.unitContext = unitContext;
        this.logger = logger;
        // logger.GetWriter()?.Write("Default command");
    }

    public async Task Execute(Options options, string[] args, CancellationToken cancellationToken)
    {
        foreach (var x in args)
        {
        }

        //var kimiControl = serviceProvider.GetRequiredService<KimiControl>();
        //var solution = serviceProvider.GetRequiredService<Solution>();
        // solution.TryReadFile("aaa");
    }
}
