// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand(EmitCommand.Name)]
public sealed class EmitCommand : ISimpleCommand
{
    public const string Name = "emit";

    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public EmitCommand(ILogger<EmitCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(string[] arguments, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, Name, arguments, async (options, args) =>
        {
            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Generate(cancellationToken) ? 0 : 1;
        });
    }
}
