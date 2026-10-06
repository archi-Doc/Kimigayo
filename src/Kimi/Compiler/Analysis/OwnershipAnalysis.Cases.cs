// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of a definition (SPEC 8.10): a body whose scope has a resolved pair binder is built, solved and
/// verified once per admitted case, with every pair layer as the complete Type of that case; the first case is the listed body,
/// the others pooled side bodies whose problems merge into the listed one.</summary>
public sealed partial class OwnershipAnalysis
{
    // SPEC 8.10, 23.3.6.1: the bound on the cases of one body, the width of an issue's case set.
    internal const int CaseBound = 64;

    private readonly List<PairBinder> caseBinders = new();
    private readonly List<PairBinder> namedBinders = new();
    private readonly List<OwnershipBody> casePool = new();
    private int caseBodyCount;
    private OwnershipBody? caseBody;
    private OwnershipBody? caseListed;
    private ulong caseBit;
    private long caseProduct;

    private static SemanticsKind? NextAdmitted(SemanticsMask admitted, int start)
    {
        for (var i = start; i < Binding.SemanticsOrder.Length; i++)
        {
            if ((admitted & Binding.SemanticsOrder[i].ToMask()) != 0)
            {
                return Binding.SemanticsOrder[i];
            }
        }

        return null;
    }

    // Whether a code's catalog row carries the `case` Reason fact as its last evidence fact (SPEC 23.3.6.4).
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

