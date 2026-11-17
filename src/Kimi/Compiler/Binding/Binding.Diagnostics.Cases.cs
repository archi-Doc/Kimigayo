// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The Semantics-case renderer (SPEC 8.14, 8.15, 23.1.2; DIAGNOSTICS "Semantics cases"; R6-check Q8-6; DECISIONS BU-82).
// - Responsibility: the one rendering of Semantics cases in a record: a binder's name and found Semantics (`s = ref or uniq`), a
//   case set as the `case` fact (CaseFact: per binder when it is a product, else the list of cases), the `case` evidence, the Note
//   `Found under the Semantics case ... (SPEC 8.15)`, the related declarations of the named bindings, the explicit-entry Note
//   tail (FailingCaseNote), and the case set of a bare acquisition that moves a pair-layer Place (AcquisitionCases, R6-check H1).
// - Inputs: a definition's pair binders (PairBinders) and a set of case bits in their enumeration order (the innermost resolved
//   binder advancing fastest, each in SemanticsMask order: the numbering of MirCases' views); a failing mask.
// - Outputs: text, evidence and related declarations; CaseFact leaves the binders it names in the caller's list.
// - Invariants: the text is a function of its inputs; it is built only for a published record, so a warm Bind renders nothing.
// - Must not: decide which cases a problem is found under, or read an analysis.

