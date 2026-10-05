// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of a definition (SPEC 8.10): a body whose scope has a resolved pair binder is built, solved and
/// verified once per admitted case, with every pair layer as the complete Type of that case; the first case is the listed body,
/// the others pooled side bodies whose problems merge into the listed one.</summary>
public sealed partial class OwnershipAnalysis
{
    // The enumeration order of cases, the bit order of SemanticsMask.
    private static readonly SemanticsKind[] CaseOrder =
    [
        SemanticsKind.Owner, SemanticsKind.Ref, SemanticsKind.Uniq, SemanticsKind.Obj, SemanticsKind.Rc, SemanticsKind.Arc, SemanticsKind.ObjRef, SemanticsKind.ObjUniq, SemanticsKind.Raw,
    ];

    private readonly List<PairBinder> caseBinders = new();
    private readonly List<OwnershipBody> casePool = new();
    private int caseBodyCount;
    private OwnershipBody? caseBody;
    private OwnershipBody? caseListed;

    private static SemanticsKind? NextAdmitted(SemanticsMask admitted, int start)
    {
        for (var i = start; i < CaseOrder.Length; i++)
        {
            if ((admitted & CaseOrder[i].ToMask()) != 0)
            {
                return CaseOrder[i];
            }
        }

        return null;
    }

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
        for (var i = 0; i < this.caseBinders.Count; i++)
        {
            if (this.caseBinders[i].Resolved)
            {
                this.cases[at++] = new(this.caseBinders[i].Target, NextAdmitted(this.caseBinders[i].Admitted, 0)!.Value);
            }
        }

        this.caseCount = resolved;
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
            if (NextAdmitted(admitted, Array.IndexOf(CaseOrder, this.cases[at].Semantics) + 1) is { } next)
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
    // every case is (SPEC 8.10).
    private void BuildCases(FunctionKoto function)
    {
        this.caseBodyCount = 0;
        this.BuildOnce(function, -1);
        var listed = this.body;
        this.caseListed = listed;
        while (this.NextCase())
        {
            if (this.caseBodyCount == this.casePool.Count)
            {
                this.casePool.Add(new());
            }

            var side = this.casePool[this.caseBodyCount++];
            this.caseBody = side;
            this.BuildOnce(function, -1);
            this.caseBody = null;
            listed.IsVerified &= side.IsVerified;
        }

        this.caseListed = null;
        this.caseCount = 0;
        this.body = listed;
    }

    // Appends the built body's problems to the result: a side body's only when no earlier case of the same function reported the
    // same problem (SPEC 23.3.6.4).
    private void AppendIssues()
    {
        var storage = this.body.IssueStorage;
        var listed = this.caseBody is null ? null : this.caseListed;
        for (var i = 0; i < storage.Count; i++)
        {
            if (listed is null || listed.TryReport(storage[i]))
            {
                this.issues.Add(storage[i]);
            }
        }
    }
}
