// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi;

public partial class Project
{
    internal Compilation? PrepareTests(CancellationToken cancellationToken)
        => this.PrepareTests(cancellationToken, null);

    /// <summary>Prepares and checks the test build through the shared check entry (SPEC 23.2.2).</summary>
    /// <param name="cancellationToken">Cancels dependency resolution.</param>
    /// <param name="context">The check context, or null for the command, which reads the disk.</param>
    /// <returns>The accepted test compilation, or null.</returns>
    /// <exception cref="InvalidDataException">The test settings, identity, target or lock is invalid.</exception>
    internal Compilation? PrepareTests(CancellationToken cancellationToken, CheckContext? context)
    {
        var inputs = context?.Inputs ?? CheckInputSource.Disk;
        cancellationToken.ThrowIfCancellationRequested();

        // As for a product build, each configuration failure is its own record (SPEC 23.1.2); a command renders them here.
        var diagnostics = context?.Diagnostics ?? new DiagnosticOwner();
        if (!this.ValidateConfiguration(diagnostics, out var sources))
        {
            this.Publish(diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Input, context);
            return null;
        }

        this.ProjectFile.Test.Validate();
        _ = Testing.TestCatalog.ProjectIdOf(this); // SPEC 23.2.2: an invalid configuration blocks the check before the front end.
        var target = this.KimiOptions.Target;
        if (string.IsNullOrEmpty(target))
        {
            if (this.ProjectFile.Targets.Length != 1)
            {
                throw new InvalidDataException("Select --Target when a test project has more than one configured target.");
            }

            target = this.ProjectFile.Targets[0];
        }

        if (target != WindowsProfile.Target || !this.ProjectFile.Targets.Contains(target, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Testing requires a configured Windows x64 target.");
        }

        DependencyPartition? graph = null;
        if (this.FilePath is { } path)
        {
            var resolution = DependencyResolver.Resolve(path, target, this.ProjectFile.LangVersion ?? this.SolutionLanguageVersion ?? Compilation.CurrentLanguageVersion, cancellationToken, TinyhandSerializer.SerializeToUtf8(this.ProjectFile), true, inputs);
            if (DependencyLock.Validate(DependencyLock.PathForProject(path), resolution, true, inputs) is { } failure)
            {
                throw new InvalidDataException(failure);
            }

            graph = resolution.Test;
        }

        Compilation? result = null;
        return this.BuildTarget(target, false, null, sources, graph, c => result = c, context) ? result : null;
    }
}
