// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The computation behind TyCtxt.Retains, under its contract (TyCtxt.Retention.cs): a contract's Origin atoms and carriers
// (Binding.Retention.Atoms.cs), the premises its callee's body is checked under in one premise environment (its instantiated own part,
// then the premise catalog's walk outward from its binder's parent), the relations of its erased callables, and the rows
// (Binding.Retention.Rows.cs). Each entry point, Retains for a callee's contract and RetainsErased for an erased callable's, runs its
// own steps between BeginRetention and EndRetention; the premise scope TyCtxt read an instance under is RetentionRows' parameter.
public sealed partial class Binding
{
    // The occurrences in the contract being computed (RetentionAtom), the erased callable Types it names and each carrier's node,
    // reused.
    private readonly List<RetentionAtom> retentionAtoms = new();
    private readonly List<BoundType> retentionCallables = new();
    private readonly Dictionary<BoundType, int> retentionCarriers = new(ReferenceEqualityComparer.Instance);

    // The inputs (and declaring Type) of the contract or erased signature being walked, whose borrows a raw layer's Origin belongs to
    // (RawContent), reused, and how many of them are a callee's own signature inputs, which the signature cap reads (SignatureStep; none
    // for an erased signature, a Function Type's or a Callable bound's, whose raw layers read their kind: SPEC 5.2.1 caps a layer written
    // in a function's own signature); the (Origin, input) pairs whose content that walk has added, so each is walked once; and whether a
    // member walk was incomplete (CallRetention.Limit).
    private readonly List<BoundType> retentionScope = new();
    private readonly HashSet<(BoundOrigin Origin, int Input)> retentionRawVisits = new();
    private int retentionInputs;
    private bool retentionLimit;

    // SPEC 15.6.4: the retention of a callee's contract whose inputs and result are instantiated (a function, or the closure or item a
    // Value call names), under the premises its body is checked under: its own part, its clauses substituted under the declaring Type,
    // and the catalog's walk from its binder's parent outward. An output retains an input when the premises entail that an atom of the
    // input outlives an atom of the output, or when both name one carrier. Its Callable bounds (CallableBoundSignatures), instantiated as its
    // inputs, are erased callables its body may call. `premises`: the scope TyCtxt read an instance with an abstract part under (null:
    // none), which the transfer rule's Iterator key reads (Binding.Retention.Transfers.cs).
    internal CallRetention Retains(
        FunctionKoto callee,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        BoundType? declaring,
        ReadOnlySpan<BoundType> bounds,
        BindingScope? premises)
    {
        var use = callee.Accessor?.Binder ?? callee;
        var environment = this.BeginRetention(use, inputs, result, declaring, inputs.Length);
        try
        {
            this.CollectOriginResolvers(environment);
            this.RetentionPremises(environment, callee, inputs, result, declaring, bounds);
            if (this.retentionLimit)
            {
                return CallRetention.Limit;
            }

            var nodes = this.RetentionNodes(environment);
            this.ExtractOriginPremises(environment, use.Parent);
            return this.RetentionRows(environment, nodes, inputs, callee, premises);
        }
        finally
        {
            this.EndRetention(environment);
        }
    }

    // An erased callable's retention, one whose implementation the call cannot see (a Function Type value, or a value of a Callable-bounded
    // Type parameter): it reads no declaration's premises, only its own well-formedness and the relations of its fixed Origins
    // (AddErasedRelations), which make the premises of `use`, the calling definition, redundant except for its pair layers' admitted cases.
    internal CallRetention RetainsErased(Koto use, ReadOnlySpan<BoundType> inputs, BoundType result)
    {
        var environment = this.BeginRetention(use, inputs, result, null, 0);
        try
        {
            this.RetentionPremises(environment, null, inputs, result, null, default);
            if (this.retentionLimit)
            {
                return CallRetention.Limit;
            }

            var nodes = this.RetentionNodes(environment);
            this.AddErasedRelations(environment, 0);
            return this.RetentionRows(environment, nodes, inputs, null, null);
        }
        finally
        {
            this.EndRetention(environment);
        }
    }

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

