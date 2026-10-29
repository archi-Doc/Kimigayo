// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.7.1: receiver[key] on a user Type resolves through its Indexable<Key> conformance to a synthesized call of
    // index, and of indexUniq for updates and exclusive borrows. The synthesized calls are ordinary method calls of the
    // conforming Type's implementations, so receiver acquisition, key adaptation, reference results and generic instantiation
    // follow the ordinary rules; the index expression designates the referent of the reference the selected call returns (LentElement).
    private readonly Dictionary<(IndexKoto Source, bool Exclusive), InvocationKoto> indexerCalls = new();

    // The UniqIndexable side of each user or view index expression, the fact indexUniq's binding and SharedIndexNote read: whether
    // indexUniq is available here, and the receiver's UniqIndexable conformance the check selected by declaration or by the key (none
    // for a parameter's fact or a view).
    private readonly Dictionary<IndexKoto, UniqIndexing> uniqIndexing = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the synthesized <c>index</c> (shared) or <c>indexUniq</c> (exclusive) call of a user index expression, or null.</summary>
    /// <param name="node">The index expression.</param>
    /// <param name="exclusive">Whether the exclusive requirement is selected.</param>
    /// <returns>The bound call, or null for built-in indexing or an unbound mode.</returns>
    internal InvocationKoto? IndexerCall(Koto node, bool exclusive)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto index && index.BindingState == BindingState.Resolved &&
            this.indexerCalls.TryGetValue((index, exclusive), out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets the call a user or view index expression selects (SPEC 4.7.1): its indexer in the mode given, else its view range call.</summary>
    /// <param name="node">The index expression.</param>
    /// <param name="exclusive">Whether an update or an exclusive borrow takes the element (indexUniq).</param>
    /// <returns>The selected call, or null for built-in indexing.</returns>
    internal InvocationKoto? SelectedIndexCall(Koto node, bool exclusive) => this.IndexerCall(node, exclusive) ?? this.ViewRangeCall(node);

    /// <summary>Gets a value indicating whether the receiver of a user index expression conforms to UniqIndexable, whatever its path permits.</summary>
    /// <param name="node">The index expression.</param>
    /// <returns>Whether indexUniq exists for the receiver.</returns>
    internal bool HasExclusiveIndexer(Koto node)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto index && this.uniqIndexing.GetValueOrDefault(index).Exclusive;

    // SPEC 8.4.3: whether the Contract, a bound reference or a declaration, is the declaration or refines it.
    private static bool RefinesDeclaration(BindingSymbol contract, Koto declaration)
    {
        if (ReferenceEquals(contract.Declaration, declaration))
        {
            return true;
        }

        var shape = contract.Contract;
        for (var i = 0; shape is not null && i < shape.Ancestors.Count; i++)
        {
            if (ReferenceEquals(shape.Ancestors[i].Declaration, declaration))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 4.7.1: the element a selected index or indexUniq call lends, the referent of the reference its plan returns; the index
    // expression designates it, whatever the call node itself is typed.
    private static BoundType? LentElement(InvocationKoto? call)
        => call?.BoundCall?.ReturnType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var element] } ? element : null;

    // SPEC 4.7.1: the Type a key selects a conformance by, a borrowed key's referent (the key parameter is ref/Key); null for `null`
    // or an unfitted number literal, which could fit several keys.
    private static BoundType? KeyType(Koto keyNode)
    {
        var key = KotoHelper.UnwrapParentheses(keyNode);
        if (key is NullLiteralKoto || KotoHelper.SignedNumber(key, out _) is not null || key.BoundType is not { } type)
        {
            return null;
        }

        while (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            type = type.Components[0];
        }

        return type;
    }

    private bool TryBindIndexer(IndexKoto source, BindingScope scope, BoundType? receiver, out BoundType? result)
    {
        result = null;
        var core = receiver;
        while (core is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            core = core.Components[0]; // SPEC 3.4.3: selection continues at the referent.
        }

        // SPEC 4.7, 4.7.1: a Dictionary or user receiver takes a key of the key's own Type, a range literal among them, when it
        // takes a Range key; for any other receiver a range reads as the slice that only the concrete sequences offer
        // (NotIndexable_Kd).
        this.uniqIndexing.Remove(source);
        if (core is null || this.Library.Indexable is not { } indexable ||
            (source.Right is RangeKoto && core.Kind != BoundTypeKind.Dictionary && !this.TakesRangeKey(core, indexable, scope)))
        {
            return false;
        }

        bool exclusiveAvailable;
        BoundConformance? uniq = null;
        if (core.Symbol is { Declaration: StructKoto or EnumKoto } owner)
        {
            var conformance = this.ConformanceByDeclaration(owner, indexable, out var ambiguous);
            if (conformance is null)
            {
                if (!ambiguous)
                {
                    return false;
                }

                // SPEC 4.7.1: several Key conformances are distinct; the key's own Type selects one, and the call then selects
                // that conformance's index by the same key. An unfitted literal key could fit several and stays ambiguous.
                this.BindNode(source.Right, scope);
                if (this.ConformanceForKey(core, indexable, source.Right, scope) is null)
                {
                    result = this.Fail(source, BindingFailure.Ambiguous);
                    return true;
                }

                uniq = this.Library.UniqIndexable is { } uniqByKey ? this.ConformanceForKey(core, uniqByKey, source.Right, scope) : null;
            }
            else
            {
                uniq = this.Library.UniqIndexable is { } uniqIndexable ? this.ConformanceByDeclaration(owner, uniqIndexable, out _) : null;
            }

            exclusiveAvailable = this.ConformsHere(core, uniq, scope);
        }
        else if (core.Kind == BoundTypeKind.Parameter && this.HasContractFact(core, indexable, scope))
        {
            // SPEC 4.7.1, 8.4: a constrained Type parameter indexes through its Indexable fact; the synthesized call
            // resolves to the requirement, and the instance supplies the conforming Type's implementation.
            exclusiveAvailable = this.Library.UniqIndexable is { } uniqIndexable && this.HasContractFact(core, uniqIndexable, scope);
        }
        else
        {
            return false;
        }

        this.uniqIndexing[source] = new(exclusiveAvailable, uniq);
        var shared = this.BindIndexerCall(source, scope, false);
        if (LentElement(shared) is not { } element)
        {
            result = Complete(source, null);
            return true;
        }

        // SPEC 4.7.1, 15.1.5: indexUniq is selected for updates and exclusive borrows. It is bound when the conformance offers it and
        // the receiver can be exclusively acquired; a use that needs it through another path is diagnosed by that path.
        if (exclusiveAvailable && ExclusivelyAcquirable(source.Left))
        {
            this.BindIndexerCall(source, scope, true);
        }

        result = Complete(source, element);
        return true;
    }

    // Whether a registered Indexable conformance of the receiver's declaration takes a Range or ClosedRange key for the receiver: its
    // bound reference instantiated for `core` and normalized in `scope` (a key named through a projection, as NamesReference reads it).
    private bool TakesRangeKey(BoundType core, BindingSymbol indexable, BindingScope scope)
    {
        if (core.Symbol is not { Declaration: StructKoto or EnumKoto } owner || !this.conformancesByType.TryGetValue(owner, out var identities))
        {
            return false;
        }

        for (var i = 0; i < identities.Count; i++)
        {
            if (!ReferenceEquals(identities[i].Contract.Declaration, indexable.Declaration) || identities[i].Contract.Type is not { } formal)
            {
                continue;
            }

            if (this.StoredType(formal, core) is { } instance && this.ContractType(instance, scope) is { Components: [var key] } && IsRangeShape(key))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 4.7.1, 8.6.1: the one conformance of the receiver's declaration to a bound reference of `contract` that names the key's
    // reference (`contract` with the key's Type as its argument) for `core` in `scope` (NamesReference), or null for none or several.
    private BoundConformance? ConformanceForKey(BoundType core, BindingSymbol contract, Koto keyNode, BindingScope scope)
    {
        if (KeyType(keyNode) is not { } type || core.Symbol is not { } owner || !this.conformancesByType.TryGetValue(owner, out var identities))
        {
            return null;
        }

        BoundConformance? found = null;
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            if (ReferenceEquals(identity.Contract.Declaration, contract.Declaration) && identity.Paths.Count != 0 && identity.Contract.Type is { } formal &&
                this.NamesReference(formal, core, this.types.Rebuild(formal, [type], null, []), scope))
            {
                if (found is not null)
                {
                    return null;
                }

                found = identity;
            }
        }

        return found;
    }

    // SPEC 8.3.3: a registered conformance supplies its requirements to this instance only when its conditions hold here;
    // a refuted or unproven condition supplies nothing, and the Type stays usable through its other conformances.
    private bool ConformsHere(BoundType type, BoundConformance? conformance, BindingScope scope)
        => conformance is not null && this.ProveConformance(type, conformance.Contract, scope) == ConstraintProof.Proven;

    // SPEC 8.4.3, 8.3: whether an available Constraint fact makes the subject conform to the Contract declaration or a refinement.
    private bool HasContractFact(BoundType subject, BindingSymbol contract, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && AssociatedIdentityMatches(fact.Subject, subject) && fact.Contract is { } bound &&
                    this.AvailableConstraintFact(environment, fact) && RefinesDeclaration(bound, contract.Declaration))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private InvocationKoto? BindIndexerCall(IndexKoto source, BindingScope scope, bool exclusive)
    {
        // A cached call is reused only while it names the current receiver and key; an edit may replace either.
        if (!this.indexerCalls.TryGetValue((source, exclusive), out var call) || !ReferenceEquals(call.ArgumentNodes[0], source.Right) ||
            !ReferenceEquals(((MemberAccessKoto)call.Method).Left, source.Left))
        {
            var callee = new MemberAccessKoto(source, source.Left, new IdentifierNameKoto(source, exclusive ? "indexUniq" : "index"));
            call = new InvocationKoto(source, callee, [source.Right]);
            this.indexerCalls[(source, exclusive)] = call;
        }
        else
        {
            // A synthesized node is outside the tree that each Bind resets.
            ResetSynthetic(call);
            ResetSynthetic(call.Method);
            ResetSynthetic(((MemberAccessKoto)call.Method).Right);
        }

        // SPEC 10.2: a literal key is an unfitted literal that each call fits to ref/Key once; the second synthesized call
        // fits the key the first one already completed.
        var key = KotoHelper.UnwrapParentheses(source.Right);
        if (key.BoundType is not null && (key is NullLiteralKoto || KotoHelper.SignedNumber(key, out _) is not null))
        {
            if (key is UnaryKoto prefix)
            {
                ResetSynthetic(prefix.Operand);
            }

            ResetSynthetic(key);
        }

        // Like every synthesized call, its own failure is a published fact that the index expression rests on.
        this.nodes.Add(call);
        return this.BindCall(call, scope, null) is null ? null : call;
    }

    // A user or view index expression's UniqIndexable fact (uniqIndexing).
    private readonly record struct UniqIndexing(bool Exclusive, BoundConformance? Conformance);
}
