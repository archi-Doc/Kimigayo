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
        var diagnostics = new List<CheckDiagnostic>();
        var outcome = CheckOutcome.Completed;
        var location = project.FilePath is { } file ? SourceIdentity.FromPath(file) : default;
        try
        {
            accepted = mode == CheckMode.Product
                ? project.Check(context, cancellationToken).GetAwaiter().GetResult()
                : project.PrepareTests(cancellationToken, context) is not null;
        }
        catch (InvalidDataException ex)
        {
            // Test preparation reports configuration and test-definition failures as exceptions.
            context.Failures.Add(ex.Message);
        }
        catch (Exception ex) when (ex is not (PendingInputException or OperationCanceledException))
        {
            outcome = CheckOutcome.Faulted;
            diagnostics.Add(Create(DiagnosticCode.CheckFaulted_Kd, location, ex.Message));
        }

        if (outcome == CheckOutcome.Completed && !context.FrontEndRan)
        {
            outcome = CheckOutcome.Blocked;
        }

        foreach (var failure in context.Failures)
        {
            diagnostics.Add(Create(DiagnosticCode.ProjectPreparationFailed_Kd, location, failure));
        }

        if (context.Compilation is { } compilation)
        {
            foreach (var collection in compilation.Kimigayo.DiagnosticCollections)
            {
                foreach (var diagnostic in collection.GetArray())
                {
                    diagnostics.Add(Convert(diagnostic));
                }
            }
        }

        if (outcome == CheckOutcome.Blocked && !diagnostics.Exists(static x => x.Severity == DiagnosticSeverity.Error))
        {
            // SPEC 23.3.3: a Blocked result always carries a diagnostic that explains it.
            diagnostics.Add(Create(DiagnosticCode.ProjectPreparationFailed_Kd, location, "the project inputs could not be established"));
        }

        var presence = mode == CheckMode.Product ? ScanTestPresence(context) : TestPresence.Unknown;
        return new(outcome, accepted && outcome == CheckOutcome.Completed, presence, diagnostics.ToArray());
    }

    /// <summary>Creates a record that concerns a whole input, such as a project file.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="location">The input, or the default value.</param>
    /// <param name="argument">The message argument.</param>
    /// <returns>The record, without a range.</returns>
    public static CheckDiagnostic Create(DiagnosticCode code, SourceIdentity location, object? argument = null)
    {
        DiagnosticEntries.TryGet(code, out var entry);
        var message = entry?.FormatMessage(argument, null) ?? code.ToString();
        return new(code.ToString(), entry?.Severity ?? DiagnosticSeverity.Error, message, location, null);
    }

    private static CheckDiagnostic Convert(Diagnostic diagnostic)
    {
        var location = default(SourceIdentity);
        SourceRange? range = null;
        if (diagnostic.SourceDocument is { } document)
        {
            location = SourceIdentity.FromPath(document.Path);
            if (diagnostic.Span.Start >= 0 && diagnostic.Span.End <= document.SourceText.Length)
            {
                range = document.GetSourceRange(diagnostic.Span);
            }
        }
        else if (diagnostic.Location is { } path)
        {
            location = SourceIdentity.FromPath(path);
        }

        return new(diagnostic.Entry.Name, diagnostic.Entry.Severity, diagnostic.Message, location, range);
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
        return finder.Found ? TestPresence.Yes : compilation.Kotonoha.HasSourceErrors ? TestPresence.Unknown : TestPresence.No;
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
