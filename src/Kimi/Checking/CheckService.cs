// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>Runs one check unit through the shared check entry and returns its immutable output (SPEC 23.3).</summary>
/// <remarks>
/// The unit's compilation lives only for one call: its diagnostics become <see cref="CheckDiagnostic"/> records, and the
/// compilation is released. The service holds no state, so any number of callers may share it.
/// </remarks>
internal static class CheckService
{
    /// <summary>Checks one unit.</summary>
    /// <param name="project">The loaded project; its compiler service should render no diagnostic.</param>
    /// <param name="target">The unit's target.</param>
    /// <param name="mode">The unit's mode.</param>
    /// <param name="debug">The unit's <c>Debug</c> setting.</param>
    /// <param name="inputs">The input source.</param>
    /// <param name="cancellationToken">Cancels dependency resolution.</param>
    /// <returns>The output.</returns>
    /// <exception cref="PendingInputException">The check needs an input with an event after its base.</exception>
    /// <exception cref="OperationCanceledException">The check was cancelled.</exception>
    public static CheckOutput Run(Project project, string target, CheckMode mode, bool debug, CheckInputSource inputs, CancellationToken cancellationToken)
    {
        var context = new CheckContext(inputs);
        project.KimiOptions = new KimiOptions { Target = target, Debug = debug };
        var accepted = false;
        var outcome = CheckOutcome.Completed;
        var location = project.FilePath;
        Exception? failure = null;
        try
        {
            accepted = mode == CheckMode.Product
                ? project.Check(context, cancellationToken).GetAwaiter().GetResult()
                : project.PrepareTests(cancellationToken, context) is not null;
        }
        catch (InvalidDataException ex)
        {
            // Test preparation reports configuration and test-definition failures as exceptions.
            context.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, location, note: ex.Message);
        }
        catch (DiagnosticContractException ex)
        {
            // SPEC 23.3.3: a violated contract discards every partial record.
            return new(CheckOutcome.Faulted, false, TestPresence.Unknown, DiagnosticFaults.Create(ex.Fault, ex.Message, location));
        }
        catch (Exception ex) when (ex is not (PendingInputException or OperationCanceledException))
        {
            outcome = CheckOutcome.Faulted;
            failure = ex;
        }

        if (outcome == CheckOutcome.Completed && !context.FrontEndRan)
        {
            outcome = CheckOutcome.Blocked;
            if (!context.Diagnostics.HasErrorsThrough(DiagnosticPartition.Input))
            {
                // SPEC 23.3.3: the fallback of input preparation, a compiler defect to repair where it occurs.
                context.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, location, note: "The project inputs could not be established.");
            }
        }

        DiagnosticResult result;
        try
        {
            // SPEC 23.3.3: a rejected result that publishes no Error violates the diagnostic contract.
            result = context.Diagnostics.Finalize(rejected: outcome != CheckOutcome.Faulted && !accepted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(CheckOutcome.Faulted, false, TestPresence.Unknown, DiagnosticFaults.Create(ex is DiagnosticContractException contract ? contract.Fault : DiagnosticFault.Collection, ex.Message, location));
        }

        if (failure is not null)
        {
            // Analysis threw while collection stayed intact: keep the valid records.
            return new(outcome, false, TestPresence.Unknown, DiagnosticFaults.Create(DiagnosticFault.Exception, failure.Message, location, result));
        }

        var presence = mode == CheckMode.Product ? ScanTestPresence(context) : TestPresence.Unknown;
        return new(outcome, accepted && outcome == CheckOutcome.Completed, presence, result);
    }

    /// <summary>Creates the diagnostics of a result that concerns a whole input, such as a project file.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="location">The input, or the default value.</param>
    /// <param name="note">The environment-dependent text of the failure, published as a bounded Note.</param>
    /// <returns>The finalized diagnostics, without a range.</returns>
    public static DiagnosticResult Create(DiagnosticCode code, SourceIdentity location, string? note = null)
    {
        var owner = new DiagnosticOwner();
        owner.Report(DiagnosticPartition.Input, code, location.IsEmpty ? null : location.Value, note: note);
        return owner.Finalize();
    }

    // SPEC 23.3.1: a syntax-level scan of the project's own sources; semantic failures never hide a marker.
    private static TestPresence ScanTestPresence(CheckContext context)
    {
        if (context.Compilation is not { } compilation || !context.FrontEndRan)
        {
            return TestPresence.Unknown;
        }

        var finder = new MarkerFinder();
        finder.Visit(compilation.Kotonoha.RootKoto);
        return finder.Found ? TestPresence.Yes : compilation.Diagnostics.HasSyntaxErrors(compilation.Kotonoha) ? TestPresence.Unknown : TestPresence.No;
    }

    private sealed class MarkerFinder : KotoVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(Koto node)
        {
            if (this.Found)
            {
                return;
            }

            if (node is FunctionKoto && TestDefinition.Marker(node) is not null)
            {
                this.Found = true;
                return;
            }

            base.Visit(node);
        }
    }
}
