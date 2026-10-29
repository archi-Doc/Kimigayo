// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The computation behind TyCtxt.Retains, under its contract (TyCtxt.Retention.cs): a contract's Origin atoms and carriers
// (Binding.Retention.Atoms.cs), the premises its callee's body is checked under in one premise environment (its instantiated own part,
// then the premise catalog's walk outward from its binder's parent), the relations of its erased callables, and the rows
// (Binding.Retention.Rows.cs). Each entry point, Retains for a callee's contract and RetainsErased for an erased callable's, runs its
// own steps between BeginRetention and EndRetention; the premise scope TyCtxt read an instance under is RetentionRows' parameter.
// The erased signature (ErasedSignature; DECISIONS R4-M154, Revision 9) is the one representation of a callable whose implementation the
// call cannot see:
// - Responsibility: what such a callable relates, read from its signature alone (AddErasedRelations): each fixed Origin of an input
//   outlives each fixed Origin of an output.
// - Inputs: the instantiated signature Type and the declaration whose syntax binds its per-call Origins, its Binder: a Function Type's
//   FunctionTypeKoto (FunctionTypeBinder, null where it binds none), a requirement's or a Virtual's own declaration, a Property
//   accessor's Binder (BoundAccessor.Binder: its declaration, else its Property's), never the accessor's FunctionKoto. TyCtxt.ErasedCallee
//   gives one for a requirement or Virtual callee on an abstract Self, TyCtxt.Retains for a
//   Value callee's signature, and HandedSignatures (with TyCtxt.RequirementSignatures) and HandedCallables for each callable a contract
//   hands its body.
// - Outputs: the relations (premise edges) the contract's environment takes.
// - Invariants: an Origin is per-call exactly when it is an input or parameter Origin whose binder is the signature's Binder (IsFixed);
//   any other Origin but `static` (a contract Type argument's, the calling contract's own) and a carrier's hidden Origins are fixed.
// - Prohibitions: no reader decides by a declaration's kind (IsRequirement, IsVirtual, a MIR callee kind or the syntax) whether a callee
//   is erased or an Origin per-call; each reads the pair.
public sealed partial class Binding
{
    // The occurrences in the contract being computed (RetentionAtom), the erased callable Types it names and each carrier's node,
    // reused.
    private readonly List<RetentionAtom> retentionAtoms = new();
    private readonly List<BoundType> retentionCallables = new();
    private readonly Dictionary<BoundType, int> retentionCarriers = new(ReferenceEqualityComparer.Instance);

    // Each carrier's ReachedNode (DECISIONS R4-M154, Revision 3): the storage a value of it may refer to that the body does not own, reached
    // only by a relation whose output is a writable carrier position (an erased writer's, AddErasedRelations), which fills the Abstract
    // entry (the storage walk's Abstract positions); a carrier's own node (retentionCarriers) is its value's. Reused.
    private readonly Dictionary<BoundType, int> retentionReachedNodes = new(ReferenceEqualityComparer.Instance);

    // The Origins the call's Type arguments, Self and declaring Type bring (Instantiation), reused.
    private readonly HashSet<BoundOrigin> retentionInstantiated = new(ReferenceEqualityComparer.Instance);

    // The (subject, Contract) premises whose requirement signatures HandedSignatures has listed, and the Types the contract's own Types
    // reach (Reach), reused.
    private readonly HashSet<(BoundType Subject, BindingSymbol Contract)> retentionContracts = new();
    private readonly HashSet<BoundType> retentionReached = new(ReferenceEqualityComparer.Instance);

    // The inputs (and declaring Type) of the contract or erased signature being walked, whose borrows a raw layer's Origin belongs to
    // (RawContent), reused, and how many of them are a callee's own signature inputs, which the signature cap reads (SignatureStep; none
    // for an erased signature, a Function Type's or a Callable bound's, whose raw layers read their kind: SPEC 5.2.1 caps a layer written
    // in a function's own signature); the (Origin, input) pairs whose content that walk has added, so each is walked once; and whether a
    // member walk was incomplete (CallRetention.Limit).
    private readonly List<BoundType> retentionErasedScope = new();
    private readonly HashSet<(BoundOrigin Origin, int Input)> retentionRawVisits = new();
    private List<BoundType> retentionScope = new();
    private int retentionInputs;
    private bool retentionLimit;

