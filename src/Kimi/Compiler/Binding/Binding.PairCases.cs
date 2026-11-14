// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of pair binders (SPEC 8.15): the outer Origin <c>o</c> of a pair's whole Type (SPEC 8.1.1; a raw
/// argument's binding states none, <c>TyCtxt.PairBinding</c>), the complete Type every pair layer takes in one admitted case, the
/// bare-acquisition plan per case (SPEC 8.14) and the binders in scope of a body.</summary>
public sealed partial class Binding
{
    /// <summary>The Semantics in <see cref="SemanticsMask"/> bit order, the enumeration order of Semantics cases (SPEC 8.15).</summary>
    internal static readonly SemanticsKind[] SemanticsOrder =
    [
        SemanticsKind.Owner, SemanticsKind.Ref, SemanticsKind.Uniq, SemanticsKind.Obj, SemanticsKind.Rc, SemanticsKind.Arc, SemanticsKind.ObjRef, SemanticsKind.ObjUniq, SemanticsKind.Raw,
    ];

    // The binders in scope of one PairSlotCondition query, reused, and whether a query is collecting them.
    private readonly List<PairBinder> pairConditionBinders = new();
    private readonly List<PairBinder> copyCaseBinders = new(); // The binders of one CopiesInEveryCase query, reused.
    private bool pairConditionActive;

    /// <summary>Appends the Semantics of a mask in bit order, separated.</summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="mask">The Semantics.</param>
    /// <param name="separator">The separator between two Semantics.</param>
    internal static void AppendSemantics(StringBuilder text, SemanticsMask mask, string separator)
    {
        var first = true;
        for (var i = 0; i < SemanticsOrder.Length; i++)
        {
            if ((mask & SemanticsOrder[i].ToMask()) != 0)
            {
                text.Append(first ? string.Empty : separator).Append(SemanticsOrder[i].ToText());
                first = false;
            }
        }
    }

    /// <summary>Tells whether a bare acquisition of a pair-layer Place Reborrows in some admitted case (SPEC 8.14): the use is then
    /// exclusive in that case, the strongest a Closure's receiver can be over its cases (SPEC 7.7.3).</summary>
    /// <param name="type">The stored Type of the Place.</param>
    /// <param name="node">The acquiring syntax, whose scope supplies the premises.</param>
    /// <returns>Whether an admitted case Reborrows.</returns>
    internal bool BareReborrows(BoundType type, Koto node)
    {
        this.BareAcquisition(type, node, out var reborrow);
        return reborrow != SemanticsMask.None;
    }

    /// <summary>Tells whether a pair layer is invariant in its target (SPEC 15.3.5): its binder admits <c>uniq</c>, <c>obj</c>,
    /// <c>objuniq</c> or <c>raw</c>, so a relation between its targets must hold as an equality in a generic body; the admitted set
    /// is the binder's own, from its declaring scope.</summary>
    /// <param name="type">A Type.</param>
    /// <returns>Whether <paramref name="type"/> is a pair layer with an invariant admitted Semantics.</returns>
    internal bool InvariantAdmitted(BoundType type)
    {
        const SemanticsMask invariant = SemanticsMask.Uniq | SemanticsMask.Obj | SemanticsMask.ObjUniq | SemanticsMask.Raw;
        if (type.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            // Each child is a complete case Type and enforces its own layer variance.
            return false;
        }

        return (this.LayerAdmitted(type) & invariant) != 0;
    }

    /// <summary>Gets the admitted set of a pair layer's binder (SPEC 8.3), from its declaring scope, which every occurrence of the layer
    /// shares; None for another Type.</summary>
    /// <param name="type">A Type.</param>
    /// <returns>The admitted Semantics.</returns>
    internal SemanticsMask LayerAdmitted(BoundType type)
        => TryPairLayer(type, out var whole, out _) && whole.Symbol is { Scope: { } scope } ? this.AdmittedSemantics(whole, scope) : SemanticsMask.None;

    /// <summary>Gets the outer Origin <c>o</c> of a pair's whole Type (SPEC 8.1.1): one fixed Origin per pair binder, shared by every
    /// occurrence of the original <c>s/T</c>, present only in the admitted borrow cases and never written in source. The whole Type
    /// itself keeps no Origin; this atom is the binder's and is obtained only through <see cref="OuterOrigin"/>.</summary>
    /// <param name="target">The pair's SemanticsTarget symbol.</param>
    /// <returns>The interned atom, stable across rebinds.</returns>
    internal BoundOrigin PairOuterOrigin(BindingSymbol target)
    {
        // A negative slot keeps the atom apart from the declared Origins of the same binder (slots 0 and up, Binding.Origins);
        // the closure environment slots of Binding.ArgumentOperations are Projection atoms and never meet a Parameter atom.
        var key = (target.Scope.Owner, OriginKind.Parameter, -1 - target.Slot);
        if (!this.originAtoms.TryGetValue(key, out var origin))
        {
            this.originAtoms.Add(key, origin = new(OriginKind.Parameter, key.Owner, key.Item3, target.Pair?.Name ?? target.Name) { Occurrence = target.Declaration });
        }

        return origin;
    }

