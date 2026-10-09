// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Semantics cases of pair binders (SPEC 8.10): the outer Origin <c>o</c> of a pair's whole Type (SPEC 8.1.1), the
/// complete Type every pair layer takes in one admitted case, the bare-acquisition plan per case (SPEC 8.9) and the binders in
/// scope of a body.</summary>
public sealed partial class Binding
{
    /// <summary>The Semantics in <see cref="SemanticsMask"/> bit order, the enumeration order of Semantics cases (SPEC 8.10).</summary>
    internal static readonly SemanticsKind[] SemanticsOrder =
    [
        SemanticsKind.Owner, SemanticsKind.Ref, SemanticsKind.Uniq, SemanticsKind.Obj, SemanticsKind.Rc, SemanticsKind.Arc, SemanticsKind.ObjRef, SemanticsKind.ObjUniq, SemanticsKind.Raw,
    ];

    // The binders in scope of one PairSlotCondition query, reused, and whether a query is collecting them.
    private readonly List<PairBinder> pairConditionBinders = new();
    private bool pairConditionActive;

    /// <summary>Counts the resolved cases of one body without overflowing the resource counter.</summary>
    /// <param name="binders">The body's pair binders and their admitted sets.</param>
    /// <returns>The product, saturated at the largest representable count.</returns>
    internal static long SemanticsCaseProduct(IReadOnlyList<PairBinder> binders)
    {
        var product = 1L;
        for (var i = 0; i < binders.Count; i++)
        {
            if (binders[i].Resolved)
            {
                var count = BitOperations.PopCount((uint)binders[i].Admitted);
                product = product > long.MaxValue / count ? long.MaxValue : product * count;
            }
        }

        return product;
    }

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

    /// <summary>Tells whether a bare acquisition of a pair-layer Place Reborrows in some admitted case (SPEC 8.9): the use is then
    /// exclusive in that case, the strongest a Closure's receiver can be over its cases (SPEC 7.6.3).</summary>
    /// <param name="type">The stored Type of the Place.</param>
    /// <param name="node">The acquiring syntax, whose scope supplies the premises.</param>
    /// <returns>Whether an admitted case Reborrows.</returns>
    internal bool BareReborrows(BoundType type, Koto node)
    {
        if (!TryPairLayer(type, out _, out _))
        {
            return false;
        }

        this.BareAcquisition(type, node, out var reborrow);
        return reborrow != SemanticsMask.None;
    }

    /// <summary>Formats the Semantics cases in which a bare acquisition of a pair layer neither Copies nor Reborrows, for a Note
    /// (SPEC 8.9, 23.3.6.4); empty for another Type or when every case has a plan.</summary>
    /// <param name="type">The stored Type.</param>
    /// <param name="node">The acquiring syntax.</param>
    /// <returns>The Note's tail, such as <c> in the Semantics case s = owner (SPEC 8.9)</c>.</returns>
    internal string FailingCaseNote(BoundType type, Koto node)
    {
        if (!TryPairLayer(type, out var whole, out _) || (this.BareAcquisition(type, node, out _) is var failing && failing == SemanticsMask.None))
        {
            return string.Empty;
        }

        var text = new StringBuilder(" in the Semantics case").Append(BitOperations.PopCount((uint)failing) == 1 ? " " : "s ");
        text.Append(whole.Symbol is { } symbol ? symbol.Pair?.Name ?? symbol.Name : "s").Append(" = ");
        AppendSemantics(text, failing, " or ");
        return text.Append(" (SPEC 8.9)").ToString();
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

        return TryPairLayer(type, out var whole, out _) && whole.Symbol is { Scope: { } scope } &&
            (this.AdmittedSemantics(whole, scope) & invariant) != 0;
    }

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
    /// no condition when every admitted case of its binder is a borrow, the binder's bit in the order of <see cref="PairBinders"/> at
    /// the use when its admitted set mixes borrows with other Semantics, and null when no admitted case is a borrow, so the slot
    /// never exists. A binder that is not in scope, or beyond the 64th, is <see cref="ulong.MaxValue"/>: a premise under it is never
    /// usable, and a relation required under it is judged unconditionally.</summary>
    /// <param name="layer">A Type; only a pair layer has a condition.</param>
    /// <param name="use">The use whose binders give the bits.</param>
    /// <returns>The condition, or null when the slot never exists.</returns>
    internal ulong? PairSlotCondition(BoundType layer, Koto use)
    {
        if (!TryPairLayer(layer, out var whole, out _))
        {
            return 0;
        }

        var admitted = this.AdmittedSemantics(whole, this.ConstraintScope(use));
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
            this.PairBinders(use, binders);
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

        if (type.Kind == BoundTypeKind.SemanticsAdaptation && CaseOf(cases, type.Symbol!) is { } selected)
        {
            return FamilyCase(type, selected) is { } child ? this.CaseType(child, cases) : type;
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
                ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, type.ClosureContext, type.LengthArguments, type.ResultMode)
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

    // SPEC 8.1.1 table: the complete Type of one case over the direct target; only a safe borrow keeps the outer-Origin slot.
    private BoundType Form(SemanticsKind semantics, BoundType target, BoundOrigin? origin)
        => semantics == SemanticsKind.Owner ? target : this.InternType(BoundTypeKind.Semantics, null, semantics, [target], origin: IsBorrow(semantics) ? origin : null);

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

    /// <summary>Binds every <c>o</c> of <paramref name="binder"/>'s pairs in an Origin, alone or as a meet operand, through the Type
    /// arguments (SPEC 8.1.1, 8.1.2), keeping the instance when none occurs.</summary>
    /// <param name="origin">The Origin.</param>
    /// <param name="binder">The declaration whose Type arguments are given.</param>
    /// <param name="arguments">The Type arguments by slot.</param>
    /// <returns>The substituted Origin.</returns>
    private BoundOrigin SubstitutePairOrigins(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundType?> arguments)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.PairOuterImage(origin, binder, arguments) ?? origin;
        }

        // A meet's operands are atoms; it is rebuilt only when one of them is bound.
        var bound = false;
        for (var i = 0; i < origin.Operands.Count && !bound; i++)
        {
            bound = this.PairOuterImage(origin.Operands[i], binder, arguments) is not null;
        }

        if (!bound)
        {
            return origin;
        }

        var result = BoundOrigin.Static;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            result = this.Meet(result, this.PairOuterImage(origin.Operands[i], binder, arguments) ?? origin.Operands[i]);
        }

        return result;
    }
}
