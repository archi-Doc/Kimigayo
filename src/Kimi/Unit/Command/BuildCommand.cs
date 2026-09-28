// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("build")]
public class BuildCommand : ISimpleCommand<KimiOptions>
{
    private readonly UnitContext unitContext;
    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public BuildCommand(UnitContext unitContext, ILogger<BuildCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.unitContext = unitContext;
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, async () =>
        {
            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Build(cancellationToken) ? 0 : 1;
        });
    }
}