    // SPEC 15.6.4: the retention of a callee's contract whose inputs and result are instantiated (a function, or the closure or item a
    // Value call names), under the premises its body is checked under: its own part, its clauses substituted under the declaring Type,
    // and the catalog's walk from its binder's parent outward. An output retains an input when the premises entail that an atom of the
    // input outlives an atom of the output, or when both name one carrier. Its Callable bounds (HandedSignatures), instantiated as its
    // inputs, are erased callables its body may call. `types`: the call's Subst (null: a Value callee's declared signature), whose Type
    // arguments, Self and declaring Type bring the Origins a fit may relate under the call's solved Origins (Instantiation). `premises`:
    // the scope TyCtxt read an instance with an abstract part under (null: none), which the transfer rule's Iterator key reads
    // (Binding.Retention.Transfers.cs).
    internal CallRetention Retains(
        FunctionKoto callee,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        Subst? types,
        ReadOnlySpan<ErasedSignature> bounds,
        BindingScope? premises)
    {
        var use = callee.Accessor?.Binder ?? callee;
        var declaring = types?.Declaring;
        var environment = this.BeginRetention(use, inputs, result, declaring, inputs.Length);
        try
        {
            this.retentionParts.Clear();
            foreach (var type in types is null ? [] : types.Types)
            {
                this.Instantiation(type);
            }

            this.Instantiation(types?.Self);
            this.Instantiation(declaring);
            this.CollectOriginResolvers(environment);
            this.RetentionPremises(environment, callee, inputs, result, declaring, bounds);
            if (this.retentionLimit)
            {
                return CallRetention.Limit;
            }

            var nodes = this.RetentionNodes(environment);
            this.ExtractOriginPremises(environment, use.Parent);
            return this.RetentionRows(environment, nodes, inputs, result, callee, premises);
        }
        finally
        {
            this.EndRetention(environment);
        }
    }

