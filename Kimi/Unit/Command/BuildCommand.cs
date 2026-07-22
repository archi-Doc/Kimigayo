// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("build")]
public class BuildCommand : ISimpleCommand<KimiOptions>
{
    private readonly UnitContext unitContext;
    private readonly ILogger logger;
    private readonly KimiControl kimiControl;
    private readonly Solution solution;

    public BuildCommand(UnitContext unitContext, ILogger<BuildCommand> logger, KimiControl kimiControl, Solution solution)
    {
        this.unitContext = unitContext;
        this.logger = logger;
        this.kimiControl = kimiControl;
        this.solution = solution;
    }

    public async Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        this.solution.LoadForBuild(this.logger, options, args);
        this.solution.PrepareProject(this.logger);
    }
}
