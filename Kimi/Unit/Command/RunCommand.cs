// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("run")]
public class RunCommand : ISimpleCommand<KimiOptions>
{
    private readonly UnitContext unitContext;
    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public RunCommand(UnitContext unitContext, ILogger<RunCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.unitContext = unitContext;
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        this.solution.LoadForRun(this.logger, options, args);
        this.solution.PrepareProject(this.logger);
        await this.solution.Build();
    }
}
