// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The one renderer of Semantics cases in a record (SPEC 8.14, 8.15, 23.1.2; DIAGNOSTICS "Semantics cases"; R6-check Q8-6): a binder's
// found Semantics (`s = ref or uniq`), the explicit-entry Note tail (FailingCaseNote), the `case` evidence fact, the Note `Found under
// the Semantics case ... (SPEC 8.15)` and the related declarations of the named bindings. It decides no case: its callers give the
// binders and the Semantics found. The old Analysis calls it for its case records until H10b deletes that Analysis.

using System.Numerics;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The name a pair binder is declared with (`s` of `s/T`).
    internal static string BinderName(BindingSymbol binder) => binder.Pair?.Name ?? binder.Name;

    // One binder of a case fact: its name and the Semantics found, `s = ref or uniq` (a single case `s = uniq`).
    internal static void AppendCaseBinder(StringBuilder text, string binder, SemanticsMask found)
    {
        text.Append(binder).Append(" = ");
        AppendSemantics(text, found, " or ");
    }

    // SPEC 8.15, 23.1.2: a problem found under some Semantics cases names them in its Note.
    internal static string? CaseNote(string? note, string? found, bool single)
        => found is null ? note : (note is null ? "Found" : note + "; found") + (single ? " under the Semantics case " : " under the Semantics cases ") + found + " (SPEC 8.15)";

    // The `case` fact closes the evidence alternative of the codes that carry it; a code without the alternative keeps its Note.
    internal static object?[]? CaseEvidence(DiagnosticCode code, object? target, string? found)
    {
        var withCase = found is not null && HasCaseEvidence(code);
        return target is null ? (withCase ? [found] : null) : withCase ? [target, found] : [target];
    }

    // The related declarations of a case fact: the Semantics bindings it names (SPEC 23.1.2).
    internal static (string Role, Koto At, string? Label)[]? WithCaseDeclarations((string Role, Koto At, string? Label)[]? related, List<PairBinder> named, string? found)
    {
        if (found is null || named.Count == 0)
        {
            return related;
        }

        var locations = new (string Role, Koto At, string? Label)[(related?.Length ?? 0) + named.Count];
        related?.CopyTo(locations, 0);
        for (var i = 0; i < named.Count; i++)
        {
            locations[(related?.Length ?? 0) + i] = ("declaration", named[i].Target.Declaration, "the Semantics binding " + BinderName(named[i].Target));
        }

        return locations;
    }

    /// <summary>Formats the Semantics cases in which a bare acquisition of a pair layer neither Copies nor Reborrows, for a Note
    /// (SPEC 8.14, 23.1.2); empty for another Type or when every case has a plan.</summary>
    /// <param name="type">The stored Type.</param>
    /// <param name="node">The acquiring syntax.</param>
    /// <returns>The Note's tail, such as <c> in the Semantics case s = owner (SPEC 8.14)</c>.</returns>
    internal string FailingCaseNote(BoundType type, Koto node)
    {
        if (!TryPairLayer(type, out var whole, out _) || (this.BareAcquisition(type, node, out _) is var failing && failing == SemanticsMask.None))
        {
            return string.Empty;
        }

        var text = new StringBuilder(" in the Semantics case").Append(BitOperations.PopCount((uint)failing) == 1 ? " " : "s ");
        AppendCaseBinder(text, whole.Symbol is { } symbol ? BinderName(symbol) : "s", failing);
        return text.Append(" (SPEC 8.14)").ToString();
    }

    // Whether a code's catalog row carries the `case` Reason fact as its last evidence fact (SPEC 23.1.2).
    private static bool HasCaseEvidence(DiagnosticCode code)
    {
        if (!DiagnosticEntries.TryGet(code, out var entry))
        {
            return false;
        }

        var alternatives = entry.EvidenceAlternatives;
        for (var i = 0; i < alternatives.Length; i++)
        {
            if (alternatives[i].Length > 0 && alternatives[i][^1].Name == "case")
            {
                return true;
            }
        }

        return false;
    }
}
