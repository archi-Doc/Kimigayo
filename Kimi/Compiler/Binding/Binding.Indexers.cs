// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.6.9: receiver[key] on a user Type resolves through its Indexable<Key> conformance to a synthesized call of
    // index, and of indexUniq for updates and exclusive borrows. The synthesized calls are ordinary method calls of the
    // conforming Type's implementations, so receiver acquisition, key adaptation, Place results and generic instantiation
    // follow the ordinary rules; the index expression designates the published Place.
    private readonly Dictionary<(IndexKoto Source, bool Exclusive), InvocationKoto> indexerCalls = new();
    private readonly HashSet<IndexKoto> exclusiveIndexers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the synthesized <c>index</c> (shared) or <c>indexUniq</c> (exclusive) call of a user index expression, or null.</summary>
    /// <param name="node">The index expression.</param>
    /// <param name="exclusive">Whether the exclusive requirement is selected.</param>
    /// <returns>The bound call, or null for built-in indexing or an unbound mode.</returns>
    internal InvocationKoto? IndexerCall(Koto node, bool exclusive)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto index && index.BindingState == BindingState.Resolved &&
            this.indexerCalls.TryGetValue((index, exclusive), out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets a value indicating whether the receiver of a user index expression conforms to UniqIndexable, whatever its path permits.</summary>
    /// <param name="node">The index expression.</param>
    /// <returns>Whether indexUniq exists for the receiver.</returns>
    internal bool HasExclusiveIndexer(Koto node)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto index && this.exclusiveIndexers.Contains(index);

    // SPEC 8.4.2: whether the Contract, a bound reference or a declaration, is the declaration or refines it.
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

    private bool TryBindIndexer(IndexKoto source, BindingScope scope, BoundType? receiver, out BoundType? result)
    {
        result = null;
        var core = receiver;
        while (core is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            core = core.Components[0]; // SPEC 3.4.1: selection continues at the referent.
        }

        this.exclusiveIndexers.Remove(source);
        if (source.Right is RangeKoto || core is null || this.Library.Indexable is not { } indexable)
        {
            return false;
        }

        bool exclusiveAvailable;
        if (core.Symbol is { Declaration: StructKoto or EnumKoto } owner)
        {
            var conformance = this.ConformanceByDeclaration(owner, indexable, out var ambiguous);
            if (conformance is null)
            {
                if (!ambiguous)
                {
                    return false;
                }

                // SPEC 4.6.9: several Key conformances are distinct; selecting among them by the key Type remains a boundary (STATUS).
                this.BindNode(source.Right, scope);
                result = Fail(source, BindingFailure.Ambiguous);
                return true;
            }

            exclusiveAvailable = this.Library.UniqIndexable is { } uniqIndexable && this.ConformanceByDeclaration(owner, uniqIndexable, out _) is not null;
        }
        else if (core.Kind == BoundTypeKind.Parameter && this.HasContractFact(core, indexable, scope))
        {
            // SPEC 4.6.9, 8.4: a constrained Type parameter indexes through its Indexable fact; the synthesized call
            // resolves to the requirement, and the instance supplies the conforming Type's implementation.
            exclusiveAvailable = this.Library.UniqIndexable is { } uniqIndexable && this.HasContractFact(core, uniqIndexable, scope);
        }
        else
        {
            return false;
        }

        if (exclusiveAvailable)
        {
            this.exclusiveIndexers.Add(source);
        }

        var shared = this.BindIndexerCall(source, scope, false);
        if (shared?.BoundType is not { } element)
        {
            result = Complete(source, null);
            return true;
        }

        // SPEC 4.6.9: indexUniq is selected for updates and exclusive borrows. It is bound when the conformance offers it and
        // the receiver path can lend exclusively; a use that needs it through another path is diagnosed by that path.
        if (this.exclusiveIndexers.Contains(source) && PathAuthority(source.Left) != SemanticsKind.Ref &&
            (source.Left.BoundType?.Semantics == SemanticsKind.Uniq || Writable(source.Left)))
        {
            this.BindIndexerCall(source, scope, true);
        }

        result = Complete(source, element);
        return true;
    }

    // SPEC 8.4.2, 8.7: whether an available Constraint fact makes the subject conform to the Contract declaration or a refinement.
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
                if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, subject) && fact.Contract is { } bound &&
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
        if (!this.indexerCalls.TryGetValue((source, exclusive), out var call))
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
        if (key.BoundType is not null && key is NumberLiteralKoto or NullLiteralKoto or PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto })
        {
            if (key is UnaryKoto prefix)
            {
                ResetSynthetic(prefix.Operand);
            }

            ResetSynthetic(key);
        }

        return this.BindCall(call, scope, null) is null ? null : call;

        static void ResetSynthetic(Koto node)
        {
            node.BindingState = BindingState.Unvisited;
            node.BoundType = null;
            node.BoundSymbol = null;
            node.BindingFailure = BindingFailure.None;
        }
    }
}
