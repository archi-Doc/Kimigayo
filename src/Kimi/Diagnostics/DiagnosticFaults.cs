// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>
/// The one fault path (SPEC 23.3.3): exactly one <c>CheckFaulted_Kd</c> Error explains a Faulted result. Its text is fixed here and
/// depends neither on the catalog nor on ordinary formatting; a test keeps it equal to the catalog entry.
/// </summary>
internal static class DiagnosticFaults
{
    /// <summary>The message before the fault description; the catalog template is this text followed by <c>{0}</c>.</summary>
    internal const string MessagePrefix = "The compiler failed internally while checking: ";

    private const int MaxNoteLength = 400;

    /// <summary>Describes a fault kind in the message.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The description.</returns>
    internal static string Describe(DiagnosticFault fault) => fault switch
    {
        DiagnosticFault.Catalog => "the diagnostic catalog is malformed",
        DiagnosticFault.UnknownCode => "a diagnostic named an unknown code",
        DiagnosticFault.InvalidLocation => "a diagnostic named an invalid location",
        DiagnosticFault.InvalidArgument => "a diagnostic did not match its definition",
        DiagnosticFault.Exception => "an exception ended the analysis",
        DiagnosticFault.ConflictingProblem => "one problem was reported with different facts",
        DiagnosticFault.UndefinedOrder => "two problems have no defined order",
        DiagnosticFault.UnexplainedRejection => "a rejection has no explaining error",
        _ => "diagnostics could not be collected",
    };

    /// <summary>Creates the Faulted result's diagnostics, keeping earlier valid records when collection stayed intact.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="detail">The environment-dependent detail, kept as a bounded Note.</param>
    /// <param name="location">The project file, or <see langword="null"/>.</param>
    /// <param name="kept">The valid records to keep, or <see langword="null"/> to discard every earlier record.</param>
    /// <returns>The diagnostics.</returns>
    internal static DiagnosticResult Create(DiagnosticFault fault, string? detail, string? location, DiagnosticResult? kept = null)
    {
        var sources = kept?.Sources ?? [];
        var source = -1;
        if (location is not null)
        {
            source = Array.FindIndex(sources, x => x.Path == location);
            if (source < 0)
            {
                source = sources.Length;
                sources = [.. sources, new(location, !location.StartsWith(Checking.SourceIdentity.BuiltInPrefix, StringComparison.Ordinal))];
            }
        }

        var note = detail is null ? null : detail.Length <= MaxNoteLength ? detail : string.Concat(detail.AsSpan(0, MaxNoteLength), "…");
        var record = new CheckDiagnostic(nameof(DiagnosticCode.CheckFaulted_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Internal, MessagePrefix + Describe(fault), source, null)
        {
            Reason = [DiagnosticValue.Enumeration("fault", fault)],
            Note = note,
        };

        return new([.. kept?.Diagnostics ?? [], record], sources);
    }
}
