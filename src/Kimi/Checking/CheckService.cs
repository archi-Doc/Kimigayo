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
    /// <param name="project">The loaded project; the check sets its options and renders nothing, whatever its compiler service.</param>
    /// <param name="target">The unit's target.</param>
    /// <param name="mode">The unit's mode.</param>
    /// <param name="debug">The unit's <c>Debug</c> setting.</param>
    /// <param name="inputs">The input source.</param>
    /// <param name="cancellationToken">Cancels dependency resolution.</param>
    /// <param name="collectHover">Whether to collect documentation and optional editor information.</param>
    /// <returns>The output.</returns>
    /// <exception cref="PendingInputException">The check needs an input with an event after its base.</exception>
    /// <exception cref="OperationCanceledException">The check was cancelled; a cancelled check has no output (SPEC 23.3.3).</exception>
    public static CheckOutput Run(Project project, string target, CheckMode mode, bool debug, CheckInputSource inputs, CancellationToken cancellationToken, bool collectHover = false)
    {
        var context = new CheckContext(inputs, collectHover);
        var location = project.FilePath;
        project.KimiOptions = new KimiOptions { Target = target, Debug = debug };
        var accepted = false;
        Exception? failure = null;
        try
        {
            accepted = mode == CheckMode.Product
                ? project.Check(context, cancellationToken)
                : project.PrepareTests(cancellationToken, context) is not null;
        }
        catch (InvalidDataException ex) when (context.Compilation is null)
        {
            // Test preparation reports an invalid configuration as an exception before the front end (SPEC 23.3.3: Blocked).
            context.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, location, note: ex.Message);
        }
        catch (DiagnosticContractException ex)
        {
            // SPEC 23.3.3: a violated contract discards every partial record.
            return Faulted(ex.Fault, ex.Message, location);
        }
        catch (Exception ex) when (ex is not (PendingInputException or OperationCanceledException))
        {
            failure = ex; // Analysis threw while collection stayed intact: the valid records are kept.
        }

        DiagnosticResult result;
        try
        {
            // SPEC 23.3.3, 23.3.6.7: a rejected result that publishes no Error violates the diagnostic contract; the front end
            // reports its fallback where the command does, so a violation here is a defect of the check entry's input path.
            result = context.Diagnostics.Finalize(rejected: failure is null && !accepted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Faulted(ex is DiagnosticContractException contract ? contract.Fault : DiagnosticFault.Collection, ex.Message, location);
        }

        if (failure is not null)
        {
            return Faulted(DiagnosticFault.Exception, failure.Message, location, result);
        }

        // SPEC 23.3.3: the outcome follows from how the check ended; a front end that never ran leaves the check Blocked.
        if (context.Compilation is not { } compilation)
        {
            return new(CheckOutcome.Blocked, false, TestPresence.Unknown, result);
        }

        var presence = mode == CheckMode.Product ? ScanTestPresence(compilation) : TestPresence.Unknown;
        string? hoverFault = null;
        var hover = collectHover ? TryCreateHover(compilation, static c => HoverBuilder.Create(c), out hoverFault) : null;
        hoverFault ??= compilation.DocumentationFailure;
        return new(CheckOutcome.Completed, accepted, presence, result) { Hover = hover, HoverFault = hoverFault };
    }

    // SPEC 23.4.11.6: optional projection has no authority to alter finalized diagnostics, acceptance or TestPresence.
    // Pass state separately so the production factory is a shared static delegate, without a closure per check.
    internal static HoverSnapshot? TryCreateHover<T>(T state, Func<T, HoverSnapshot> create, out string? fault)
    {
        fault = null;
        try
        {
            return create(state);
        }
        catch (Exception ex) when (ex is not (PendingInputException or OperationCanceledException))
        {
            fault = ex.Message;
            return null;
        }
    }

    // SPEC 23.3.3: exactly one CheckFaulted_Kd Error explains a Faulted result; earlier records are kept only when collection stayed intact.
    private static CheckOutput Faulted(DiagnosticFault fault, string? detail, string? location, DiagnosticResult? kept = null)
        => new(CheckOutcome.Faulted, false, TestPresence.Unknown, DiagnosticFaults.Create(fault, detail, location, kept));

    // SPEC 23.3.1: a syntax-level scan of the project's own sources; semantic failures never hide a marker.
    private static TestPresence ScanTestPresence(Compilation compilation)
    {
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
