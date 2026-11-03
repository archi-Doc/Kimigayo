// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of a definition (SPEC 8.15): a body whose scope has a resolved pair binder is built, solved and
/// verified once per admitted case, with every pair layer as the complete Type of that case; the first case is the listed body,
/// the others pooled side bodies whose problems merge into the listed one.</summary>
public sealed partial class OwnershipAnalysis
{
    private readonly List<PairBinder> caseBinders = new();
    private readonly List<OwnershipBody> casePool = new();
    private int caseBodyCount;
    private OwnershipBody? caseBody;
    private OwnershipBody? caseListed;
    private ulong caseBit;

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

    // Resolves the cases of a definition: the product of the admitted sets of the resolved binders in scope (SPEC 8.15). False
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
    // every case is (SPEC 8.15). Its problems are published after the last case, with the cases each was found under, and a problem
    // every case found shows no case (SPEC 23.1.2). Binding bounds the cases (PairBinder.CaseBound) before the analysis runs.
    private void BuildCases(FunctionKoto function, int declarationDefault)
    {
        this.caseBodyCount = 0;
        this.caseBit = 1;
        this.BuildOnce(function, declarationDefault);
        var listed = this.body;
        this.caseListed = listed;
        listed.AppendCleanupCase(listed.Cases.Span);
        this.compilation.Binding.CollectCaseDestructions(listed, listed, this.caseBit);
        var run = 1;
        while (this.NextCase())
        {
            if (this.caseBodyCount == this.casePool.Count)
            {
                this.casePool.Add(new());
            }

            var side = this.casePool[this.caseBodyCount++];
            this.caseBit <<= 1;
            this.caseBody = side;
            this.BuildOnce(function, declarationDefault);
            this.caseBody = null;
            listed.AppendCleanupCase(side.Cases.Span);
            this.compilation.Binding.CollectCaseDestructions(side, listed, this.caseBit);
            listed.IsVerified &= side.IsVerified;
            run++;
        }

        this.caseListed = null;
        this.caseCount = 0;
        this.caseBit = 0;
        this.body = listed;
        listed.DropUniversalCases(run == PairBinder.CaseBound ? ulong.MaxValue : (1UL << run) - 1);
        for (var i = 0; i < listed.IssueStorage.Count; i++)
        {
            this.issues.Add(listed.IssueStorage[i]);
        }
    }

    // Appends the built body's problems to the result. In a case run the listed body publishes after the last case (BuildCases)
    // and a side body's problems merge into it (SPEC 23.1.2).
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
}
