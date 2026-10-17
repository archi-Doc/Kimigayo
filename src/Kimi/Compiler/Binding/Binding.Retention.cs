// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The computation behind TyCtxt.Retains, under its contract (TyCtxt.Retention.cs): a contract's Origin atoms and carriers, the premises
// its callee's body is checked under in one premise environment (its instantiated own part, then the premise catalog's walk outward
// from its binder's parent), the relations of its erased callables, and one closure from each output atom.
public sealed partial class Binding
{
    // The occurrences in the contract being computed (RetentionAtom), the erased callable Types it names, each carrier's node and the
    // rows, reused.
    private readonly List<RetentionAtom> retentionAtoms = new();
    private readonly List<BoundType> retentionCallables = new();
    private readonly Dictionary<BoundType, int> retentionCarriers = new(ReferenceEqualityComparer.Instance);
    private readonly List<Retention> retentionRows = new();

    // SPEC 15.6.4: the retention of a callee's contract whose inputs and result are instantiated (a function, or the closure or item a
    // Value call names), under the premises its body is checked under: its own part, its clauses substituted under the declaring Type,
    // and the catalog's walk from its binder's parent outward. An output retains an input when the premises entail that an atom of the
    // input outlives an atom of the output, or when both name one carrier. Its Callable bounds (CallableBoundSignatures), instantiated as its
    // inputs, are erased callables its body may call.
    internal CallRetention Retains(FunctionKoto callee, ReadOnlySpan<BoundType> inputs, BoundType result, BoundType? declaring, ReadOnlySpan<BoundType> bounds)
        => this.RetentionOf(callee.Accessor?.Binder ?? callee, callee, inputs, result, declaring, bounds);

    // An erased callable's retention, one whose implementation the call cannot see (a Function Type value, or a value of a Callable-bounded
    // Type parameter): it reads no declaration's premises, only its own well-formedness and the relations of its fixed Origins
    // (AddErasedRelations), which make the premises of `use`, the calling definition, redundant except for its pair layers' admitted cases.
    internal CallRetention RetainsErased(Koto use, ReadOnlySpan<BoundType> inputs, BoundType result)
        => this.RetentionOf(use, null, inputs, result, null, default);

    // SPEC 8.10: the signatures of the Callable bounds among the premises at `use`, in its constraint scope and those enclosing it.
    internal void CallableBoundSignatures(Koto use, List<BoundType> bounds)
    {
        bounds.Clear();
        for (var current = this.ConstraintScope(use); current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } constraints)
            {
                continue;
            }