    // Begins a retention computation at `use`: resets the per-computation fields, registers each input's atoms and holds and the result's
    // atoms and its one hold, its whole Type (Held walks what lies behind its borrows as lent, DECISIONS BU-40), over the scope of the
    // inputs and declaring Type, of which `signatureInputs` are a callee's own signature inputs (none for an erased signature), then rents
    // the premise environment, which EndRetention returns.
    private OriginPremiseEnvironment BeginRetention(Koto use, ReadOnlySpan<BoundType> inputs, BoundType result, BoundType? declaring, int signatureInputs)
    {
        this.retentionAtoms.Clear();
        this.retentionCallables.Clear();
        this.retentionCarriers.Clear();
        this.retentionRawInputs.Clear();
        this.retentionHolds.Clear();
        this.retentionScope.Clear();
        this.retentionScope.AddRange(inputs);
        this.retentionInputs = signatureInputs;
        if (declaring is not null)
        {
            this.retentionScope.Add(declaring);
        }

        this.retentionRawVisits.Clear();
        this.retentionLimit = false;
        for (var i = 0; i < inputs.Length; i++)
        {
            this.RetentionInput(inputs[i], i);
        }

        this.RetentionWalk(result, new(Retention.Result, RetentionKind.Value, Writable: false, null, null, null), OriginVariance.Covariant, callable: false);
        this.retentionHolds.Add((Retention.Result, result));
        return this.RentPremises(use) ??
            throw new InvalidOperationException($"The premise environment of a retention at `{use}` is not ready: it depends on itself (PLAN G74).");
    }

    // Ends a retention computation, from each entry point's `finally`: returns its premise environment and clears what it registered.
    private void EndRetention(OriginPremiseEnvironment environment)
    {
        this.ReturnPremises(environment);
        this.retentionAtoms.Clear();
        this.retentionCallables.Clear();
        this.retentionCarriers.Clear();
        this.retentionRawInputs.Clear();
        this.retentionHolds.Clear();
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

    // An erased callable signature's own atoms, walked after the contract's, its relations added, then dropped with its raw inputs and
    // holds, which are no outputs of the contract.
    private void AddErasedSignature(OriginPremiseEnvironment environment, BoundType signature)
    {
        var atoms = this.retentionAtoms;
        var (start, raws, holds) = (atoms.Count, this.retentionRawInputs.Count, this.retentionHolds.Count);
        var inputs = signature.Components[0];
        this.retentionScope.Clear();
        if (!ReferenceEquals(inputs, BoundType.Unit))
        {
            this.retentionScope.AddRange(inputs.Components);
        }

        this.retentionInputs = 0;
        this.retentionRawVisits.Clear();

        for (var i = 0; !ReferenceEquals(inputs, BoundType.Unit) && i < inputs.Components.Count; i++)
        {
            this.RetentionInput(inputs.Components[i], i);
        }

        var result = new RetentionAtom(Retention.Result, RetentionKind.Value, Writable: false, null, null, null);
        this.RetentionWalk(signature.Components[1], result, OriginVariance.Covariant, callable: false);
        this.AddErasedRelations(environment, start);
        atoms.RemoveRange(start, atoms.Count - start);
        this.retentionRawInputs.RemoveRange(raws, this.retentionRawInputs.Count - raws);
        this.retentionHolds.RemoveRange(holds, this.retentionHolds.Count - holds);
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
                    AddPremiseEdge(environment, longer, this.AtomNode(environment, atoms[j]), OriginPremiseRule.Input);
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
                AddPremiseEdge(environment, nodes[i], this.PremiseNode(environment, layer), OriginPremiseRule.Input);
            }
        }

        return nodes;
    }
}
