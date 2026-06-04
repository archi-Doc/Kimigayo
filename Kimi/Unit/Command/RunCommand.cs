// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimigayo.Command;

[SimpleCommand("run")]
public class RunCommand : ISimpleCommand<SolutionOptions>
{
    private readonly UnitContext unitContext;
    private readonly ILogger logger;
    private readonly KimiControl kimiControl;
    private readonly Solution solution;

    public RunCommand(UnitContext unitContext, ILogger<RunCommand> logger, KimiControl kimiControl, Solution solution)
    {
        this.unitContext = unitContext;
        this.logger = logger;
        this.kimiControl = kimiControl;
        this.solution = solution;
    }

    public async Task Execute(SolutionOptions options, string[] args, CancellationToken cancellationToken)
    {
        Console.WriteLine("Run");
        this.solution.LoadForRun(this.logger, options, args);
        this.solution.PrepareProject(this.logger);
        await this.solution.Build();
    }
}
