// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("emit-llvm")]
public sealed class EmitLlvmCommand : ISimpleCommand<KimiOptions>
{
    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public EmitLlvmCommand(ILogger<EmitLlvmCommand> logger, Kimigayo kimigayo, Solution solution)
    {
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
            return await this.solution.Generate(cancellationToken) ? 0 : 1;
        });
    }
}
