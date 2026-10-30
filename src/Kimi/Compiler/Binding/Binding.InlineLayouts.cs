// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Inline layouts (SPEC 6.2 Finite inline storage; PLAN G310; DECISIONS BU-65): the struct and enum declarations whose storage, their own
// parameters open, reaches the same declaration again, with any Type arguments, through by-value positions: Fields, the base, Case
// payloads, Tuple and fixed-array elements, and the parts a declaration stores inline of its arguments, a projection's included as SPEC
// 8.6.1 completes it. A borrow, raw pointer, object handle or sequence is an indirection.
// - Inputs: the prepared storage (AdtDef.Types, after PrepareStorage), ProjectionSpecification's outcome (a binding with its Subject and
//   conformance path), the premise proof of a node's scope, and Binding.IsRecursive (SPEC 8.3.3).
// - Outputs: AdtDef.InlineCycle and one InvalidInlineLayout_Kd at each such declaration, in storageDeclarations order.
// - Representation: a node per struct or enum declaration (its storage walked and proven in the declaration's scope) and per associated
//   binding (its binding walked and proven in its conformance path's Scope, SPEC 8.8.2's D + P, keyed by that path), each with its
//   summary: the abstract parts its storage reaches by value. An instance at a by-value position adds the edge to its declaration and
//   walks that summary under the instance; a completed projection adds the edge to its binding's node and walks that summary under the
//   Subject (an inherited path's binding is its source's, on the base clause's instance). A declaration is invalid iff it lies on a cycle
//   of the edges.
// - Invariants: Tarjan over the edges the walks discover, each summary computed before its first read (every read follows its edge, so
//   dependencies are edges), a cyclic component re-walked until no summary grows: the result is the cycles of the least-fixpoint edge
//   graph, whatever the declaration order. Termination: nodes are finite, a walk visits the written DAG, its substituted subterms and
//   the projections it resolves to nodes, and a summary holds a node's parameters, pair applications and their formable projections
//   through non-recursive Contracts, finitely many (AddInlinePart). Each walk is one query of layoutParts (DECISIONS BU-19), so it is
//   linear in the distinct parts of its storage. A warm pass reuses its nodes and allocates nothing.
// - Prohibitions: no instance walk of a declaration's storage, no record for a binding node (a cycle of binding nodes alone is its
//   conformance's error, SPEC 8.3.3), no limit, no read of TyCtxt.RecursiveContracts' post-Bind memo inside Bind.
public sealed partial class Binding
{
    private readonly Dictionary<(object Key, BoundType? Binding), InlineSummary> inlineSummaries = new();
    private readonly List<InlineSummary> inlineSummaryPool = new();
    private readonly List<InlineSummary> inlineComponents = new(); // Tarjan's stack: the nodes whose component is open.
    private readonly Dictionary<BindingSymbol, bool> inlineRecursive = new(ReferenceEqualityComparer.Instance);
    private readonly TypeMemo<bool> layoutParts = new(); // The composite parts one node's storage walk met.
    private int inlineIndex;
    private long inlineGrowth;

    private void ValidateInlineLayouts()
    {
        this.inlineSummaries.Clear();
        this.inlineRecursive.Clear();
        this.inlineIndex = 0;
        foreach (var container in this.storageDeclarations)
        {
            container.BoundSymbol!.Adt!.InlineCycle = false;
        }

        foreach (var container in this.storageDeclarations)
        {
            if (this.Summary(container, null, this.scopes[container]) is { Index: 0 } summary)
            {
                this.SummarizeInlineStorage(summary);
            }
        }

        foreach (var container in this.storageDeclarations)
        {
            if (this.Summary(container, null, this.scopes[container]).Cyclic)
            {
                container.BoundSymbol!.Adt!.InlineCycle = true;
                this.Fail(container, BindingFailure.InvalidInlineLayout);
            }
        }
    }

    // A node's summary in this run, taken from the pool on its first request: a declaration's (key the declaration, binding null) or an
    // associated binding's (key its conformance path), walked and proven in `scope`.
    private InlineSummary Summary(object key, BoundType? binding, BindingScope scope)
    {
        if (!this.inlineSummaries.TryGetValue((key, binding), out var summary))
        {
            var count = this.inlineSummaries.Count;
            if (count == this.inlineSummaryPool.Count)
            {
                this.inlineSummaryPool.Add(new());
            }

            summary = this.inlineSummaryPool[count];
            summary.Reset(key as DeclarationContainerKoto, binding, scope);
            this.inlineSummaries.Add((key, binding), summary);
        }

        return summary;
    }