    /// <summary>Gets the outer-Origin slot of a pair occurrence (SPEC 8.1.2): its annotation, the own slot of an application
    /// <c>s/U</c> (which never copies <c>W</c>'s outer Origin and may have none), or, for the original <c>s/T</c>, <c>W</c>'s own
    /// outer Origin <c>o</c>.</summary>
    /// <param name="occurrence">A pair layer.</param>
    /// <returns>The slot, or null for an application without one.</returns>
    internal BoundOrigin? OuterOrigin(BoundType occurrence)
        => occurrence.Origin ?? (occurrence.Kind == BoundTypeKind.Parameter && occurrence.Symbol is { Kind: BindingSymbolKind.SemanticsTarget } target ? this.PairOuterOrigin(target) : null);

    /// <summary>Gets the Semantics-case condition under which a pair layer's outer-Origin slot exists at a use (SPEC 8.1.2, 15.6.5):
    /// no condition when every admitted case of its binder is a borrow, the binder's bit in the order of
    /// <see cref="PairBinders(BindingScope, List{PairBinder})"/> in the use's scope when its admitted set mixes borrows with other
    /// Semantics, and null when no admitted case is a borrow, so the slot never exists. A binder that is not in scope, or beyond the 64th, is <see cref="ulong.MaxValue"/>: a premise under it is never
    /// usable, and a relation required under it is judged unconditionally.</summary>
    /// <param name="layer">A Type; only a pair layer has a condition.</param>
    /// <param name="scope">The constraint scope of the use, whose binders give the bits.</param>
    /// <returns>The condition, or null when the slot never exists.</returns>
    internal ulong? PairSlotCondition(BoundType layer, BindingScope scope)
    {
        if (!TryPairLayer(layer, out var whole, out _))
        {
            return 0;
        }

        var admitted = this.AdmittedSemantics(whole, scope);
        if ((admitted & SemanticsMask.Borrow) == 0)
        {
            return null;
        }

        if ((admitted & ~SemanticsMask.Borrow) == 0)
        {
            return 0;
        }

        // A query nested in the collection of the binders, such as a proof of an admitted set, takes its own list.
        var binders = this.pairConditionBinders.Count == 0 && !this.pairConditionActive ? this.pairConditionBinders : new List<PairBinder>();
        var outer = this.pairConditionActive;
        this.pairConditionActive = true;
        try
        {
            this.PairBinders(scope, binders);
            for (var i = 0; i < binders.Count && i < 64; i++)
            {
                if (ReferenceEquals(binders[i].Target.WholeType, whole))
                {
                    return 1UL << i;
                }
            }

            return ulong.MaxValue;
        }
        finally
        {
            binders.Clear();
            this.pairConditionActive = outer;
        }
    }

    /// <summary>Gets the outer Origin a Type stores at a use and the Semantics case it is stored in (SPEC 8.1.2, 15.6.5): a pair layer's
    /// slot, the implicit <c>o</c> of an original <c>s/T</c> included, in its binder's borrow cases (<see cref="PairSlotCondition"/>), and
    /// any other Type's written Origin in every case.</summary>
    /// <param name="type">A Type, such as a pattern's layer or its substitution at a use.</param>
    /// <param name="scope">The constraint scope of the use, whose binders give the condition's bits.</param>
    /// <param name="condition">The Semantics case of the storage, 0 for every case.</param>
    /// <returns>The stored outer Origin, or null when no case stores one.</returns>
    internal BoundOrigin? StoredSlot(BoundType type, BindingScope scope, out ulong condition)
    {
        condition = 0;
        if (!type.ContainsPairLayer)
        {
            return type.Origin;
        }

        if (this.PairSlotCondition(type, scope) is not { } slot)
        {
            return null;
        }

        condition = slot;
        return this.OuterOrigin(type);
    }

