// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("restore")]
public sealed class RestoreCommand : ISimpleCommand<KimiOptions>
{
    private readonly Kimigayo kimigayo;

    public RestoreCommand(Kimigayo kimigayo) => this.kimigayo = kimigayo;

    public async Task Execute(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(this.kimigayo, () =>
        {
            if (args.Length != 1)
            {
                throw new InvalidDataException("Restore requires one .kimiproj input.");
            }

            var path = Solution.ResolveInputPath(args[0]);
            if (!path.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Restore requires a .kimiproj input.");
            }

            var resolution = DependencyResolver.Resolve(path, options.Target, Compilation.CurrentLanguageVersion, cancellationToken);
            var lockPath = DependencyLock.PathForProject(path);
            var changed = DependencyLock.Update(lockPath, resolution, cancellationToken);
            if (!resolution.Product.IsResolved)
            {
                this.kimigayo.WriteLine(DiagnosticSeverity.Error, resolution.Product.Diagnostic ?? resolution.Product.ReasonCode!);
            }
            else if (!resolution.Test.IsResolved)
            {
                this.kimigayo.WriteLine(DiagnosticSeverity.Warning, $"Test dependency resolution failed: {resolution.Test.Diagnostic ?? resolution.Test.ReasonCode}");
            }

            this.kimigayo.WriteLine(DiagnosticSeverity.Information, $"Dependency lock {(changed ? "updated" : "unchanged")}: {lockPath}");
            return Task.FromResult(resolution.Product.IsResolved ? 0 : 1);
        });
    }
}