using System.Numerics;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The binders in scope of a record's definition and the ones its case fact names (AcquisitionCases), reused per record.
    private readonly List<PairBinder> acquisitionBinders = [];
    private readonly List<PairBinder> namedCaseBinders = [];

    // The name a pair binder is declared with (`s` of `s/T`).
    internal static string BinderName(BindingSymbol binder) => binder.Pair?.Name ?? binder.Name;

    // One binder of a case fact: its name and the Semantics found, `s = ref or uniq` (a single case `s = uniq`).
    internal static void AppendCaseBinder(StringBuilder text, string binder, SemanticsMask found)
    {
        text.Append(binder).Append(" = ");
        AppendSemantics(text, found, " or ");
    }

    // SPEC 23.1.2: the `case` fact of a set of case bits over `binders`, or null for no case. The found set is factored per binder
    // when it is a product ("s = uniq"; "s = ref or uniq, t = uniq"), a binder found under all its admitted cases omitted; otherwise
    // each case is listed ("s = uniq, t = owner; s = owner, t = uniq"). The binders named are left in `named`.
    internal static string? CaseFact(List<PairBinder> binders, ulong cases, List<PairBinder> named, out bool single)
    {
        single = false;
        named.Clear();
        var resolved = 0;
        for (var i = 0; i < binders.Count; i++)
        {
            resolved += binders[i].Resolved ? 1 : 0;
        }

        if (cases == 0 || resolved == 0)
        {
            return null;
        }

        Span<SemanticsMask> found = stackalloc SemanticsMask[resolved];
        Span<SemanticsKind> digits = stackalloc SemanticsKind[resolved];
        var count = 0;
        for (var index = 0; index < PairBinder.CaseBound; index++)
        {
            if ((cases & (1UL << index)) != 0)
            {
                count++;
                CaseDigits(binders, index, digits);
                for (var j = 0; j < resolved; j++)
                {
                    found[j] |= digits[j].ToMask();
                }
            }
        }

        var product = 1;
        for (var j = 0; j < resolved; j++)
        {
            product *= BitOperations.PopCount((uint)found[j]);
        }

        single = count == 1;
        var text = new StringBuilder();
        if (product == count)
        {
            AppendProduct(text, binders, found, named);
        }
        else
        {
            AppendCases(text, binders, cases, digits, named);
        }

        return text.Length == 0 ? null : text.ToString();
    }

    // SPEC 8.15, 23.1.2: a problem found under some Semantics cases names them in its Note.
    internal static string? CaseNote(string? note, string? found, bool single)
    {
        if (found is null)
        {
            return note;
        }

        var head = note is null ? "Found" : note + "; found";
        return head + (single ? " under the Semantics case " : " under the Semantics cases ") + found + " (SPEC 8.15)";
    }

    // The `case` fact closes the evidence alternative of the codes that carry it; a code without the alternative keeps its Note.
    internal static object?[]? CaseEvidence(DiagnosticCode code, object? target, string? found)
    {
        var withCase = found is not null && HasCaseEvidence(code);
        if (target is null)
        {
            return withCase ? [found] : null;
        }

        return withCase ? [target, found] : [target];
    }

    // The related declarations of a case fact: the Semantics bindings it names (SPEC 23.1.2).
    internal static (string Role, Koto At, string? Label)[]? WithCaseDeclarations(
        (string Role, Koto At, string? Label)[]? related,
        List<PairBinder> named,
        string? found)
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
        if (!TryAdaptationSelector(type, out var whole) || (this.BareAcquisition(type, node, out _) is var failing && failing == SemanticsMask.None))
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

    // The product form: each resolved binder not found under all its admitted cases, by the Semantics found.
    private static void AppendProduct(StringBuilder text, List<PairBinder> binders, ReadOnlySpan<SemanticsMask> found, List<PairBinder> named)
    {
        for (int j = 0, b = 0; b < binders.Count; b++)
        {
            if (!binders[b].Resolved)
            {
                continue;
            }

            if (found[j] != binders[b].Admitted)
            {
                named.Add(binders[b]);
                AppendCaseBinder(text.Append(text.Length == 0 ? string.Empty : ", "), BinderName(binders[b].Target), found[j]);
            }

            j++;
        }
    }

    // The list form: every case, each resolved binder by its Semantics in that case.
    private static void AppendCases(StringBuilder text, List<PairBinder> binders, ulong cases, Span<SemanticsKind> digits, List<PairBinder> named)
    {
        for (var b = 0; b < binders.Count; b++)
        {
            if (binders[b].Resolved)
            {
                named.Add(binders[b]);
            }
        }

        for (var index = 0; index < PairBinder.CaseBound; index++)
        {
            if ((cases & (1UL << index)) == 0)
            {
                continue;
            }

            CaseDigits(binders, index, digits);
            text.Append(text.Length == 0 ? string.Empty : "; ");
            for (var j = 0; j < digits.Length; j++)
            {
                AppendCaseBinder(text.Append(j == 0 ? string.Empty : ", "), BinderName(named[j].Target), digits[j].ToMask());
            }
        }
    }

    // The case of an index: the admitted Semantics of each resolved binder, outer to inner, the inner advancing fastest.
    private static void CaseDigits(List<PairBinder> binders, int index, Span<SemanticsKind> digits)
    {
        for (int j = digits.Length - 1, b = binders.Count - 1; b >= 0; b--)
        {
            if (!binders[b].Resolved)
            {
                continue;
            }

            var admitted = binders[b].Admitted;
            var size = BitOperations.PopCount((uint)admitted);
            digits[j--] = NthAdmitted(admitted, index % size);
            index /= size;
        }
    }

    // The n-th Semantics of an admitted set in SemanticsOrder.
    private static SemanticsKind NthAdmitted(SemanticsMask admitted, int n)
    {
        for (var i = 0; ; i++)
        {
            if ((admitted & SemanticsOrder[i].ToMask()) != 0 && n-- == 0)
            {
                return SemanticsOrder[i];
            }
        }
    }

    // SPEC 8.14, 8.15.2 (R6-check H1): the `case` fact of a bare acquisition that moves a Place: every case of the definition's
    // resolved binders whose complete Type (CaseType, which takes each layer's Semantics from its own binder, a nested layer's
    // included) the rows of SPEC 3.5 move; null when no case or every case moves. The binders it names are left in namedCaseBinders.
    private string? AcquisitionCases(Koto place, out bool single)
    {
        single = false;
        this.namedCaseBinders.Clear();
        if (place.BoundType is not { ContainsPairLayer: true } type)
        {
            return null;
        }

        var binders = this.acquisitionBinders;
        this.PairBinders(place, binders);
        var resolved = 0;
        var count = 1;
        for (var b = 0; b < binders.Count; b++)
        {
            if (binders[b].Resolved)
            {
                count *= BitOperations.PopCount((uint)binders[b].Admitted);
                resolved++;
            }
        }

        Span<SemanticsKind> digits = stackalloc SemanticsKind[resolved];
        var scratch = this.caseScratch.Rent(resolved);
        var cases = 0UL;
        try
        {
            var assignment = scratch.AsSpan(0, resolved);
            for (var index = 0; index < count && index < PairBinder.CaseBound; index++)
            {
                CaseDigits(binders, index, digits);
                for (int j = 0, b = 0; b < binders.Count; b++)
                {
                    if (binders[b].Resolved)
                    {
                        assignment[j] = new(binders[b].Target, digits[j]);
                        j++;
                    }
                }

                if (this.BareAcquire(this.CaseType(type, assignment), place, assignment, out _, out _) == HirAcquire.Move)
                {
                    cases |= 1UL << index;
                }
            }
        }
        finally
        {
            this.caseScratch.Return(scratch, clearArray: true);
        }

        return CaseFact(binders, cases, this.namedCaseBinders, out single);
    }
}
