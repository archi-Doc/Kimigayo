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
    /// <exception cref="InvalidDataException">The test target cannot be selected.</exception>
    internal Compilation? PrepareTests(CancellationToken cancellationToken, CheckContext? context)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The product build's preparation sequence (BuildTargets) with the test settings: each failure is its own record (SPEC 23.1.2),
        // blocking the check before the front end (SPEC 23.2.2); a command renders them.
        var diagnostics = context?.Diagnostics ?? new DiagnosticOwner();
        try
        {
            if (!this.ValidateConfiguration(diagnostics, out var sources))
            {
                return null;
            }

            var valid = !this.Reported(diagnostics, this.ProjectFile.Test.Validate());
            if (Testing.TestCatalog.TryProjectIdOf(this) is null)
            {
                this.Fail(diagnostics, Testing.TestCatalog.InvalidProjectId);
                valid = false;
            }

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

            Compilation? result = null;
            return this.BuildTarget(target, valid, false, null, sources, cancellationToken, c => result = c, context) ? result : null;
        }
        finally
        {
            this.Publish(diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Input, context);
        }
    }
}
