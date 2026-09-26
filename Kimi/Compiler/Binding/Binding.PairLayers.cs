// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The admitted set of every pair layer followed in this pass (SPEC 13.5.5.1), keyed by the @follow node.
    private readonly Dictionary<Koto, SemanticsMask> pairFollows = new(ReferenceEqualityComparer.Instance);

    // Receivers whose stored pair layer selection follows implicitly (SPEC 3.4.1, 7.3), with their admitted sets.
    private readonly Dictionary<Koto, SemanticsMask> implicitPairFollows = new(ReferenceEqualityComparer.Instance);

    /// <summary>Decomposes a pair layer (SPEC 13.5.5.1): the original <c>s/T</c>, stored as the pair's whole Type, or an
    /// application <c>s/U</c> to another Type. The pair target <c>T</c> alone is not a pair layer.</summary>
    /// <param name="type">The normalized Type.</param>
    /// <param name="whole">The pair's whole Type, which carries the Semantics premises.</param>
    /// <param name="target">The direct target.</param>
    /// <returns>Whether <paramref name="type"/> is a pair layer.</returns>
    internal static bool TryPairLayer(BoundType? type, out BoundType whole, out BoundType target)
    {
        if (type is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } original, Type: { } projection } } &&
            ReferenceEquals(type, original))
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

        whole = target = null!;
        return false;
    }

    /// <summary>Gets the admitted set of the pair layer a <c>@follow</c> node follows, or None for any other node.</summary>
    /// <param name="node">A <c>@follow</c> conversion.</param>
    /// <returns>The admitted Semantics, a subset of owner, ref and uniq.</returns>
    internal SemanticsMask PairAdmitted(Koto node) => this.pairFollows.GetValueOrDefault(node);

    /// <summary>Gets the shared reference Type to <paramref name="referent"/>.</summary>
    /// <param name="referent">The borrowed Type.</param>
    /// <param name="origin">The reference's Origin, or null when ownership analysis infers it.</param>
    /// <returns>The interned <c>ref/referent</c>.</returns>
    internal BoundType SharedReference(BoundType referent, BoundOrigin? origin = null) => this.Reference(SemanticsKind.Ref, referent, origin);

    /// <summary>Gets the shared or exclusive reference Type to <paramref name="referent"/>.</summary>
    /// <param name="semantics">Ref or Uniq.</param>
    /// <param name="referent">The borrowed Type.</param>
    /// <param name="origin">The reference's Origin, or null when ownership analysis infers it.</param>
    /// <returns>The interned reference Type.</returns>
    internal BoundType Reference(SemanticsKind semantics, BoundType referent, BoundOrigin? origin = null) => this.InternType(BoundTypeKind.Semantics, null, semantics, [referent], origin: origin);

    /// <summary>Gets the admitted set of a pair layer that selection follows implicitly at a receiver (SPEC 3.4.1, 7.3), or None.</summary>
    /// <param name="node">The receiver operand whose stored pair layer is followed.</param>
    /// <returns>The admitted Semantics, a subset of owner, ref and uniq.</returns>
    internal SemanticsMask ImplicitPairAdmitted(Koto node) => this.implicitPairFollows.GetValueOrDefault(node);

    // SPEC 13.5.5.1: the admitted set of a pair layer that may be followed, or None.
    private SemanticsMask FollowablePair(BoundType type, BindingScope scope, out BoundType target)
    {
        if (!TryPairLayer(type, out var whole, out target))
        {
            return SemanticsMask.None;
        }

        var admitted = this.AdmittedSemantics(whole, scope);
        return admitted != SemanticsMask.None && (admitted & ~(SemanticsMask.Owner | SemanticsMask.ValueBorrow)) == 0 ? admitted : SemanticsMask.None;
    }

    // SPEC 13.5.5.1: the weakest capability over the admitted cases. Owner inherits the operand Place's own capability,
    // uniq grants Write also through a let slot but never through a shared path, and ref grants Read only.
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
            ((admitted & SemanticsMask.Owner) == 0 || (scope is null ? Writable(operand) : this.BorrowablePlace(operand, scope, true)));
    }

    // SPEC 7.3, 13.5.5.1: a ref/Self or uniq/Self receiver selected through a pair layer is acquired as p@follow@ref or
    // p@follow@uniq; an exclusive receiver needs the weakest admitted Write capability.
    private bool TryPairReceiver(Koto source, BoundType pattern, BoundType actual, BindingScope scope, out BoundType adapted)
    {
        adapted = actual;
        if (pattern is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return false;
        }

        var admitted = this.FollowablePair(actual, scope, out var target);
        if (admitted == SemanticsMask.None || !FitsType(target, pattern.Components[0]) || !this.PairCapability(source, admitted, scope, pattern.Semantics == SemanticsKind.Uniq))
        {
            return false;
        }

        this.implicitPairFollows[source] = admitted;
        adapted = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [target], origin: this.PairOrigin(source, actual, admitted));
        return true;
    }

    // SPEC 15.1.6, 14.6.2: a bare Subject Place that is a qualifying pair layer is followed like a borrow value. The Subject
    // is a reference to the pair target in the weakest mode over the admitted cases: Shared unless only uniq is admitted,
    // and Shared on any shared path.
    private BoundType? PairSubject(Koto node, BoundType? type, BindingScope scope)
    {
        if (type is null || !IsBarePlace(node) || (this.FollowablePair(type, scope, out var target) is var admitted && admitted == SemanticsMask.None))
        {
            return null;
        }

        this.implicitPairFollows[node] = admitted;
        var exclusive = admitted == SemanticsMask.Uniq && !ReachedThroughShared(node);
        return this.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, target, this.PairOrigin(node, type, admitted));
    }

    // SPEC 13.5.5.1: the Type below every further qualifying pair layer of a pair target.
    private BoundType PairTerminal(BoundType target, BindingScope scope)
    {
        for (var depth = 0; depth < 16 && this.FollowablePair(target, scope, out var next) != SemanticsMask.None; depth++)
        {
            target = next;
        }

        return target;
    }

    // SPEC 13.5.5.1: a reference through a pair layer depends on the operand Place when owner is admitted (Borrow) and
    // otherwise on the stored reference (Reborrow).
    private BoundOrigin PairOrigin(Koto node, BoundType pair, SemanticsMask admitted)
        => (admitted & SemanticsMask.Owner) != 0 ? this.PlaceOrigin(node) : pair.Origin ?? this.PlaceOrigin(node);
}