    private static FunctionKoto? EnclosingFunction(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto function)
            {
                return function;
            }
        }

        return null;
    }

    // The name a pair binder is declared with (`s` of `s/T`).
    private static string BinderName(in PairBinder binder) => binder.Target.Pair?.Name ?? binder.Target.Name;

    // Resolves the cases of a definition: the product of the admitted sets of the resolved binders in scope (SPEC 8.10). False
    // when there is none, so the body is analyzed once with its declared Types.
    private bool ResolveCases(FunctionKoto function)
    {
        this.compilation.Binding.PairBinders(function, this.caseBinders);
        var resolved = 0;
        for (var i = 0; i < this.caseBinders.Count; i++)
        {
            resolved += this.caseBinders[i].Resolved ? 1 : 0;
        }

        if (resolved == 0)
        {
            return false;
        }

        if (this.cases.Length < resolved)
        {
            this.cases = new PairCase[Math.Max(resolved, 2 * this.cases.Length)];
        }

        var at = 0;
        var product = 1L;
        for (var i = 0; i < this.caseBinders.Count; i++)
        {
            if (this.caseBinders[i].Resolved)
            {
                this.cases[at++] = new(this.caseBinders[i].Target, NextAdmitted(this.caseBinders[i].Admitted, 0)!.Value);
                product *= BitOperations.PopCount((uint)this.caseBinders[i].Admitted);
            }
        }

        this.caseCount = resolved;
        this.caseProduct = product;
        return true;
    }

    // Advances to the next case, the innermost binder fastest, in SemanticsMask order; false after the last one.
    private bool NextCase()
    {
        var at = this.caseCount - 1;
        for (var i = this.caseBinders.Count - 1; i >= 0 && at >= 0; i--)
        {
            if (!this.caseBinders[i].Resolved)
            {
                continue;
            }

            var admitted = this.caseBinders[i].Admitted;
            if (NextAdmitted(admitted, Array.IndexOf(Binding.SemanticsOrder, this.cases[at].Semantics) + 1) is { } next)
            {
                this.cases[at] = new(this.cases[at].Target, next);
                return true;
            }

            this.cases[at] = new(this.cases[at].Target, NextAdmitted(admitted, 0)!.Value);
            at--;
        }

        return false;
    }

    // Builds every case: the first into the listed body, the others into pooled side bodies; the listed body is verified only when
    // every case is (SPEC 8.10). Its problems are published after the last case, with the cases each was found under, and a problem
    // every case found shows no case (SPEC 23.3.6.4). Beyond the bound, the first case is built and the bound reported (SPEC 23.3.6.1).
    private void BuildCases(FunctionKoto function)
    {
        this.caseBodyCount = 0;
        this.caseBit = 1;
        this.BuildOnce(function, -1);
        var listed = this.body;
        this.caseListed = listed;
        var run = 1;
        if (this.caseProduct > CaseBound)
        {
            listed.ReportIssue(new(function, OwnershipFailure.CaseLimit));
            listed.IsVerified = false;
        }
        else
        {
            while (this.NextCase())
            {
                if (this.caseBodyCount == this.casePool.Count)
                {
                    this.casePool.Add(new());
                }

                var side = this.casePool[this.caseBodyCount++];
                this.caseBit <<= 1;
                this.caseBody = side;
                this.BuildOnce(function, -1);
                this.caseBody = null;
                listed.IsVerified &= side.IsVerified;
                run++;
            }
        }

        this.caseListed = null;
        this.caseCount = 0;
        this.caseBit = 0;
        this.body = listed;
        listed.DropUniversalCases(run == CaseBound ? ulong.MaxValue : (1UL << run) - 1);
        for (var i = 0; i < listed.IssueStorage.Count; i++)
        {
            this.issues.Add(listed.IssueStorage[i]);
        }
    }

    // Appends the built body's problems to the result. In a case run the listed body publishes after the last case (BuildCases)
    // and a side body's problems merge into it (SPEC 23.3.6.4).
    private void AppendIssues()
    {
        var storage = this.body.IssueStorage;
        if (this.caseCount != 0)
        {
            if (this.caseBody is not null && this.caseListed is { } listed)
            {
                for (var i = 0; i < storage.Count; i++)
                {
                    listed.MergeIssue(storage[i]);
                }
            }

            return;
        }

        for (var i = 0; i < storage.Count; i++)
        {
            this.issues.Add(storage[i]);
        }
    }

    // SPEC 23.3.6.4: the text of the cases a problem was found under, or null for a problem of every case or of no case run. The
    // found set is factored per binder when it is a product ("s = uniq"; "s = ref or uniq, t = uniq"), each binder found under
    // all its admitted cases omitted; otherwise each case is listed ("s = uniq, t = owner; s = owner, t = uniq"). The binders
    // named are left in namedBinders for the related declarations. Formatted only for a published record.
    private string? CaseFact(in OwnershipIssue issue, out bool single)
    {
        single = false;
        this.namedBinders.Clear();
        if (issue.Cases == 0 || EnclosingFunction(issue.Source) is not { } function)
        {
            return null;
        }

        this.compilation.Binding.PairBinders(function, this.caseBinders);
        var resolved = 0;
        for (var i = 0; i < this.caseBinders.Count; i++)
        {
            resolved += this.caseBinders[i].Resolved ? 1 : 0;
        }

        if (resolved == 0)
        {
            return null;
        }

        // The enumeration of BuildCases: the innermost resolved binder advances fastest, each in SemanticsMask order (NextCase).
        Span<SemanticsMask> found = stackalloc SemanticsMask[resolved];
        Span<SemanticsKind> digits = stackalloc SemanticsKind[resolved];
        var count = 0;
        for (var index = 0; index < CaseBound; index++)
        {
            if ((issue.Cases & (1UL << index)) == 0)
            {
                continue;
            }

            count++;
            this.CaseDigits(index, digits);
            for (var j = 0; j < resolved; j++)
            {
                found[j] |= digits[j].ToMask();
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
            for (int j = 0, b = 0; b < this.caseBinders.Count; b++)
            {
                if (!this.caseBinders[b].Resolved)
                {
                    continue;
                }

                if (found[j] != this.caseBinders[b].Admitted)
                {
                    this.namedBinders.Add(this.caseBinders[b]);
                    text.Append(text.Length == 0 ? string.Empty : ", ").Append(BinderName(this.caseBinders[b])).Append(" = ");
                    Binding.AppendSemantics(text, found[j], " or ");
                }

                j++;
            }
        }
        else
        {
            for (var b = 0; b < this.caseBinders.Count; b++)
            {
                if (this.caseBinders[b].Resolved)
                {
                    this.namedBinders.Add(this.caseBinders[b]);
                }
            }

            for (var index = 0; index < CaseBound; index++)
            {
                if ((issue.Cases & (1UL << index)) == 0)
                {
                    continue;
                }

                this.CaseDigits(index, digits);
                text.Append(text.Length == 0 ? string.Empty : "; ");
                for (var j = 0; j < resolved; j++)
                {
                    text.Append(j == 0 ? string.Empty : ", ").Append(BinderName(this.namedBinders[j])).Append(" = ").Append(digits[j].ToText());
                }
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    // The case of an index: the admitted Semantics of each resolved binder, outer to inner, the inner advancing fastest.
    private void CaseDigits(int index, Span<SemanticsKind> digits)
    {
        for (int j = digits.Length - 1, b = this.caseBinders.Count - 1; b >= 0; b--)
        {
            if (!this.caseBinders[b].Resolved)
            {
                continue;
            }

            var admitted = this.caseBinders[b].Admitted;
            var size = BitOperations.PopCount((uint)admitted);
            var digit = index % size;
            index /= size;
            var semantics = NextAdmitted(admitted, 0)!.Value;
            for (var d = 0; d < digit; d++)
            {
                semantics = NextAdmitted(admitted, Array.IndexOf(Binding.SemanticsOrder, semantics) + 1)!.Value;
            }

            digits[j--] = semantics;
        }
    }

    // The related declarations of a case fact: the Semantics bindings it names (SPEC 23.3.6.4).
    private (string Role, Koto At, string? Label)[]? WithCaseDeclarations((string Role, Koto At, string? Label)[]? related, string? found)
    {
        if (found is null || this.namedBinders.Count == 0)
        {
            return related;
        }

        var locations = new (string Role, Koto At, string? Label)[(related?.Length ?? 0) + this.namedBinders.Count];
        related?.CopyTo(locations, 0);
        for (var i = 0; i < this.namedBinders.Count; i++)
        {
            locations[(related?.Length ?? 0) + i] = ("declaration", this.namedBinders[i].Target.Declaration, "the Semantics binding " + BinderName(this.namedBinders[i]));
        }

        return locations;
    }

    // SPEC 23.3.6.1: the number of cases of a definition, the product of its resolved binders' admitted sets.
    private long CaseProduct(FunctionKoto function)
    {
        this.compilation.Binding.PairBinders(function, this.caseBinders);
        var product = 1L;
        for (var i = 0; i < this.caseBinders.Count; i++)
        {
            if (this.caseBinders[i].Resolved)
            {
                product *= BitOperations.PopCount((uint)this.caseBinders[i].Admitted);
            }
        }

        return product;
    }
}
