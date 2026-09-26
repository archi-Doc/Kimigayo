// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The admitted set of every pair layer followed in this pass (SPEC 13.5.5.1), keyed by the @follow node.
    private readonly Dictionary<Koto, SemanticsMask> pairFollows = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>Gets the shared reference Type to <paramref name="referent"/>, whose Origin ownership analysis infers.</summary>
    /// <param name="referent">The borrowed Type.</param>
    /// <returns>The interned <c>ref/referent</c>.</returns>
    internal BoundType SharedReference(BoundType referent) => this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [referent]);

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
    {
        if (!exclusive)
        {
            return (this.PairAdmitted(followed) & SemanticsMask.Owner) == 0 || scope is null || this.BorrowablePlace(followed.Left, scope, false);
        }

        var admitted = this.PairAdmitted(followed);
        return (admitted & SemanticsMask.Ref) == 0 &&
            ((admitted & SemanticsMask.Uniq) == 0 || !ReachedThroughShared(followed.Left)) &&
            ((admitted & SemanticsMask.Owner) == 0 || (scope is null ? Writable(followed.Left) : this.BorrowablePlace(followed.Left, scope, true)));
    }
}
