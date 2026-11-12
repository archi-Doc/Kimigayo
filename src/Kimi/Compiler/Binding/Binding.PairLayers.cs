// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The admitted set of every pair layer followed in this pass (SPEC 13.5.5), keyed by the @follow node.
    private readonly Dictionary<Koto, SemanticsMask> pairFollows = new(ReferenceEqualityComparer.Instance);

    // Receivers whose stored pair layer selection follows implicitly (SPEC 3.4.3, 7.3), with their admitted sets.
    private readonly Dictionary<Koto, SemanticsMask> implicitPairFollows = new(ReferenceEqualityComparer.Instance);
    private int termWalks;

    /// <summary>Gets the steps the walks of one Type's parts took in the latest pass (PLAN G647): a layer followed by OperandTerminal,
    /// PairSubject, PairTerminal, FormedApplication or SharedReferenceThroughLayers, and a part entered by CollectCallRelations,
    /// FailedOriginRelation, AddTypeClauseObligations, SolveOmittedOrigins, Occurs or CExchangeable; read by the scaling tests.</summary>
    internal int TermWalkWork => this.termWalks;

    /// <summary>Decomposes a pair layer (SPEC 13.5.5): the original <c>s/T</c>, stored as the pair's whole Type, an annotated
    /// occurrence of it (<c>s/T during a</c>, SPEC 8.1.2), or an application <c>s/U</c> to another Type. The pair target <c>T</c>
    /// alone is not a pair layer.</summary>
    /// <param name="type">The normalized Type.</param>
    /// <param name="whole">The pair's whole Type, which carries the Semantics premises.</param>
    /// <param name="target">The direct target.</param>
    /// <returns>Whether <paramref name="type"/> is a pair layer.</returns>
    internal static bool TryPairLayer(BoundType? type, out BoundType whole, out BoundType target)
    {
        if (type is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } original, Type: { } projection } })
        {
            whole = original;
            target = projection;
            return true;
        }

        if (type is { Kind: BoundTypeKind.SemanticsApplication, Symbol.WholeType: { } pair, Components.Count: 1 })
        {
            whole = pair;
            target = type.Components[0];
            return true;
        }

        // BoundType.IsAbstractType excludes a pair's own Type by its symbol alone, so every such Type decomposes above.
        if (type is { Kind: BoundTypeKind.Parameter, Symbol.Kind: BindingSymbolKind.SemanticsTarget })
        {
            throw new InvalidOperationException($"The pair `{type}` has no whole Type or target (InternalInvariant).");
        }

        whole = target = null!;
        return false;
    }

    /// <summary>Gets the admitted set of the pair layer a <c>@follow</c> node follows, or None for any other node.</summary>
    /// <param name="node">A <c>@follow</c> conversion.</param>
    /// <returns>The admitted Semantics, a subset of owner, ref and uniq.</returns>
    internal SemanticsMask PairAdmitted(Koto node) => this.pairFollows.GetValueOrDefault(node);

    /// <summary>Gets the admitted set of a pair layer that selection follows implicitly at a receiver (SPEC 3.4.3, 7.3), or None.</summary>
    /// <param name="node">The receiver operand whose stored pair layer is followed.</param>
    /// <returns>The admitted Semantics, a subset of owner, ref and uniq.</returns>
    internal SemanticsMask ImplicitPairAdmitted(Koto node) => this.implicitPairFollows.GetValueOrDefault(node);

    /// <summary>Gets the terminal target below every qualifying pair layer of a node's Type (SPEC 13.5.5), or null when
    /// its Type is no qualifying pair layer.</summary>
    /// <param name="node">A pair Subject or receiver.</param>
    /// <returns>The Type the followed layers end in.</returns>
    internal BoundType? PairTerminalOf(Koto node)
        => node.BoundType is { } type && this.ConstraintScope(node) is var scope && this.FollowablePair(type, scope, out var target) != SemanticsMask.None
            ? this.PairTerminal(target, scope) : null;

    /// <summary>Gets the object Semantics a Type takes in every case and the View Target they share (SPEC 3.4.3, 8.1.1, 8.15.2): a
    /// concrete object handle's or borrow's own, and a pair layer's admitted set where it lies in the object family, each case an object
    /// form of its direct target; None for any other Type. Selection and receivers read an object layer by it alone.</summary>
    /// <param name="type">A complete Type.</param>
    /// <param name="scope">The scope whose premises give a pair layer's admitted set.</param>
    /// <param name="view">The View Target.</param>
    /// <returns>The object Semantics of the cases.</returns>
    internal SemanticsMask ObjectCases(BoundType type, BindingScope scope, out BoundType view)
    {
        const SemanticsMask objectFamily = SemanticsMask.Object | SemanticsMask.ObjectBorrow;
        if (type is { Kind: BoundTypeKind.Semantics, Components: [var target] } && IsObjectSemantics(type.Semantics))
        {
            view = target;
            return type.Semantics.ToMask();
        }

        if (TryPairLayer(type, out var whole, out view) && this.AdmittedSemantics(whole, scope) is var admitted && admitted != SemanticsMask.None &&
            (admitted & ~objectFamily) == 0)
        {
            return admitted;
        }

        view = null!;
        return SemanticsMask.None;
    }

    /// <summary>Gets the View Target an object layer designates through stored references (SPEC 3.4.3, 13.5.5): a concrete handle's or
    /// view's, or a pair layer's in every case (<see cref="ObjectCases"/>); null for any other Type.</summary>
    /// <param name="type">A complete Type.</param>
    /// <param name="at">The node whose scope gives a pair layer's admitted set.</param>
    /// <returns>The View Target, or null.</returns>
    internal BoundType? ViewTarget(BoundType? type, Koto at)
    {
        while (ReferenceTypes.IsStorage(type))
        {
            type = type!.Components[0];
        }

        return type is not null && this.ObjectCases(type, this.ConstraintScope(at), out var view) != SemanticsMask.None ? view : null;
    }

    /// <summary>Tells whether a receiver's Place is reached through a stored reference or a borrow in every case (SPEC 4.7.1, 4.8.2): a
    /// storage reference (<see cref="ReferenceTypes.IsStorage"/>), or an object layer whose every case is an object borrow
    /// (<see cref="ObjectCases"/>); a case that is an owning handle keeps an owned root, as a concrete handle does.</summary>
    /// <param name="type">The receiver's Type.</param>
    /// <param name="at">The receiver, whose scope gives a pair layer's admitted set.</param>
    /// <returns>Whether no owned root holds the selected Place.</returns>
    internal bool BorrowsInEveryCase(BoundType? type, Koto at)
        => ReferenceTypes.IsStorage(type) ||
            (type is not null && this.ObjectCases(type, this.ConstraintScope(at), out _) is var cases && cases != SemanticsMask.None && (cases & ~SemanticsMask.ObjectBorrow) == 0);

    /// <summary>Gets the Semantics of a shared borrow of a stored value (SPEC 4.7.1, 10.2): `ref` of an owned value or a stored `uniq`,
    /// `objref` of an object layer in every case (<see cref="ObjectCases"/>; a shared view is Copy and never asks); null otherwise.</summary>
    /// <param name="stored">The stored Type.</param>
    /// <param name="at">The node whose scope gives a pair layer's admitted set.</param>
    /// <returns>The borrow's Semantics, or null.</returns>
    internal SemanticsKind? SharedBorrowSemantics(BoundType stored, Koto at) => stored.Semantics switch
    {
        SemanticsKind.Owner or SemanticsKind.Uniq => SemanticsKind.Ref,
        SemanticsKind.ObjRef => null,
        _ => this.ObjectCases(stored, this.ConstraintScope(at), out _) != SemanticsMask.None ? SemanticsKind.ObjRef : null,
    };

    // SPEC 13.5.5: the admitted set of a pair layer that may be followed, or None.
    private SemanticsMask FollowablePair(BoundType type, BindingScope scope, out BoundType target)
    {
        if (!TryPairLayer(type, out var whole, out target))
        {
            return SemanticsMask.None;
        }

        var admitted = this.AdmittedSemantics(whole, scope);
        return admitted != SemanticsMask.None && (admitted & ~(SemanticsMask.Owner | SemanticsMask.ValueBorrow)) == 0 ? admitted : SemanticsMask.None;
    }

    // SPEC 3.4.3: the Type a selection continues at below a safe reference layer (its referent) or an object layer in every case (its View
    // Target, ObjectCases); null for any other Type (a pair layer admitting a value case is followed after its own lookup, FollowablePair).
    private BoundType? ViewBelow(BoundType type, BindingScope scope)
        => type is { Kind: BoundTypeKind.Semantics, Components: [var below] } && IsBorrow(type.Semantics) ? below
            : this.ObjectCases(type, scope, out var view) != SemanticsMask.None ? view : null;

    // SPEC 13.5.5: the weakest capability over the admitted cases. Owner inherits the operand Place's own capability (a write
    // needs an assignable Place, such as an inline element of a writable root), uniq grants Write also through a let slot but
    // never through a shared path, and ref grants Read only.
    private bool PairCapability(ConversionKoto followed, BindingScope? scope, bool exclusive)
        => this.PairCapability(followed.Left, this.PairAdmitted(followed), scope, exclusive);

    private bool PairCapability(Koto operand, SemanticsMask admitted, BindingScope? scope, bool exclusive)
    {
        if (!exclusive)
        {
            return (admitted & SemanticsMask.Owner) == 0 || scope is null || this.BorrowablePlace(operand, scope, false);
        }

        return (admitted & SemanticsMask.Ref) == 0 &&
            ((admitted & SemanticsMask.Uniq) == 0 || !ReachedThroughShared(operand)) &&
            ((admitted & SemanticsMask.Owner) == 0 || (scope is null ? Writable(operand) || ElementAccess.WritableRoot(operand) is not null : this.BorrowablePlace(operand, scope, true)));
    }

    // SPEC 7.3, 13.5.5: a ref/Self or uniq/Self receiver selected through a pair layer is acquired as p@follow@ref or
    // p@follow@uniq; an exclusive receiver needs the weakest admitted Write capability.
    private bool TryPairReceiver(Koto source, BoundType pattern, BoundType actual, BindingScope scope, out BoundType adapted, out ArgumentOperationKind kind, bool record)
    {
        adapted = actual;
        kind = ArgumentOperationKind.Value;
        if (pattern is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return false;
        }

        var exclusive = pattern.Semantics == SemanticsKind.Uniq;
        var admitted = this.FollowablePair(actual, scope, out var target);
        if (admitted != SemanticsMask.None && !IsSubtype(target, pattern.Components[0]))
        {
            target = this.PairTerminal(target, scope, exclusive); // A receiver is also selected below further layers (s/(t/U)).
        }

        if (admitted == SemanticsMask.None || target is null || !this.PairCapability(source, admitted, scope, exclusive))
        {
            return false;
        }

        if (IsSubtype(target, pattern.Components[0]))
        {
            adapted = this.types.Reference(pattern.Semantics, target, this.PairOrigin(source, actual, admitted));
            kind = ArgumentOperationKind.Borrow;
        }
        else if (!exclusive && target is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref } &&
            this.SharedReferenceThroughLayers(target, pattern.Components[0], out _) is { } inner)
        {
            // The inner shared reference Copies in every admitted case and keeps only its own dependencies.
            adapted = inner;
            kind = ArgumentOperationKind.ReferenceRead;
        }
        else
        {
            return false;
        }

        if (record)
        {
            this.implicitPairFollows[source] = admitted;
        }

        return true;
    }

    // SPEC 15.1.6, 14.6.2: a bare Subject Place that is a qualifying pair layer is followed like a borrow value, through
    // every further qualifying layer (s/(t/U)). The Subject is a reference to the terminal target in the weakest mode over
    // the admitted cases of every layer: Shared unless every layer admits only uniq, and Shared on any shared path.
    private BoundType? PairSubject(Koto node, BoundType? type, BindingScope scope)
    {
        if (type is null || !IsBarePlace(node) || (this.FollowablePair(type, scope, out var target) is var admitted && admitted == SemanticsMask.None))
        {
            return null;
        }

        var exclusive = admitted == SemanticsMask.Uniq && !ReachedThroughShared(node);
        while (this.FollowablePair(target, scope, out var next) is var inner && inner != SemanticsMask.None)
        {
            this.termWalks++;
            exclusive &= inner == SemanticsMask.Uniq;
            target = next;
        }

        this.implicitPairFollows[node] = admitted;
        return this.types.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, target, this.PairOrigin(node, type, admitted));
    }

    // SPEC 13.5.5: the Type below every further qualifying pair layer of a pair target. Following for Write needs Write
    // through every layer, so an inner layer admitting ref ends an exclusive walk (the outer layer's capability is checked
    // at its operand). The outer layer's Origin bounds the result in every case: each inner Origin outlives it.
    private BoundType? PairTerminal(BoundType target, BindingScope scope, bool exclusive = false)
    {
        while (this.FollowablePair(target, scope, out var next) is var admitted && admitted != SemanticsMask.None)
        {
            this.termWalks++;
            if (exclusive && (admitted & SemanticsMask.Ref) != 0)
            {
                return null;
            }

            target = next;
        }

        return target;
    }

    // The dependency of a reference through a pair layer at a Place (PairSlot): the operand Place, or the slot met with the borrows
    // on the operand's path.
    private BoundOrigin PairOrigin(Koto node, BoundType? pair, SemanticsMask admitted)
        => this.PairSlot(pair, admitted, out var met) is not { } slot ? this.PlaceOrigin(node) : met ? this.PathReborrowOrigin(node, slot)! : slot;

    // SPEC 13.5.5, 15.5, 15.6.3: the one case split of a reference's dependency through a pair layer. It depends on the Place reached
    // when owner is admitted (Borrow; null here) and otherwise keeps the dependencies of the stored reference, like a Reborrow: the
    // layer's outer-Origin slot (`o` for the original pair, the annotation or the application's own slot otherwise; null, the Place
    // reached, for an application without a slot), met (`met`) with the borrows that reach the layer when an exclusive case is
    // admitted, as that case's follow of its concrete reference is. The meet over the cases keeps the one judgment as strong as
    // every case's. PairOrigin (a Place) and SelectLayers (a Pattern path) supply the Place reached and the borrows reaching it.
    private BoundOrigin? PairSlot(BoundType? pair, SemanticsMask admitted, out bool met)
    {
        met = (admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0;
        return (admitted & SemanticsMask.Owner) != 0 || pair is null ? null : this.OuterOrigin(pair);
    }
}