    // An erased signature's retention (its instantiated inputs and result, and `binder`, the Binder of its per-call Origins): it reads no
    // declaration's premises, only its own well-formedness and the relations of its fixed Origins (AddErasedRelations), which make the
    // premises of `use`, the calling definition, redundant except for its pair layers' admitted cases.
    internal CallRetention RetainsErased(Koto use, ReadOnlySpan<BoundType> inputs, BoundType result, Koto? binder)
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
            this.AddErasedRelations(environment, 0, binder);
            return this.RetentionRows(environment, nodes, inputs, result, null, null);
        }
        finally
        {
            this.EndRetention(environment);
        }
    }

    // SPEC 8.10, 8.4, 15.6.4 (β′; DECISIONS R4-M52, R4-M154): the callables the premises at `use` hand its body, in its constraint scope and
    // those enclosing it, each one erased callable: each Callable bound's signature, and each requirement signature of each Contract premise
    // on its subject (TyCtxt.RequirementSignatures, a refinement's inherited requirements included), then of the Contract premises its
    // supply states on the subject's associated Types (ContractSupply, as a requirement call's resolution adds them, AddContractPremises)
    // that the contract's own Types reach (`signature`, its declared inputs and result: a projection
    // occurring in them or its base; a recursive clause, `associate Child is Branch`, grows no further), each (subject, Contract) once;
    // whether each substitutes (false: CallRetention.Limit).
    internal bool HandedSignatures(Koto use, ReadOnlySpan<BoundType> signature, List<ErasedSignature> handed)
    {
        handed.Clear();
        this.retentionContracts.Clear();
        this.retentionReached.Clear();
        foreach (var type in signature)
        {
            this.Reach(type);
        }

        var scope = this.ConstraintScope(use);
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } constraints)
            {
                continue;
            }

            foreach (var fact in constraints.Facts)
            {
                if (fact.Kind == ConstraintKind.Callable && this.AvailableConstraintFact(constraints, fact))
                {
                    handed.Add(new(fact.RequiredType!, FunctionTypeBinder(fact.RequiredType!)));
                }
                else if (fact is { Kind: ConstraintKind.Contract, Subject: { } subject, Contract: { } contract } &&
                    this.AvailableConstraintFact(constraints, fact) && !this.HandedContract(subject, contract, scope, handed))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // A fixed Origin of an erased signature (Revision 9): a carrier's hidden Origins (R4-M36), or an Origin neither `static` nor one of its
    // per-call Origins, an input or parameter Origin its own syntax binds (`binder`, the signature's Binder; SPEC 8.10, 15.3.4).
    private static bool IsFixed(in RetentionAtom atom, Koto? binder)
        => atom.Carrier is not null || (atom.Origin is { Kind: not OriginKind.Static } origin &&
            !(origin.Kind is OriginKind.Input or OriginKind.Parameter && binder is not null && ReferenceEquals(origin.Binder, binder)));

    // A Contract premise's requirement signatures (HandedSignatures), then those of the Contract premises its supply states on its subject's
    // associated Types that the contract's own Types reach (ContractSupply); whether each substitutes.
    private bool HandedContract(BoundType subject, BindingSymbol contract, BindingScope scope, List<ErasedSignature> handed)
    {
        if (contract.Contract is not { } shape || !this.retentionContracts.Add((subject, contract)))
        {
            return true;
        }

        if (!this.types.RequirementSignatures(subject, contract, handed))
        {
            return false;
        }

        foreach (var (_, _, premise) in new ContractSupply(this, shape, subject, scope))
        {
            if (premise is not { Kind: ConstraintKind.Contract, Subject: { } associated, Contract: { } required })
            {
                continue;
            }

            var reached = ReferenceEquals(associated, subject) || this.retentionReached.Contains(associated);
            if (reached && !this.HandedContract(associated, required, scope, handed))
            {
                return false;
            }
        }

        return true;
    }

    // Begins a retention computation at `use`: resets the per-computation fields, registers each input's atoms and the result's atoms,
    // over the scope of the inputs and declaring Type, of which `signatureInputs` are a callee's own signature inputs (none for an erased
    // signature), then rents the premise environment, which EndRetention returns.
    private OriginPremiseEnvironment BeginRetention(Koto use, ReadOnlySpan<BoundType> inputs, BoundType result, BoundType? declaring, int signatureInputs)
    {
        this.retentionAtoms.Clear();
        this.retentionCallables.Clear();
        this.retentionCarriers.Clear();
        this.retentionReachedNodes.Clear();
        this.retentionRawInputs.Clear();
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

        var atom = new RetentionAtom(CallRetention.ResultOutput, RetentionKind.Value, Writable: false, null, null, null);
        this.RetentionWalk(result, atom, OriginVariance.Covariant, callable: false);
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
        this.retentionReachedNodes.Clear();
        this.retentionRawInputs.Clear();
        this.retentionInstantiated.Clear();
    }

    // The Origins a Type of the call's Subst brings (retentionInstantiated; DECISIONS R4-M154, Revision 2): the caller's, whose relations
    // its solved Origins hold, each part once (retentionParts).
    private void Instantiation(BoundType? type)
    {
        if (type is null || !(type.CarriesOrigin || type.CarriesRawOrigin) || !this.retentionParts.Add(type))
        {
            return;
        }

        if (OwnedOrigin(type) is { Kind: not (OriginKind.Static or OriginKind.Unstated) } origin)
        {
            this.retentionInstantiated.Add(origin);
        }

        foreach (var argument in type.OriginArguments)
        {
            this.retentionInstantiated.Add(argument);
        }

        foreach (var part in type.Components)
        {
            this.Instantiation(part);
        }
    }

    // The contract's own premises (SPEC 15.6.4 step 3): each input's well-formedness and Types' relations (R2), the declaring Type's
    // relations, the result's well-formedness (R3) and the callee's declared relations (R1) under its declaring Type, its own Origins
    // kept as the call's fresh regions; then the relations of each erased callable it may call, its Callable bounds and the callables its
    // inputs and result hand it (TyCtxt.HandedCallables, nested ones included). The enclosing declarations' premises follow from the
    // catalog's walk (ExtractOriginPremises).
    private void RetentionPremises(
        OriginPremiseEnvironment environment,
        FunctionKoto? callee,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        BoundType? declaring,
        ReadOnlySpan<ErasedSignature> bounds)
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

        foreach (var input in inputs)
        {
            this.Handed(input, null);
        }

        this.Handed(result, null);
        if (declaring is not null)
        {
            this.Handed(declaring, null);
        }

        foreach (var bound in bounds)
        {
            this.AddErasedSignature(environment, bound);
            this.Handed(bound.Signature, bound.Signature);
        }

        for (var i = 0; i < this.retentionCallables.Count; i++)
        {
            this.AddErasedSignature(environment, new(this.retentionCallables[i], FunctionTypeBinder(this.retentionCallables[i])));
        }
    }

    // The callables a contract's Type hands its body (TyCtxt.HandedCallables) into the computation's erased callables, each once; a Callable
    // bound (`self`), whose own relations RetentionPremises adds, only for the callables its signature names.
    private void Handed(BoundType type, BoundType? self)
    {
        foreach (var callable in this.types.HandedCallables(type))
        {
            if (!ReferenceEquals(callable, self) && !this.retentionCallables.Contains(callable))
            {
                this.retentionCallables.Add(callable);
            }
        }
    }

    // The Types a contract's Type reaches, each component at any depth (a projection's base among them), into retentionReached.
    private void Reach(BoundType type)
    {
        if (this.retentionReached.Add(type))
        {
            foreach (var component in type.Components)
            {
                this.Reach(component);
            }
        }
    }

    // An erased signature's own atoms, walked after the contract's in its own scope (retentionErasedScope, the contract's restored after
    // it, which the storage walk reads), its relations added, then dropped with its raw inputs, which are no inputs of the contract.
    private void AddErasedSignature(OriginPremiseEnvironment environment, ErasedSignature erased)
    {
        var (atoms, signature) = (this.retentionAtoms, erased.Signature);
        var (start, raws) = (atoms.Count, this.retentionRawInputs.Count);
        var inputs = signature.Components[0];
        var (contract, signatureInputs) = (this.retentionScope, this.retentionInputs);
        (this.retentionScope, this.retentionInputs) = (this.retentionErasedScope, 0);
        this.retentionScope.Clear();
        if (!ReferenceEquals(inputs, BoundType.Unit))
        {
            this.retentionScope.AddRange(inputs.Components);
        }

        this.retentionRawVisits.Clear();

        for (var i = 0; !ReferenceEquals(inputs, BoundType.Unit) && i < inputs.Components.Count; i++)
        {
            this.RetentionInput(inputs.Components[i], i);
        }

        var result = new RetentionAtom(CallRetention.ResultOutput, RetentionKind.Value, Writable: false, null, null, null);
        this.RetentionWalk(signature.Components[1], result, OriginVariance.Covariant, callable: false);
        this.AddErasedRelations(environment, start, erased.Binder);
        atoms.RemoveRange(start, atoms.Count - start);
        this.retentionRawInputs.RemoveRange(raws, this.retentionRawInputs.Count - raws);
        (this.retentionScope, this.retentionInputs) = (contract, signatureInputs);
    }

    // An erased callable's relations (owner decision 2026-10-17, DECISIONS R4-M52): its implementation was compared where it was
    // converted, under premises no view reads, so no absence of a relation among its fixed Origins is proven (SPEC 15.6.4): each fixed
    // Origin of an input, among the atoms from `start`, outlives each fixed Origin of an output, its result's or one its inputs let it
    // write, and, where it writes an abstract part (a writer: `absorb(self: uniq/Self, r: E)`), that carrier's ReachedNode, the storage its
    // implementation may write through the part's exclusive layers; a result it returns replaces a part and reaches none (DECISIONS
    // R4-M154). Its per-call Origins (IsFixed, its Binder's) keep only its own premises.
    private void AddErasedRelations(OriginPremiseEnvironment environment, int start, Koto? binder)
    {
        var atoms = this.retentionAtoms;
        for (var i = start; i < atoms.Count; i++)
        {
            if (atoms[i].Input < 0 || !IsFixed(atoms[i], binder))
            {
                continue;
            }

            var longer = this.AtomNode(environment, atoms[i]);
            for (var j = start; j < atoms.Count; j++)
            {
                if ((atoms[j].Input == CallRetention.ResultOutput || atoms[j].Writable) && IsFixed(atoms[j], binder))
                {
                    AddPremiseEdge(environment, longer, this.AtomNode(environment, atoms[j]), OriginPremiseRule.Input);
                }

                if (atoms[j] is { Input: >= 0, Writable: true, Carrier: { } carrier } && IsFixed(atoms[j], binder))
                {
                    AddPremiseEdge(environment, longer, this.ReachedNode(environment, carrier), OriginPremiseRule.Input);
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

    // A carrier's ReachedNode (retentionReachedNodes), one per carrier for the computation.
    private int ReachedNode(OriginPremiseEnvironment environment, BoundType carrier)
    {
        if (!this.retentionReachedNodes.TryGetValue(carrier, out var node))
        {
            node = environment.Closure.Node();
            this.retentionReachedNodes.Add(carrier, node);
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

    // An erased signature (the head's contract): the instantiated signature Type and the Binder of its per-call Origins.
    internal readonly record struct ErasedSignature(BoundType Signature, Koto? Binder);
}
