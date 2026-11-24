// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand(CheckCommand.Name)]
public sealed class CheckCommand : ISimpleCommand
{
    public const string Name = "check";

    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public CheckCommand(ILogger<CheckCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(string[] arguments, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, Name, arguments, async (options, args) =>
        {
            if (options.Format == "json")
            {
                // SPEC 23.3: one check unit through the shared check entry, written as one JSON document.
                return CheckJsonOutput.Run(options, args, cancellationToken);
            }

            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Check(cancellationToken) ? 0 : 1;
        });
    }
}
