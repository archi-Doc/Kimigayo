// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand(BuildCommand.Name)]
public class BuildCommand : ISimpleCommand
{
    public const string Name = "build";

    private readonly ILogger logger;
    private readonly Kimigayo kimigayo;
    private readonly Solution solution;

    public BuildCommand(ILogger<BuildCommand> logger, Kimigayo kimigayo, Solution solution)
    {
        this.logger = logger;
        this.kimigayo = kimigayo;
        this.solution = solution;
    }

    public async Task Execute(string[] arguments, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, Name, arguments, async (options, args) =>
        {
            if (options.Manifest is { } manifest)
            {
                if (args.Length != 0 || options.Target.Length != 0 || options.Debug || options.Locked)
                {
                    throw new CommandInputException("Use kimi build --Manifest <path> without source, project, target, debug or lock options; generation settings come from the manifest.");
                }

                return await CommandExecution.Toolchain(this.kimigayo, async () =>
                {
                    await Build.NativeToolchain.BuildManifest(manifest, this.kimigayo.WriteLine, cancellationToken);
                    return 0;
                });
            }

            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Build(cancellationToken) ? 0 : 1;
        });
    }
}
