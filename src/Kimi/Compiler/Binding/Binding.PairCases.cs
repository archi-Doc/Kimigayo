// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of pair binders (SPEC 8.10): the outer Origin <c>o</c> of a pair's whole Type (SPEC 8.1.1), the
/// complete Type every pair layer takes in one admitted case, the bare-acquisition plan per case (SPEC 8.9) and the binders in
/// scope of a body.</summary>
public sealed partial class Binding
{
    /// <summary>Tells whether a pair layer is invariant in its target (SPEC 15.3.5): its binder admits <c>uniq</c>, <c>obj</c>,
    /// <c>objuniq</c> or <c>raw</c>, so a relation between its targets must hold as an equality in a generic body; the admitted set
    /// is the binder's own, from its declaring scope.</summary>
    /// <param name="type">A Type.</param>
    /// <returns>Whether <paramref name="type"/> is a pair layer with an invariant admitted Semantics.</returns>
    internal bool InvariantAdmitted(BoundType type)
        => TryPairLayer(type, out var whole, out _) && whole.Symbol is { Scope: { } scope } &&
            (this.AdmittedSemantics(whole, scope) & (SemanticsMask.Uniq | SemanticsMask.Obj | SemanticsMask.ObjUniq | SemanticsMask.Raw)) != 0;

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

    /// <summary>Forms the complete Type a Type takes in one Semantics case (SPEC 8.10): every pair layer of a binder in
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

        if (type.Components.Count == 0)
        {
            return type;
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

            return changed
                ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, type.ClosureContext)
                : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    /// <summary>Plans the bare acquisition of a pair-layer Place per admitted case (SPEC 3.5, 8.9): a Copy for a shared or raw
    /// reference and for an owner whose target is proven Copy, a Reborrow for an exclusive reference, and otherwise a case
    /// without a plan, which is a definition error naming that case.</summary>
    /// <param name="type">The stored Type of the Place, a pair layer.</param>
    /// <param name="node">The acquiring syntax, whose scope supplies the premises.</param>
    /// <param name="reborrow">The cases that Reborrow.</param>
    /// <returns>The admitted cases that neither Copy nor Reborrow; None when every case has a plan.</returns>
    internal SemanticsMask BareAcquisition(BoundType type, Koto node, out SemanticsMask reborrow)
    {
        reborrow = SemanticsMask.None;
        if (!TryPairLayer(type, out var whole, out var target))
        {
            return SemanticsMask.None;
        }

        var admitted = this.AdmittedSemantics(whole, this.ConstraintScope(node));
        reborrow = admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq);
        var copy = admitted & (SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Raw);
        if ((admitted & SemanticsMask.Owner) != 0 && this.ProveCopy(target, node) == ConstraintProof.Proven)
        {
            copy |= SemanticsMask.Owner;
        }

        return admitted & ~(reborrow | copy);
    }

    /// <summary>Collects the pair binders in scope of a body (SPEC 8.10): those of its own declaration, of its containers and, for
    /// a Closure, an accessor or a static initializer, of the enclosing declarations, outer to inner in slot order, each with its
    /// admitted set.</summary>
    /// <param name="entry">The body's declaration.</param>
    /// <param name="binders">The caller-owned list to fill; it is cleared first.</param>
    internal void PairBinders(Koto entry, List<PairBinder> binders)
    {
        binders.Clear();
        var scope = this.ConstraintScope(entry);
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

    // SPEC 8.1.1 table: the complete Type of one case over the direct target; only a safe borrow keeps the outer-Origin slot.
    private BoundType Form(SemanticsKind semantics, BoundType target, BoundOrigin? origin)
        => semantics == SemanticsKind.Owner ? target : this.InternType(BoundTypeKind.Semantics, null, semantics, [target], origin: IsBorrow(semantics) ? origin : null);
}