    // Tarjan's visit: the node's walk, then, at a component's root, its members re-walked while a summary grows (a cyclic component only:
    // a node on no cycle reads only finished summaries). A re-walk that reaches a node below the root merges the component into the
    // enclosing one, whose root re-walks the union.
    private void SummarizeInlineStorage(InlineSummary summary)
    {
        summary.Index = summary.Low = ++this.inlineIndex;
        var start = this.inlineComponents.Count;
        this.inlineComponents.Add(summary);
        summary.OnStack = true;
        this.WalkInlineStorage(summary);
        if (summary.Low != summary.Index)
        {
            return;
        }

        var cyclic = this.inlineComponents.Count - start > 1 || summary.SelfEdge;
        for (var growth = -1L; cyclic && growth != this.inlineGrowth;)
        {
            growth = this.inlineGrowth;
            for (var i = start; i < this.inlineComponents.Count; i++)
            {
                this.WalkInlineStorage(this.inlineComponents[i]);
                summary.Low = Math.Min(summary.Low, this.inlineComponents[i].Low);
            }

            if (summary.Low != summary.Index)
            {
                return;
            }
        }

        for (var i = start; i < this.inlineComponents.Count; i++)
        {
            (this.inlineComponents[i].OnStack, this.inlineComponents[i].Cyclic) = (false, cyclic);
        }

        this.inlineComponents.RemoveRange(start, this.inlineComponents.Count - start);
    }

    // One walk of a node's storage, its parameters open, as one query of layoutParts: a part adds the same edges and summary parts wherever
    // it is met, given the summaries it reads, so each distinct composite part is walked once per walk (DECISIONS BU-19), and a
    // component's last round, which reads fixed summaries, is exact.
    private void WalkInlineStorage(InlineSummary summary)
    {
        var query = this.layoutParts.Begin(this.storageVersion);
        if (summary.Binding is { } binding)
        {
            this.VisitInlinePart(summary, binding, query);
            return;
        }

        var shape = summary.Container!.BoundSymbol!.Adt!;
        for (var i = 0; i < shape.Types.Count; i++)
        {
            this.VisitInlinePart(summary, shape.Types[i].BoundType, query);
        }
    }

    private void VisitInlinePart(InlineSummary summary, BoundType? part, uint query)
    {
        if (part is null)
        {
            return;
        }

        var instance = (AdtDef.IsStruct(part) || AdtDef.IsEnum(part)) && part.Symbol!.Adt is not null;
        var projection = part.Kind == BoundTypeKind.AssociatedProjection;
        if (part.IsAbstract && !projection)
        {
            this.AddInlinePart(summary, part); // A parameter, a pair application or target: the instance decides what it stores.
            return;
        }

        if ((!instance && !projection && part.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray)) ||
            (part.Components.Count != 0 && this.layoutParts.TryGet(part, query, out _)))
        {
            return; // Primitives, borrows, objects, sequences and raw pointers store no inline copy of a declaration.
        }

        if (part.Components.Count != 0)
        {
            this.layoutParts.Set(part, query, true);
        }

