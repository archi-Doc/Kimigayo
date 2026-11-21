// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 6.1, 9.1, 23.1.2: the pairwise checks (FailDuplicate: scopes, aliases, conformance registration, signatures, specializations)
    // fail both declarations and join their groups; only failed declarations need this storage. Each group retains its first declaration
    // and its earliest later one, independently of the order in which the pairwise checks find collisions, and RecordDuplicateGroups,
    // which owns the group's records, records them once the pass's last pairwise check has run.
    private Dictionary<Koto, (Koto Parent, Koto? Next)>? duplicateDeclarations;

    private static int CompareDeclarations(Koto left, Koto right)
    {
        var requirement = DiagnosticRequirement.Binding(BindingFailure.Duplicate);
        var x = left.KeyOf(requirement);
        var y = right.KeyOf(requirement);
        var order = (x.Source < 0 ? int.MaxValue : x.Source).CompareTo(y.Source < 0 ? int.MaxValue : y.Source);
        order = order != 0 ? order : x.Start.CompareTo(y.Start);
        return order != 0 ? order : x.Length.CompareTo(y.Length);
    }

    private static SourceSpan DuplicateSpan(Koto node) => node switch
    {
        VariableKoto variable => variable.NameKoto.Span,
        FunctionKoto function when function.SignatureSpan.Length != 0 => function.SignatureSpan,
        _ => node.Span,
    };

    // A later declaration's record: its name, at its declaring syntax, related to the group's first.
    private static void ReportDuplicate(Koto node, DiagnosticCollection? store, in BindingProblem problem, Koto first)
    {
        var name = node switch
        {
            AliasKoto alias => alias.Name!,
            IsKoto conformance => conformance.Right.ToString(),
            _ => node.BoundSymbol?.Name ?? (node as FunctionKoto)?.Name ?? "declaration",
        };
        node.ReportIn(store, problem.Requirement, problem.Code, evidence: [name], condition: problem.Condition, span: DuplicateSpan(node), relatedSpans: [("declaration", first, DuplicateSpan(first), "first declaration")]);
    }

    private void FailDuplicate(Koto first, Koto second, bool unresolved = false)
    {
        this.Fail(first, BindingFailure.Duplicate, unresolved);
        this.Fail(second, BindingFailure.Duplicate, unresolved);
        if (ReferenceEquals(first, second))
        {
            return;
        }

        var groups = this.duplicateDeclarations ??= new(ReferenceEqualityComparer.Instance);
        groups.TryAdd(first, (first, null));
        groups.TryAdd(second, (second, null));
        first = this.FirstDuplicateDeclaration(first);
        second = this.FirstDuplicateDeclaration(second);
        if (ReferenceEquals(first, second))
        {
            return;
        }

        if (CompareDeclarations(first, second) > 0)
        {
            (first, second) = (second, first);
        }

        var next = groups[first].Next;
        groups[first] = (first, next is not null && CompareDeclarations(next, second) < 0 ? next : second);
        groups[second] = (first, null);
    }

    private Koto FirstDuplicateDeclaration(Koto node)
    {
        while (this.duplicateDeclarations![node].Parent is { } parent && !ReferenceEquals(parent, node))
        {
            node = parent;
        }

        return node;
    }

    // SPEC 23.1.2: the step after the pass's last pairwise check (BindDeclarations' PrepareSpecializations; Index declared every scope, and
    // a try's deferred arms hold no written declaration), when each group is whole: one problem per later declaration, relating the group's
    // first (Related); the first rests on its earliest later (Cause), so it is derived from that record. Both declarations stay failed, as
    // the pairwise check left them. A pass reaches the step once: VerifyEarlyReads' rerun, asked before the step, is a new pass (RunPass)
    // whose start empties the report, so a group is recorded once it is whole, never beside an earlier record of its key.
    private void RecordDuplicateGroups()
    {
        if (this.duplicateDeclarations is not { Count: > 0 } groups)
        {
            return;
        }

        foreach (var (node, group) in groups)
        {
            var first = this.FirstDuplicateDeclaration(node);
            if (!ReferenceEquals(node, first))
            {
                this.Record(node, BindingFailure.Duplicate, [new(ProblemFactKind.Related, this.FactSite(first), 0)]);
            }
            else if (group.Next is { } later)
            {
                this.Record(node, BindingFailure.Duplicate, [new(ProblemFactKind.Cause, this.FactSite(later), -1)]);
            }
        }
    }
}
