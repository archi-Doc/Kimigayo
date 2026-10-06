// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("build")]
public class BuildCommand : ISimpleCommand<BuildCommand.Options>
{
    public class Options : KimiOptions
    {
        [SimpleOption("Manifest")]
        public string? Manifest { get; set; }
    }

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

    public async Task Execute(Options options, string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, async () =>
        {
            if (options.Manifest is { } manifest)
            {
                if (args.Length != 0 || options.Target.Length != 0 || options.Debug || options.Locked)
                {
                    throw new InvalidDataException("Use kimi build --Manifest <path> without source, project, target, debug or lock options; generation settings come from the manifest.");
                }

                await Compiler.NativeToolchain.BuildManifest(manifest, options.ToolchainRoot, options.LlvmBin, this.kimigayo.WriteLine, cancellationToken);
                return 0;
            }

            this.solution.LoadForBuild(this.logger, options, args);
            this.solution.PrepareProject(this.logger);
            return await this.solution.Build(cancellationToken) ? 0 : 1;
        });
    }
}
