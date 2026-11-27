// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Build;
using Kimi.Compiler;
using Kimi.Diagnostics;
using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand(RestoreCommand.Name)]
public sealed class RestoreCommand : ISimpleCommand
{
    public const string Name = "restore";

    private readonly Kimigayo kimigayo;

    public RestoreCommand(Kimigayo kimigayo) => this.kimigayo = kimigayo;

    public async Task Execute(string[] arguments, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, Name, arguments, (options, args) =>
        {
            if (args.Length != 1)
            {
                throw new CommandInputException("Restore requires one .kimiproj input.");
            }

            var path = Solution.ResolveInputPath(args[0]);
            if (!path.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandInputException("Restore requires a .kimiproj input.");
            }

            var resolution = DependencyResolver.Resolve(path, options.Target, Compilation.CurrentLanguageVersion, cancellationToken);
            var lockPath = DependencyLock.PathForProject(path);
            bool changed;
            try
            {
                changed = DependencyLock.Update(lockPath, resolution, cancellationToken);
            }
            catch (Exception ex) when (ArtifactFiles.IsOutputFailure(ex))
            {
                // The lock is restore's output: its failure fails the command with its message (IMPL 20.3).
                this.kimigayo.WriteLine(DiagnosticSeverity.Error, ex.Message);
                return Task.FromResult(1);
            }

            // IMPL 20.5: a product failure fails restore; a test-only failure is reported. Each failure is its own line (SPEC 23.1.2).
            foreach (var failure in resolution.Product.Failures ?? [])
            {
                this.kimigayo.WriteLine(DiagnosticSeverity.Error, failure.Message);
            }

            foreach (var failure in resolution.Product.IsResolved ? resolution.Test.Failures ?? [] : [])
            {
                this.kimigayo.WriteLine(DiagnosticSeverity.Warning, $"Test dependency resolution failed: {failure.Message}");
            }

            this.kimigayo.WriteLine(DiagnosticSeverity.Information, $"Dependency lock {(changed ? "updated" : "unchanged")}: {lockPath}");
            return Task.FromResult(resolution.Product.IsResolved ? 0 : 1);
        });
    }
}
