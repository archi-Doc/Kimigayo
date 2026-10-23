// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The computation behind TyCtxt.Retains, under its contract (TyCtxt.Retention.cs): a contract's Origin atoms and carriers, the premises
// its callee's body is checked under in one premise environment (its instantiated own part, then the premise catalog's walk outward
// from its binder's parent), the relations of its erased callables, and the rows (Binding.Retention.Rows.cs).
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
    // in a function's own signature); the (Origin, input) pairs whose content that walk has added, so each is walked once; whether a member
    // walk was incomplete (CallRetention.Limit); and whether its result has a raw layer without an Origin (SPEC 5.7 Calls, D6).
    private readonly List<BoundType> retentionScope = new();
    private readonly HashSet<(BoundOrigin Origin, int Input)> retentionRawVisits = new();
    private int retentionInputs;
    private bool retentionLimit;
    private bool retentionUnstatedResult;

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

    // A borrow of storage a value of the Type refers to, with its outer Origin and referent: a ref, uniq, objref or objuniq layer; a
    // pair layer whose binder admits only those, a borrow in each admitted case, its slot the outer Origin (SPEC 8.1.2) and exclusive when
    // an exclusive borrow is admitted (DECISIONS BU-3); a Slice, a shared borrow of its elements (SPEC 4.8.1); a UniqSlice, an exclusive
    // one, its `source` the outer Origin (SPEC 4.8.2).
    private BoundType? BorrowReferent(BoundType type, out BoundOrigin? outer, out bool exclusive)
    {
        (outer, exclusive) = (type.Origin, IsExclusive(type.Semantics));
        if (type is { Kind: BoundTypeKind.Semantics, Components: [var referent] } && IsBorrow(type.Semantics))
        {
            return referent;
        }

        if (this.LayerAdmitted(type) is not SemanticsMask.None and var admitted && (admitted & ~SemanticsMask.Borrow) == 0 &&
            TryPairLayer(type, out _, out var target))
        {
            (outer, exclusive) = (this.OuterOrigin(type), (admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0);
            return target;
        }

        if (type is { Kind: BoundTypeKind.Slice, Components: [var elements] })
        {
            return elements;
        }

        if (type is { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice, Components: [var exclusiveElements], OriginArguments: [var source] })
        {
            (outer, exclusive) = (source, true);
            return exclusiveElements;
        }

        return null;
    }

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
        this.retentionScope.Clear();
        this.retentionScope.AddRange(inputs);
        this.retentionInputs = callee is null ? 0 : inputs.Length;
        if (declaring is not null)
        {
            this.retentionScope.Add(declaring);
        }

        this.retentionRawVisits.Clear();
        this.retentionLimit = false;
        this.retentionUnstatedResult = false;
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
            if (this.retentionLimit)
            {
                return CallRetention.Limit;
            }

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

    // An input's atoms: a borrowed input (BorrowReferent) gives Value through its outer Origin and Content through its referent (SPEC
    // 15.6.4 step 4), which an exclusive one lets the call write, its referent invariant there (SPEC 15.3.5), and a shared one's lies
    // past a shared layer (SPEC 5.7, 8.11.4 rule 1).
    private void RetentionInput(BoundType input, int index)
    {
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent)
        {
            this.AddRetentionAtom(new(index, RetentionKind.Value, Writable: false, outer, null, null), callable: false);
            var content = new RetentionAtom(index, RetentionKind.Content, exclusive, null, null, outer, Shared: !exclusive);
            this.RetentionWalk(referent, content, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant, callable: false);
        }
        else
        {
            this.RetentionWalk(input, new(index, RetentionKind.Value, Writable: true, null, null, null), OriginVariance.Covariant, callable: false);
        }
    }

    // The atoms and carriers of a Type at a position of `variance` within its input (OwnedOrigins and the outer Origin, SPEC 15.2.3): a pair
    // layer's slot and target; a Type parameter or a pair's target as a carrier; the Origin, Origin arguments and components of any other,
    // so a projection or a dependent Semantics term is its parts and joins its base. An occurrence is writable storage of its input when
    // the walk has reached an invariant position through storage the call may write (`at.Writable`: an exclusive referent, the pointee
    // of a raw layer without an Origin or with `static`, or a value the call may write, whose nominal Type may hide an exclusive layer)
    // at or above it: a value stored there brings its whole Type, so every occurrence below that position is in it, past any layer
    // (SPEC 15.6.4, DECISIONS BU-1). Before such a position, the walk reaches no writable storage past a shared layer or a raw layer
    // the callee's own signature makes Shared (`at.Shared`; SignatureStep: only shared input borrows state its Origin there, so a pointer
    // read from it has read authority only, SPEC 5.2.1 condition 3, 8.11.4 rule 1; D10, superseding R4-M51's raw clause), nor past a
    // pair layer whose binder admits no exclusive Semantics. A raw layer with another Origin addresses what each borrow its Origin
    // belongs to admits (RawContent), and its atom is that Origin, as a borrow's; a raw result layer without one reaches every input (D6).
    // A callable Type stores nothing, and its per-call Origins belong to no value of it (SPEC 15.2.3).
    private void RetentionWalk(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        var here = at with { Writable = at.Writable && variance == OriginVariance.Invariant };
        if (TryPairLayer(type, out _, out var target))
        {
            var exclusive = IsInvariantLayer(type, this);
            var slot = this.OuterOrigin(type);
            this.AddRetentionAtom(here with { Origin = slot }, callable);
            var inner = ComposeVariance(variance, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant);
            this.RetentionWalk(target, at with { Writable = here.Writable || (at.Writable && exclusive), Layer = slot ?? at.Layer }, inner, callable);
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

        this.AddRetentionAtom(here with { Origin = OwnedOrigin(type) }, callable);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var position = ComposeVariance(variance, type.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant);
            this.AddRetentionAtom(at with { Origin = type.OriginArguments[i], Writable = at.Writable && position == OriginVariance.Invariant }, callable);
        }

        var layer = this.BorrowReferent(type, out var outer, out _) is not null ? outer ?? at.Layer : at.Layer;
        var inside = callable || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem;
        var shared = type.Kind == BoundTypeKind.Slice ||
            (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc);
        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        var rawOrigin = type.RawOrigin;
        var step = raw && !inside ? this.SignatureStep(rawOrigin, CollectionsMarshal.AsSpan(this.retentionScope)[..this.retentionInputs]) : RawStep.Unstated;
        var past = at.Shared || shared || step == RawStep.Shared;
        var through = raw ? !past && step is RawStep.Unstated or RawStep.Static : at.Writable && !shared;
        var below = at with { Layer = layer, Writable = !inside && (through || here.Writable), Shared = past };
        if (raw && !inside && !past && step == RawStep.Exclusive && at.Input >= 0)
        {
            this.RawContent(rawOrigin!, at.Input);
        }

        this.retentionUnstatedResult |= raw && !inside && rawOrigin is null && at.Input == Retention.Result;
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.RetentionWalk(type.Components[i], below, ComposeVariance(variance, this.ComponentVariance(type, i)), inside);
        }
    }

    // SPEC 5.7 Calls (D15; design-final §6.2): a raw parameter layer whose Origin steps Exclusive lets the call write what each borrow the
    // Origin belongs to admits (RawMembers: the referent of each input whose outer Origin it is, the keepers of each slot it binds in a
    // nominal Type of the contract, substituted by that Type), walked as input `index`'s writable content. Each (Origin, input) is walked
    // once per contract or erased signature, which loses nothing: its members and its content atom are the same each time. An incomplete
    // member walk makes the contract CallRetention.Limit.
    private void RawContent(BoundOrigin origin, int index)
    {
        if (!this.retentionRawVisits.Add((origin, index)))
        {
            return;
        }

        var content = new RetentionAtom(index, RetentionKind.Content, Writable: true, null, null, origin);
        var start = this.rawMembers.Count;
        this.retentionLimit |= !this.RawMembers(origin, CollectionsMarshal.AsSpan(this.retentionScope), this.rawMembers);
        var end = this.rawMembers.Count;
        for (var i = start; i < end && !this.retentionLimit; i++)
        {
            this.RetentionWalk(this.rawMembers[i].Referent, content, OriginVariance.Invariant, callable: false);
        }

        this.rawMembers.RemoveRange(start, end - start);
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
        var unstated = this.retentionUnstatedResult;
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
        this.retentionUnstatedResult = unstated;
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

    // An Origin atom or a carrier of an input or of the result (Retention.Result): the kind it gives the input, whether it is writable
    // storage of the input (RetentionWalk), the Origin of the nearest borrow, Slice or pair layer above it, which a carrier's hidden
    // Origins outlive (SPEC 15.6.1), and whether a shared layer or a raw layer whose step is Shared lies above it.
    private readonly record struct RetentionAtom(
        int Input, RetentionKind Kind, bool Writable, BoundOrigin? Origin, BoundType? Carrier, BoundOrigin? Layer, bool Shared = false);
}