            foreach (var fact in constraints.Facts)
            {
                if (fact.Kind == ConstraintKind.Callable && this.AvailableConstraintFact(constraints, fact))
                {
                    bounds.Add(fact.RequiredType!);
                }
            }
        }
    }

    // A fixed Origin of an erased callable: a carrier's hidden Origins (R4-M36), or an Origin neither `static` nor one of its per-call
    // Origins, which only its own syntax binds and no nested signature names (SPEC 8.10, 15.3.4).
    private static bool IsFixed(in RetentionAtom atom)
        => atom.Carrier is not null || atom.Origin is { Kind: not OriginKind.Static } and not { Kind: OriginKind.Input, Binder: FunctionTypeKoto };

    // An input retained through one of its occurrences: Value over Content.
    private static void Mark(RetentionKind?[] kinds, in RetentionAtom atom)
        => kinds[atom.Input] = atom.Kind == RetentionKind.Value ? RetentionKind.Value : kinds[atom.Input] ?? RetentionKind.Content;

    private CallRetention RetentionOf(
        Koto use,
        FunctionKoto? callee,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        BoundType? declaring,
        ReadOnlySpan<BoundType> bounds)
    {
        this.retentionAtoms.Clear();
        this.retentionCallables.Clear();
        this.retentionCarriers.Clear();
        for (var i = 0; i < inputs.Length; i++)
        {
            this.RetentionInput(inputs[i], i);
        }

        this.RetentionWalk(result, new(Retention.Result, RetentionKind.Value, Writable: false, null, null, null), OriginVariance.Covariant, callable: false);
        var environment = this.RentPremises(use) ??
            throw new InvalidOperationException($"The premise environment of a retention at `{use}` is not ready: it depends on itself (PLAN G74).");
        try
        {
            if (callee is not null)
            {
                this.CollectOriginResolvers(environment);
            }

            this.RetentionPremises(environment, callee, inputs, result, declaring, bounds);
            var nodes = this.RetentionNodes(environment);
            if (callee is null)
            {
                this.AddErasedRelations(environment, 0);
            }
            else
            {
                this.ExtractOriginPremises(environment, use.Parent);
            }

            return this.RetentionRows(environment, nodes, inputs.Length);
        }
        finally
        {
            this.ReturnPremises(environment);
            this.retentionAtoms.Clear();
            this.retentionCallables.Clear();
            this.retentionCarriers.Clear();
        }
    }

    // An input's atoms: a borrowed input gives Value through its outer Origin and Content through its referent (SPEC 15.6.4 step 4),
    // which an exclusive one lets the call write.
    private void RetentionInput(BoundType input, int index)
    {
        if (input is { Kind: BoundTypeKind.Semantics, Components: [var referent] } borrow && IsBorrow(borrow.Semantics))
        {
            this.AddRetentionAtom(new(index, RetentionKind.Value, Writable: false, borrow.Origin, null, null), callable: false);
            var content = new RetentionAtom(index, RetentionKind.Content, IsExclusive(borrow.Semantics), null, null, borrow.Origin);
            this.RetentionWalk(referent, content, this.ComponentVariance(borrow, 0), callable: false);
        }
        else
        {
            this.RetentionWalk(input, new(index, RetentionKind.Value, Writable: true, null, null, null), OriginVariance.Covariant, callable: false);
        }
    }

    // The atoms and carriers of a Type at a position of `variance` within its input (OwnedOrigins and the outer Origin, SPEC 15.2.3): a pair
    // layer's slot and target; a Type parameter or a pair's target as a carrier; the Origin, Origin arguments and components of any other,
    // so a projection or a dependent Semantics term is its parts and joins its base. An occurrence is writable storage of its input at an
    // invariant position the walk reaches through storage the call may write (`at.Writable`): not past a shared layer, where only reads
    // are possible (SPEC 15.6.2), unless a raw pointer is followed again (raw access within its authority, SPEC 5.2.1); not past a pair
    // layer whose binder admits no exclusive Semantics; never in a callable Type, which stores nothing and whose per-call Origins belong to
    // no value of it (SPEC 15.2.3).
    private void RetentionWalk(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        var here = at with { Writable = at.Writable && variance == OriginVariance.Invariant };
        if (TryPairLayer(type, out _, out var target))
        {
            var exclusive = IsInvariantLayer(type, this);
            var slot = this.OuterOrigin(type);
            this.AddRetentionAtom(here with { Origin = slot }, callable);
            var inner = ComposeVariance(variance, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant);
            this.RetentionWalk(target, at with { Writable = at.Writable && exclusive, Layer = slot ?? at.Layer }, inner, callable);
            return;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            this.retentionAtoms.Add(here with { Carrier = type });
        }
        else if (type.Kind == BoundTypeKind.Function)
        {
            this.retentionCallables.Add(type);
        }

        this.AddRetentionAtom(here with { Origin = type.Origin }, callable);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var position = ComposeVariance(variance, type.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant);
            this.AddRetentionAtom(at with { Origin = type.OriginArguments[i], Writable = at.Writable && position == OriginVariance.Invariant }, callable);
        }

        var layer = (type.Kind == BoundTypeKind.Semantics && IsBorrow(type.Semantics)) || type.Kind == BoundTypeKind.Slice ? type.Origin ?? at.Layer : at.Layer;
        var inside = callable || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem;
        var shared = type.Kind == BoundTypeKind.Slice ||
            (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc);
        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        var below = at with { Layer = layer, Writable = !inside && (raw || (at.Writable && !shared)) };
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.RetentionWalk(type.Components[i], below, ComposeVariance(variance, this.ComponentVariance(type, i)), inside);
        }
    }

    // An Origin occurrence; a Function Type's per-call Origin, bound by its own syntax, belongs to no value of it (SPEC 15.3.4).
    private void AddRetentionAtom(RetentionAtom atom, bool callable)
    {
        if (atom.Origin is { } origin && !(callable && origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto }))
        {
            this.retentionAtoms.Add(atom);
        }
    }

    // The contract's own premises (SPEC 15.6.4 step 3): each input's well-formedness and Types' relations (R2), the declaring Type's
    // relations, the result's well-formedness (R3) and the callee's declared relations (R1) under its declaring Type, its own Origins
    // kept as the call's fresh regions; then the relations of each erased callable it may call, its Callable bounds and the callable
    // Types the contract names, nested ones included as the list grows. The enclosing declarations' premises follow from the catalog's
    // walk (ExtractOriginPremises).
    private void RetentionPremises(
        OriginPremiseEnvironment environment,
        FunctionKoto? callee,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        BoundType? declaring,
        ReadOnlySpan<BoundType> bounds)
    {
        foreach (var input in inputs)
        {
            this.AddTypePremises(environment, input, OriginPremiseRule.Input);
        }

        if (declaring is not null)
        {
            this.AddTypePremises(environment, declaring, OriginPremiseRule.Input);
        }

        this.AddResultPremises(environment, result);
        if (callee is not null && this.originDeclarations.TryGetValue(callee.Accessor?.Binder ?? callee, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var longer = this.SubstituteClauseOrigin(relation.Longer, callee, default, default, declaring);
                var shorter = this.SubstituteClauseOrigin(relation.Shorter, callee, default, default, declaring);
                this.AddPremise(environment, longer, shorter, relation.Equality, OriginPremiseRule.Declaration);
            }
        }

        foreach (var bound in bounds)
        {
            this.AddErasedSignature(environment, bound);
        }

        for (var i = 0; i < this.retentionCallables.Count; i++)
        {
            this.AddErasedSignature(environment, this.retentionCallables[i]);
        }
    }

    // An erased callable signature's own atoms, walked after the contract's, its relations added, then dropped.
    private void AddErasedSignature(OriginPremiseEnvironment environment, BoundType signature)
    {
        var atoms = this.retentionAtoms;
        var start = atoms.Count;
        var inputs = signature.Components[0];
        for (var i = 0; !ReferenceEquals(inputs, BoundType.Unit) && i < inputs.Components.Count; i++)
        {
            this.RetentionInput(inputs.Components[i], i);
        }

        var result = new RetentionAtom(Retention.Result, RetentionKind.Value, Writable: false, null, null, null);
        this.RetentionWalk(signature.Components[1], result, OriginVariance.Covariant, callable: false);
        this.AddErasedRelations(environment, start);
        atoms.RemoveRange(start, atoms.Count - start);
    }

    // An erased callable's relations (owner decision 2026-10-17, DECISIONS R4-M52): its implementation was compared where it was
    // converted, under premises no view reads, so no absence of a relation among its fixed Origins is proven (SPEC 15.6.4): each fixed
    // Origin of an input, among the atoms from `start`, outlives each fixed Origin of an output, its result's or one its inputs let it
    // write. Its per-call Origins keep only its own premises.
    private void AddErasedRelations(OriginPremiseEnvironment environment, int start)
    {
        var atoms = this.retentionAtoms;
        for (var i = start; i < atoms.Count; i++)
        {
            if (atoms[i].Input < 0 || !IsFixed(atoms[i]))
            {
                continue;
            }

            var longer = this.AtomNode(environment, atoms[i]);
            for (var j = start; j < atoms.Count; j++)
            {
                if ((atoms[j].Input == Retention.Result || atoms[j].Writable) && IsFixed(atoms[j]))
                {
                    environment.Closure.AddEdge(longer, this.AtomNode(environment, atoms[j]), OriginPremiseRule.Input);
                }
            }
        }
    }

    // An atom's node: an Origin's own, or a carrier's one unnamed node for the computation, the Origins its instances hide (SPEC 15.3.5,
    // R4-M36), so occurrences of one carrier share it.
    private int AtomNode(OriginPremiseEnvironment environment, in RetentionAtom atom)
    {
        if (atom.Carrier is not { } carrier)
        {
            return this.PremiseNode(environment, atom.Origin!);
        }

        if (!this.retentionCarriers.TryGetValue(carrier, out var node))
        {
            node = environment.Closure.Node();
            this.retentionCarriers.Add(carrier, node);
        }

        return node;
    }

    // Each atom's node, interned before the catalog's walk, so its local-Place premises (R5) reach them; a carrier's hidden Origins outlive
    // the layer above each of its occurrences (R2, SPEC 15.6.1).
    private int[] RetentionNodes(OriginPremiseEnvironment environment)
    {
        var atoms = this.retentionAtoms;
        var nodes = new int[atoms.Count];
        for (var i = 0; i < atoms.Count; i++)
        {
            nodes[i] = this.AtomNode(environment, atoms[i]);
            if (atoms[i] is { Carrier: not null, Layer: { } layer })
            {
                environment.Closure.AddEdge(nodes[i], this.PremiseNode(environment, layer), OriginPremiseRule.Input);
            }
        }

        return nodes;
    }

    // One closure from each output atom (OriginPremiseClosure, every Semantics case's premises followed) marks the inputs whose atoms it
    // reaches, Value over Content, a carrier's through its node. The result is untied when one of its non-static atoms marks no input. The
    // work is what the closures reach, each node with its incoming premises once per closure.
    private CallRetention RetentionRows(OriginPremiseEnvironment environment, int[] nodes, int inputCount)
    {
        // The sources at a node, linked atom indexes.
        var atoms = this.retentionAtoms;
        var first = new int[environment.Closure.NodeCount];
        var next = new int[atoms.Count];
        Array.Fill(first, -1);
        for (var i = 0; i < atoms.Count; i++)
        {
            if (atoms[i].Input >= 0)
            {
                (next[i], first[nodes[i]]) = (first[nodes[i]], i);
            }
        }

        var rows = this.retentionRows;
        rows.Clear();
        var untied = false;
        var kinds = new RetentionKind?[inputCount];
        for (var output = Retention.Result; output < inputCount; output++)
        {
            Array.Clear(kinds);
            for (var i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Input == output && (output == Retention.Result || atoms[i].Writable))
                {
                    var tied = this.MarkClosure(environment.Closure, nodes[i], first, next, kinds);
                    untied |= output == Retention.Result && !tied && atoms[i].Origin?.Kind != OriginKind.Static;
                }
            }

            for (var i = 0; i < inputCount; i++)
            {
                if (kinds[i] is { } kind)
                {
                    rows.Add(new(i, kind, output));
                }
            }
        }

        return new(rows.ToArray(), untied);
    }

    // The closure from one target node, marking the inputs whose atoms it reaches; whether it marked one.
    private bool MarkClosure(OriginPremiseClosure closure, int target, int[] first, int[] next, RetentionKind?[] kinds)
    {
        var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
        closure.Close(target, -1, ulong.MaxValue);
        this.OriginProofMetrics?.Closure(closure, this.originPremiseDepth > 1, Stopwatch.GetTimestamp() - start);
        var tied = false;
        for (var k = 0; k < closure.LastWork.Insertions; k++)
        {
            for (var source = first[closure.ReachedNode(k)]; source >= 0; source = next[source])
            {
                Mark(kinds, this.retentionAtoms[source]);
                tied = true;
            }
        }

        return tied;
    }

    // An Origin atom or a carrier of an input or of the result (Retention.Result): the kind it gives the input, whether it is writable
    // storage of the input (RetentionWalk), and the Origin of the nearest borrow, Slice or pair layer above it, which a carrier's hidden
    // Origins outlive (SPEC 15.6.1).
    private readonly record struct RetentionAtom(int Input, RetentionKind Kind, bool Writable, BoundOrigin? Origin, BoundType? Carrier, BoundOrigin? Layer);
}
