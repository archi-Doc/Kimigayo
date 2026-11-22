// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Diagnostics;

/// <summary>
/// The one fault path (SPEC 23.2.2): exactly one <c>CheckFaulted_Kd</c> Error explains a Faulted result. Its text is fixed here and
/// depends neither on the catalog nor on ordinary formatting; a test keeps it equal to the catalog entry.
/// </summary>
internal static class DiagnosticFaults
{
    /// <summary>The message before the fault description; the catalog template is this text followed by <c>{0}</c>.</summary>
    internal const string MessagePrefix = "The compiler failed internally: ";

    /// <summary>Describes a fault kind in the message.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The description.</returns>
    internal static string Describe(DiagnosticFault fault) => fault switch
    {
        DiagnosticFault.Catalog => "the diagnostic catalog is malformed",
        DiagnosticFault.UnknownCode => "a diagnostic named an unknown code",
        DiagnosticFault.InvalidLocation => "a diagnostic named an invalid location",
        DiagnosticFault.InvalidArgument => "a diagnostic did not match its definition",
        DiagnosticFault.Exception => "an exception ended the compilation",
        DiagnosticFault.ConflictingProblem => "one problem was reported with different facts",
        DiagnosticFault.UndefinedOrder => "two problems have no defined order",
        DiagnosticFault.UnexplainedRejection => "a rejection has no explaining error",
        _ => "diagnostics could not be collected",
    };

    /// <summary>Creates the Faulted result's diagnostics, keeping earlier valid records when collection stayed intact.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="exception">The exception that ended the result; its chain is the bounded Note.</param>
    /// <param name="location">The project file, or <see langword="null"/>.</param>
    /// <param name="kept">The valid records to keep, or <see langword="null"/> to discard every earlier record.</param>
    /// <returns>The diagnostics.</returns>
    internal static DiagnosticResult Create(DiagnosticFault fault, Exception exception, string? location, DiagnosticResult? kept = null)
    {
        var sources = kept?.Sources ?? [];
        var previous = kept?.Diagnostics ?? [];
        var source = -1;
        if (location is not null)
        {
            source = Array.FindIndex(sources, x => x.Path == location);
            if (source < 0)
            {
                // SPEC 23.1.2: the project file is consumed before every kept source, so its new entry comes first.
                source = 0;
                sources = [new(location, !location.StartsWith(Checking.SourceIdentity.BuiltInPrefix, StringComparison.Ordinal)), .. sources];
                previous = Array.ConvertAll(previous, static x => x with
                {
                    Source = x.Source < 0 ? x.Source : x.Source + 1,
                    Related = x.Related is { } related ? Array.ConvertAll(related, static r => r with { Source = r.Source < 0 ? r.Source : r.Source + 1 }) : null,
                });
            }
        }

        // DIAGNOSTICS §6 (PLAN G1014): the environment-dependent detail is formed only here, never by a caller: the exception's chain, each
        // link its type's full name and message, outer first, joined as the framework joins inner exceptions. Its head is kept under the
        // ordinary Note limit, so the outer type always shows; the fault path stays independent of catalog formatting.
        var chain = new StringBuilder();
        for (Exception? link = exception; link is not null && chain.Length <= DiagnosticLimits.NoteLength; link = link.InnerException)
        {
            chain.Append(chain.Length == 0 ? string.Empty : " ---> ").Append(link.GetType().FullName).Append(": ").Append(link.Message);
        }

        var detail = chain.ToString();
        var note = detail.Length <= DiagnosticLimits.NoteLength ? detail :
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
