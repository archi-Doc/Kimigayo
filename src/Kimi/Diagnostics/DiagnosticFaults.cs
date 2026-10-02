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
        var previous = kept?.Diagnostics ?? [];
        var source = -1;
        if (location is not null)
        {
            source = Array.FindIndex(sources, x => x.Path == location);
            if (source < 0)
            {
                // SPEC 23.3.6.3: the project file is consumed before every kept source, so its new entry comes first.
                source = 0;
                sources = [new(location, !location.StartsWith(Checking.SourceIdentity.BuiltInPrefix, StringComparison.Ordinal)), .. sources];
                previous = Array.ConvertAll(previous, static x => x with
                {
                    Source = x.Source < 0 ? x.Source : x.Source + 1,
                    Related = x.Related is { } related ? Array.ConvertAll(related, static r => r with { Source = r.Source < 0 ? r.Source : r.Source + 1 }) : null,
                });
            }
        }

        // Keep the fault path independent of catalog formatting, with the same presentation limit as ordinary Notes.
        var note = detail is null ? null : detail.Length <= DiagnosticLimits.NoteLength ? detail :
            string.Concat(detail.AsSpan(0, DiagnosticText.HeadLength(detail, DiagnosticLimits.NoteLength - 1)), DiagnosticText.Elision);
        var record = new CheckDiagnostic(nameof(DiagnosticCode.CheckFaulted_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Internal, MessagePrefix + Describe(fault), source, null)
        {
            Reason = [DiagnosticValue.Enumeration("fault", fault)],
            Note = note,
        };

        var insertion = 0;
        var orderSource = source < 0 ? int.MaxValue : source;
        while (insertion < previous.Length)
        {
            var other = previous[insertion];
            var otherSource = other.Source < 0 ? int.MaxValue : other.Source;
            if (otherSource > orderSource || (otherSource == orderSource && (other.Span is not null || string.CompareOrdinal(other.Code, record.Code) > 0)))
            {
                break;
            }

            insertion++;
        }

        return new([.. previous.AsSpan(0, insertion), record, .. previous.AsSpan(insertion)], sources);
    }
}
