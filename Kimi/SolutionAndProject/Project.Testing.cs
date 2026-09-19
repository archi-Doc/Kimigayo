// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi;

public partial class Project
{
    internal Compilation? PrepareTests(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DependencyConfiguration.Validate(this.ProjectFile) is { } invalid)
        {
            throw new InvalidDataException(invalid);
        }

        this.ProjectFile.Test.Validate();
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

        var sources = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var source in this.ProjectFile.TestSources)
        {
            if (!sources.Add(Path.GetFullPath(source, Path.GetFullPath(this.Directory))))
            {
                throw new InvalidDataException("Duplicate resolved TestSources path: " + source);
            }
        }

        DependencyPartition? graph = null;
        if (this.FilePath is { } path)
        {
            var resolution = DependencyResolver.Resolve(path, target, this.ProjectFile.LangVersion ?? this.SolutionLanguageVersion ?? Compilation.CurrentLanguageVersion, cancellationToken, TinyhandSerializer.SerializeToUtf8(this.ProjectFile));
            if (DependencyLock.Validate(DependencyLock.PathForProject(path), resolution, true) is { } failure)
            {
                throw new InvalidDataException(failure);
            }

            graph = resolution.Test;
        }

        Compilation? result = null;
        return this.BuildTarget(target, false, null, sources, graph, c => result = c) ? result : null;
    }
}
