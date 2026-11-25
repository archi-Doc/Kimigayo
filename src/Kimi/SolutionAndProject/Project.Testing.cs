// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi;

public partial class Project
{
    internal Compilation? PrepareTests(CancellationToken cancellationToken)
        => this.PrepareTests(cancellationToken, null);

    /// <summary>Prepares and checks the test build through the shared check entry (SPEC 23.2.2): the test unit's own result
    /// (<c>OwnResult</c>), each failure before the front end its own record.</summary>
    /// <param name="cancellationToken">Cancels dependency resolution.</param>
    /// <param name="context">The check context, or null for the command, which reads the disk.</param>
    /// <returns>The accepted test compilation, or null.</returns>
    internal Compilation? PrepareTests(CancellationToken cancellationToken, CheckContext? context)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return this.OwnResult((cancellationToken, context), context, static (project, s, diagnostics) => project.PrepareTests(s.cancellationToken, s.context, diagnostics));
    }

    // The product build's preparation sequence (BuildTargets) with the test settings: each failure is its own record (SPEC 23.1.2),
    // blocking the check before the front end (SPEC 23.2.2); a command renders them. The test target is the project's own selection
    // (SelectTargets) and then one target with a row in the target table (IMPL 20.6.1; one without is Unsupported, SPEC 22 ¶1); a
    // failure there leaves no target to prepare and ends the sequence. Preparation runs the front end only: whether this host runs the
    // tests is the runner's rule (RunsOnHost, before any generation), so every Test-mode check reads the same unit on every host.
    private Compilation? PrepareTests(CancellationToken cancellationToken, CheckContext? context, DiagnosticOwner diagnostics)
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

        if (this.SelectTargets(diagnostics) is not { } targets)
        {
            return null;
        }

        if (targets.Length != 1)
        {
            this.Fail(diagnostics, "Select --Target when a test project has more than one configured target.");
            return null;
        }

        if (TargetProfile.Find(targets[0]) is null)
        {
            this.Unsupported(diagnostics);
            return null;
        }

        Compilation? result = null;
        return this.BuildTarget(targets[0], valid, false, null, sources, cancellationToken, c => result = c, context) ? result : null;
    }
}