    /// <summary>Gets the layer a Type forms for well-formedness in a use's scope (SPEC 15.6.1, 8.1.2, 15.6.5), whose target's stored Origins
    /// outlive its outer Origin: a safe borrow or a Slice in every case, and a pair layer within its stored slot (<see cref="StoredSlot"/>)
    /// in its binder's borrow cases. The condition is the raw <see cref="PairSlotCondition"/>: each consumer applies its own contract to
    /// <see cref="ulong.MaxValue"/>.</summary>
    /// <param name="type">A Type.</param>
    /// <param name="scope">The constraint scope of the use, whose binders give the condition's bits.</param>
    /// <returns>The layer's target, its outer Origin and the Semantics case it exists in, or null when no case forms a layer.</returns>
    internal (BoundType Target, BoundOrigin Outer, ulong Condition)? WellFormedLayer(BoundType type, BindingScope scope)
    {
        BoundType target;
        if (IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice)
        {
            if (type.Components.Count == 0)
            {
                return null;
            }

            target = type.Components[0];
        }
        else if (!TryPairLayer(type, out _, out target))
        {
            return null;
        }

        return this.StoredSlot(type, scope, out var condition) is { } outer ? (target, outer, condition) : null;
    }

    /// <summary>Forms the complete Type a Type takes in one Semantics case (SPEC 8.15): every pair layer of a binder in
    /// <paramref name="cases"/> becomes the Type of that binder's Semantics over its direct target with the layer's outer-Origin
    /// slot (<c>U</c> for owner, <c>s/U during o</c> for a borrow, <c>s/U</c> otherwise), other Types map their components, and
    /// a Type that mentions no such layer is returned as is.</summary>
    /// <param name="type">The declared Type.</param>
    /// <param name="cases">The Semantics of each resolved binder in scope.</param>
    /// <returns>The interned case Type.</returns>
    internal BoundType CaseType(BoundType type, ReadOnlySpan<PairCase> cases)
    {
        if (!type.ContainsPairLayer)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            return type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget, Type: { } projection } target && CaseOf(cases, target) is { } semantics
                ? this.Form(semantics, projection, this.OuterOrigin(type)) : type;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation && CaseOf(cases, type.Symbol!) is { } selected)
        {
            return FamilyCase(type, selected) is { } child ? this.CaseType(child, cases) : type;
        }

        // DECISIONS BU-60: the Type instance a closure's context holds takes the cases with the Closure Type.
        var context = TyCtxt.HeldType(type) is { } held && this.CaseType(held, cases) is var image && !ReferenceEquals(image, held)
            ? this.types.HeldContext(type.Symbol!, image) : type.ClosureContext;
        if (type.Components.Count == 0)
        {
            return this.WithContext(type, context);
        }

        var scratch = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                scratch[i] = this.CaseType(type.Components[i], cases);
                changed |= !ReferenceEquals(scratch[i], type.Components[i]);
            }

            if (type.Kind == BoundTypeKind.SemanticsApplication && type.Symbol is { } selector && CaseOf(cases, selector) is { } applied)
            {
                return this.Form(applied, scratch[0], type.Origin);
            }

            return this.WithContext(
                changed ? this.types.Rebuild(type, scratch.AsSpan(0, type.Components.Count), type.Origin, (BoundOrigin[])type.OriginArguments) : type, context);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    // SPEC 3.5, 3.5.1, 8.14, 15.1.5: the bare acquisition of a Place storing the Type, taken by value without an operator: a Copy-proven
    // Type Copies, a pair layer whose every admitted case Copies or Reborrows acquires by case, a stored exclusive reference whose path
    // grants exclusive access Reborrows in its own mode, and any other Place, one reached through a shared layer too, needs an explicit
    // operation (the Move row: TransferRequired); the proof tells an unproven Copy from a refuted one.
    internal HirAcquire BareAcquire(BoundType type, Koto node, out ConstraintProof proof) => this.BareAcquire(type, node, [], out proof, out _);

    /// <summary>Plans the bare acquisition of a pair-layer Place per admitted case (SPEC 3.5, 8.14, 8.15.2: each case acquires its
    /// complete Type, <see cref="CaseType"/>, by BareAcquire's rows, the binder's own layers on the path taking the case, SPEC 3.5.1),
    /// for every pair layer (SPEC 13.5.5: the original <c>s/T</c>, an application <c>s/U</c>, an adaptation family <c>p@s</c>): a shared or
    /// raw reference Copies, an exclusive reference whose path grants exclusive access Reborrows, the owner case is the target's own plan
    /// (a nested pair layer acquires by its own cases), and any other case has no plan, a definition error naming that case.</summary>
    /// <param name="type">The stored Type of the Place, a pair layer.</param>
    /// <param name="node">The acquiring syntax, whose scope supplies the premises.</param>
    /// <param name="reborrow">The cases that Reborrow, the owner case where its target Reborrows in some case of its own.</param>
    /// <returns>The admitted cases that neither Copy nor Reborrow; None when every case has a plan.</returns>
    internal SemanticsMask BareAcquisition(BoundType type, Koto node, out SemanticsMask reborrow) => this.BareAcquisition(type, node, [], out reborrow);

    /// <summary>Tells whether a Type Copies in every Semantics case of the pair binders in scope (SPEC 8.14, 8.15.2, 3.5): its
    /// complete Type in each case (<see cref="CaseType"/>) is proven Copy by SPEC 8.3, which no case adds a premise to. The 8.3 proof
    /// decides alone where it is not Unknown or the Type holds no pair layer, and where the cases exceed
    /// <see cref="PairBinder.CaseBound"/> (64 cases are analyzed; 65 are not), whose definition records <c>OwnershipCaseLimit_Kd</c>
    /// (<c>CheckCaseBound</c>), so no case is answered unchecked. The one Copy evidence of an
    /// acquisition: BareAcquire, a Consuming call, <c>@copy</c>, a same-Type acquisition, a bare-Place adaptation or object view, a
    /// Pattern binding, a special receiver's part, an omitted capture list (Transfers' S3), and <c>TyCtxt.CopyFacts</c>, which MIR reads
    /// (DECISIONS BU-88).</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="scope">The scope whose premises and pair binders apply.</param>
    /// <returns>Whether every case Copies.</returns>
    internal bool CopiesInEveryCase(BoundType type, BindingScope scope)
    {
        var proof = this.ProveCopy(type, scope);
        if (proof != ConstraintProof.Unknown || !type.ContainsPairLayer)
        {
            return proof == ConstraintProof.Proven;
        }

        // A query nested in a case's proof takes its own list, as PairSlotCondition's does.
        var binders = this.copyCaseBinders.Count == 0 ? this.copyCaseBinders : new List<PairBinder>();
        this.PairBinders(scope, binders);
        var resolved = 0;
        var count = 1L;
        for (var b = 0; b < binders.Count; b++)
        {
            if (binders[b].Resolved)
            {
                count *= BitOperations.PopCount((uint)binders[b].Admitted);
                resolved++;
            }
        }

        if (resolved == 0 || count > PairBinder.CaseBound)
        {
            binders.Clear();
            return false;
        }

        Span<SemanticsKind> digits = stackalloc SemanticsKind[resolved];
        var scratch = this.caseScratch.Rent(resolved);
        try
        {
            var assignment = scratch.AsSpan(0, resolved);
            for (var index = 0; index < count; index++)
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

                if (this.ProveCopy(this.CaseType(type, assignment), scope) != ConstraintProof.Proven)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            this.caseScratch.Return(scratch, clearArray: true);
            binders.Clear();
        }
    }

    // PairBinder.Collect.
    internal void PairBinders(Koto entry, List<PairBinder> binders) => this.PairBinders(this.ConstraintScope(entry), binders);

    // The pair binders in scope of a constraint scope, outer to inner in slot order, each with its admitted set (PairBinder.Collect).
    internal void PairBinders(BindingScope scope, List<PairBinder> binders)
    {
        binders.Clear();
        for (var current = scope; current is not null; current = current.Parent)
        {
            // Each scope's binders go to the front, in slot order, so outer scopes precede inner ones.
            var count = 0;
            foreach (var symbol in current.Types.Values)
            {
                if (symbol.Kind != BindingSymbolKind.SemanticsTarget || symbol.WholeType is not { } whole)
                {
                    continue;
                }

                var at = 0;
                while (at < count && binders[at].Target.Slot < symbol.Slot)
                {
                    at++;
                }

                binders.Insert(at, new(symbol, this.AdmittedSemantics(whole, scope)));
                count++;
            }
        }
    }

    // The condition of a relation required where a slot exists: a binder without a bit is judged in every case.
    private static ulong RequiredCondition(ulong? condition) => condition is { } value && value != ulong.MaxValue ? value : 0;

    // The Semantics a case gives one binder, or null when the binder is not resolved in it.
    private static SemanticsKind? CaseOf(ReadOnlySpan<PairCase> cases, BindingSymbol target)
    {
        for (var i = 0; i < cases.Length; i++)
        {
            if (ReferenceEquals(cases[i].Target, target))
            {
                return cases[i].Semantics;
            }
        }

        return null;
    }

    // SPEC 8.1.1 table: the complete Type of one case over the direct target; only a safe borrow keeps the outer-Origin slot (so the raw
    // case states none, as a raw argument's binding does, TyCtxt.PairBinding).
    private BoundType Form(SemanticsKind semantics, BoundType target, BoundOrigin? origin)
        => semantics == SemanticsKind.Owner ? target : this.types.Intern(BoundTypeKind.Semantics, null, semantics, [target], origin: IsBorrow(semantics) ? origin : null);

    /// <summary>Binds the outer Origin <c>o</c> of one of <paramref name="binder"/>'s pairs through its Type argument (SPEC 8.1.1,
    /// 8.1.2): the outer Origin of a borrow binding, nothing for a value binding (static, the identity of a meet, which no exclusive
    /// slot accepts), and the outer-Origin slot of an abstract binding.</summary>
    /// <param name="origin">An Origin atom.</param>
    /// <param name="binder">The declaration whose Type arguments are given.</param>
    /// <param name="arguments">The Type arguments by slot.</param>
    /// <returns>The image, or null when <paramref name="origin"/> is no bound <c>o</c> of the binder.</returns>
    private BoundOrigin? PairOuterImage(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundType?> arguments)
        => origin is { Kind: OriginKind.Parameter, Slot: < 0, Occurrence: GenericParameterKoto declaration } && ReferenceEquals(origin.Binder, binder) &&
            declaration.BoundSymbol is { } target && ContainerSlot(binder, target) is >= 0 and var slot && slot < arguments.Length && arguments[slot] is { } bound
            ? IsBorrow(bound.Semantics) ? bound.Origin ?? BoundOrigin.Static : this.OuterOrigin(bound) ?? BoundOrigin.Static
            : null;

    // BareAcquire's rows under a case assignment, the cases of the enclosing pair layers whose owner case stores this Type (SPEC 8.15.2).
    private HirAcquire BareAcquire(BoundType type, Koto node, ReadOnlySpan<PairCase> assignment, out ConstraintProof proof, out SemanticsMask reborrow)
    {
        reborrow = SemanticsMask.None;
        proof = this.ProveCopy(type, node);
        if (proof == ConstraintProof.Proven)
        {
            return HirAcquire.Copy;
        }

        if (TryAdaptationSelector(type, out _))
        {
            return this.BareAcquisition(type, node, assignment, out reborrow) == SemanticsMask.None ? HirAcquire.ByCase : HirAcquire.Move;
        }

        // SPEC 8.14, 8.15.2: a Type that holds a pair layer, not itself one, has one plan in every case, as no case Type of it is a
        // `uniq` reference: it Copies where every case does.
        if (proof == ConstraintProof.Unknown && this.CopiesInEveryCase(type, this.ConstraintScope(node)))
        {
            return HirAcquire.Copy;
        }

        return type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } &&
                PathAuthority(node, assignment) != SemanticsKind.Ref ? HirAcquire.Reborrow : HirAcquire.Move;
    }

    // BareAcquisition under a case assignment (BareAcquire's): every pair layer, an adaptation family included (TryAdaptationSelector),
    // takes in each admitted case its complete Type (CaseType), which BareAcquire's rows plan with the path read under the assignment
    // extended by that case (SPEC 3.5.1, 8.15.2), in nested scratch (caseScratch), so a nested level allocates nothing warm.
    private SemanticsMask BareAcquisition(BoundType type, Koto node, ReadOnlySpan<PairCase> assignment, out SemanticsMask reborrow)
    {
        reborrow = SemanticsMask.None;
        if (!TryAdaptationSelector(type, out var whole))
        {
            return SemanticsMask.None;
        }

        var admitted = this.AdmittedSemantics(whole, this.ConstraintScope(node));
        var copy = SemanticsMask.None;
        var scratch = this.caseScratch.Rent(assignment.Length + 1);
        try
        {
            var cases = scratch.AsSpan(0, assignment.Length + 1);
            assignment.CopyTo(cases);
            for (var i = 0; i < SemanticsOrder.Length; i++)
            {
                var mode = SemanticsOrder[i].ToMask();
                if ((admitted & mode) == 0)
                {
                    continue;
                }

                cases[^1] = new(whole.Symbol!, SemanticsOrder[i]);
                var plan = this.BareAcquire(this.CaseType(type, cases), node, cases, out _, out var inner);
                var reborrows = plan == HirAcquire.Reborrow || (plan == HirAcquire.ByCase && inner != SemanticsMask.None);
                reborrow |= reborrows ? mode : SemanticsMask.None;
                copy |= !reborrows && plan is HirAcquire.Copy or HirAcquire.ByCase ? mode : SemanticsMask.None;
            }

            return admitted & ~(reborrow | copy);
        }
        finally
        {
            this.caseScratch.Return(scratch, clearArray: true);
        }
    }
}