        if (projection)
        {
            this.VisitInlineProjection(summary, part, query);
        }
        else if (instance)
        {
            var declaration = (DeclarationContainerKoto)part.Symbol!.Declaration;
            this.VisitInlineNode(summary, this.Summary(declaration, null, this.scopes[declaration]), part, query);
        }
        else
        {
            for (var i = 0; i < part.Components.Count; i++)
            {
                this.VisitInlinePart(summary, part.Components[i], query);
            }
        }
    }

    // A projection at a by-value position, resolved in the walking node's scope (SPEC 8.6.1): a binding adds the edge to its node and
    // walks that node's summary under the Subject; a builtin output is walked; a pending projection is a summary part (AddInlinePart).
    private void VisitInlineProjection(InlineSummary summary, BoundType part, uint query)
    {
        var outcome = this.ProjectionSpecification(part, summary.Scope);
        if (outcome.Type is not { } specified)
        {
            if (outcome.Pending)
            {
                this.AddInlinePart(summary, part);
            }
        }
        else if (outcome.Subject is not { } subject)
        {
            this.VisitInlinePart(summary, specified, query);
        }
        else if (outcome.Path!.InheritedFrom is not null)
        {
            // An inherited path's binding is its source's, formed in the source's conformance scope: the projection is resolved again on
            // the base clause's instance, where ProveConformanceConditions proves an inherited path's conditions.
            if (this.StoredType(outcome.Path.InheritedBase!, subject) is { } parent)
            {
                this.VisitInlinePart(summary, this.WithComponent(part, 0, parent), query);
            }
        }
        else
        {
            this.VisitInlineNode(summary, this.Summary(outcome.Path, specified, outcome.Path.Scope), subject, query);
        }
    }

    // The edge from `summary` to `target` (a Tarjan step), then the target's summary under `instance`, an instance of its declaration.
    private void VisitInlineNode(InlineSummary summary, InlineSummary target, BoundType instance, uint query)
    {
        if (target.Index == 0)
        {
            this.SummarizeInlineStorage(target);
            summary.Low = Math.Min(summary.Low, target.Low);
        }
        else if (target.OnStack)
        {
            summary.Low = Math.Min(summary.Low, target.Index);
        }

        summary.SelfEdge |= ReferenceEquals(target, summary);
        for (var i = 0; i < target.Parts.Count; i++)
        {
            this.VisitInlinePart(summary, this.InlinePartUnder(target.Parts[i], instance), query);
        }
    }

    // A summary part under an instance of its node's declaration: a projection with its components substituted (its subject completed),
    // for VisitInlineProjection to resolve as a by-value projection; any other part as a stored part reads it (PartType).
    private BoundType? InlinePartUnder(BoundType part, BoundType instance)
    {
        if (part.Kind != BoundTypeKind.AssociatedProjection)
        {
            return this.PartType(part, instance);
        }

        var result = part;
        for (var i = 0; i < part.Components.Count; i++)
        {
            if (this.PartType(part.Components[i], instance) is not { } component)
            {
                return null;
            }

            result = this.WithComponent(result, i, component);
        }

        return result;
    }

    // A Type with its component at `index` replaced.
    private BoundType WithComponent(BoundType type, int index, BoundType component)
    {
        if (ReferenceEquals(type.Components[index], component))
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = i == index ? component : type.Components[i];
            }

            return this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // A summary part (SPEC 8.3.3): a pending projection is recorded only when each step's subject is a parameter, a pair target or a
    // projection of one, proven in the node's scope to conform to the step's Contract, a non-recursive one. A recursive Contract's
    // projection never completes for a concrete argument (none conforms), so it adds no edge; an unproven step, or a pending step over a
    // constructed subject, is a dependent of the node's own requirement error (an instance's obligations make each substituted step
    // formable). The parts left are finitely many: an unbounded formable path repeats a Contract that supplies a conformance of a
    // projection of Self to itself, which makes it recursive.
    private void AddInlinePart(InlineSummary summary, BoundType part)
    {
        if (summary.Parts.Contains(part))
        {
            return;
        }

        for (var step = part; step.Kind == BoundTypeKind.AssociatedProjection; step = step.Components[0])
        {
            var subject = step.Components[0];
            var contract = step.Symbol!.Scope.Owner.BoundSymbol!;
            if (subject.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection) ||
                this.RecursiveInStorage(contract) ||
                this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, subject, contract: contract)), summary.Scope) != ConstraintProof.Proven)
            {
                return;
            }
        }

        summary.Parts.Add(part);
        this.inlineGrowth++;
    }

    // Whether a Contract is recursive (SPEC 8.3.3) under the pass's premise supply as it stands, the supply AddInlinePart's proofs read;
    // judged once per run, outside TyCtxt.RecursiveContracts' post-Bind memo, which a judgment inside Bind would fix before the source
    // states are final.
    private bool RecursiveInStorage(BindingSymbol contract)
    {
        if (!this.inlineRecursive.TryGetValue(contract, out var recursive))
        {
            this.inlineRecursive.Add(contract, recursive = contract.Contract is { } shape && this.IsRecursive(shape));
        }

        return recursive;
    }

    // A node's state in one run (ValidateInlineLayouts), pooled across runs.
    private sealed class InlineSummary
    {
        internal DeclarationContainerKoto? Container { get; private set; } // A declaration node's declaration.

        internal BoundType? Binding { get; private set; } // A binding node's binding, in its subject declaration's terms.

        internal BindingScope Scope { get; private set; } = null!;

        internal List<BoundType> Parts { get; } = new(); // The abstract parts its storage reaches by value.

        internal int Index { get; set; }

        internal int Low { get; set; }

        internal bool OnStack { get; set; }

        internal bool SelfEdge { get; set; }

        internal bool Cyclic { get; set; }

        internal void Reset(DeclarationContainerKoto? container, BoundType? binding, BindingScope scope)
        {
            (this.Container, this.Binding, this.Scope) = (container, binding, scope);
            (this.Index, this.Low, this.OnStack, this.SelfEdge, this.Cyclic) = (0, 0, false, false, false);
            this.Parts.Clear();
        }
    }
}
