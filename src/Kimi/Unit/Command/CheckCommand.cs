// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("check")]
public sealed class CheckCommand : ISimpleCommand<KimiOptions>
{
    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public CheckCommand(ILogger<CheckCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, async () =>
        {
            if (string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase))
            {
                // SPEC 23.3: one check unit through the shared check entry, written as one JSON document.
                return CheckJsonOutput.Run(options, args, cancellationToken);
            }

            if (!string.Equals(options.Format, "text", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandInputException($"Unknown --Format '{options.Format}'; the forms are text and json.");
            }

            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Check(cancellationToken) ? 0 : 1;
        });
    }
}
