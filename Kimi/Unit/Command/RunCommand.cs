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
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, async () =>
        {
            if (args.Length == 1 && Path.GetExtension(args[0]).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return await Compiler.NativeToolchain.RunExecutable(Path.GetFullPath(args[0]), Directory.GetCurrentDirectory(), cancellationToken);
            }

            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Run(cancellationToken);
        });
    }
}
