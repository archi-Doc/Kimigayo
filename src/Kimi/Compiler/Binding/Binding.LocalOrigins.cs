// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A local's omitted Origins (SPEC 15.3.6, 15.4.4): the Origins its initializer infers while it binds, and the Origins its annotation
// omits, inferred from the initializer's Type and solved with the local's clauses.
// - Responsibility: ResolveInitializerOrigins closes the initializer's acquisition (initializerOrigins, whose bounds the fits record in
//   Binding.OriginProof.cs) and rewrites the initializer's Type; InferLocalOrigins matches the annotation against that Type, giving a
//   mutable local's slots and every slot no fit binds regions (LocalRegionSlot); SolveLocalBounds solves the slots its clauses bound.
// - Inputs: the local's declared and initializer Types, the bounds its initializer's fits recorded, its clauses (originDeclarations).
// - Outputs: the replacements of the local's two Origin declarations, the local's rewritten Types and annotation nodes, the discharged
//   inference obligations of resolved Origins, and the region bounds that publish their resolutions.
// - Invariants: a local's Origins are resolved once, when its initializer is bound, before any later use reads its Type; the
//   initializer's declaration is closed (State 3) before the annotation's is solved.
// - Prohibitions: no judgment of a relation (Binding.OriginClauses.cs and the proof judge them); no message text.
public sealed partial class Binding
{
    // Origins omitted in a local initializer's own Type expressions (SPEC 15.4.4), inferred while that initializer binds. They
    // are kept apart from declared Origin contracts, which are completed before bodies bind; entries are reused across binds.
    private readonly Dictionary<Koto, OriginDeclaration> initializerOrigins = new(ReferenceEqualityComparer.Instance);

    // SPEC 15.4.4, 15.3.6: `local`'s initializer has acquired every bound. An Origin its initializer omits whose bounds reach no Origin
    // but its own variables (none, or a cycle among them) has no candidate and is a local region of `local` (DECISIONS R4-M112); each
    // resolved Origin's inference obligation is discharged, and the resolutions are published as region bounds. `slot` is the local's
    // next region slot (BindVariableCore).
    private BoundType ResolveInitializerOrigins(BoundType type, OriginDeclaration declaration, VariableKoto local, BindingScope scope, ref int slot)
    {
        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation is not { Kind: BindingObligationKind.OriginInference, Longer: { } pending })
            {
                continue;
            }

            var bound = declaration.Replacements.ContainsKey(pending);
            if (!bound || declaration.Replacements.ContainsKey(this.ResolveOrigin(pending, declaration)))
            {
                if (IsWithin(pending.Binder!, local.InitializerKoto!) && ReferenceEquals(this.OpenInitializer(pending, obligation.Use), local))
                {
                    declaration.Replacements[pending] = this.LocalRegionSlot(local, slot--);
                }
                else if (!bound)
                {
                    continue;
                }
            }

            this.obligationSet.Remove(obligation);
            this.obligations.RemoveAt(i);
        }

        declaration.Scope = scope;
        declaration.State = 3;
        this.originStateVersion++;
        foreach (var replacement in declaration.Replacements)
        {
            this.RecordRegionBound(this.ResolveOrigin(replacement.Value, declaration), replacement.Key, local);
        }

        return this.RewriteOrigins(type, declaration);
    }

    // SPEC 15.3.6, 15.4.4: an Origin the local's Type omits takes its candidate from the initializer's Type; one that no fit binds, bound
    // only to itself or no longer reached by the declared Type (a projection base's slot that the projection's normalization drops), has
    // none and is a local region of `owner` (DECISIONS R4-M112 (c)), numbered after the initializer's. `slot` is the local's next region
    // slot (BindVariableCore).
    private BoundType InferLocalOrigins(BoundType declared, BoundType actual, VariableKoto owner, BindingScope scope, int slot)
    {
        OriginDeclaration? declaration = null;
        if (declared.CarriesOrigin && actual.CarriesOrigin)
        {
            Match(declared, actual, OriginVariance.Covariant);
        }

        // OmittedOrigin creates the local's declaration with the first Origin its Type omits.
        if (declaration is null && !this.originDeclarations.ContainsKey(owner))
        {
            return declared;
        }

        if (declaration?.Inferred is { Count: > 0 } inferred)
        {
            this.SolveLocalBounds(declaration, inferred, owner, ref slot);
        }

        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation is not { Kind: BindingObligationKind.OriginInference, Longer: { } pending })
            {
                continue;
            }

            if (declaration?.Replacements.ContainsKey(pending) != true)
            {
                if (!IsWithin(pending.Binder!, owner.TypeKoto!))
                {
                    continue;
                }

                declaration ??= this.OriginDeclarationFor(owner);
                declaration.Replacements[pending] = this.LocalRegionSlot(owner, slot--);
            }

            this.obligationSet.Remove(obligation);
            this.obligations.RemoveAt(i);
        }

        if (declaration is null)
        {
            return declared;
        }

        declaration.Scope = scope;
        declaration.State = 3;
        this.originStateVersion++;
        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(owner.TypeKoto!);
        visitor.Declaration = null;
        return this.RewriteOrigins(declared, declaration);

        // `polarity` is the variance of the position within the local's Type, composed as FitsTypeCore compares it.
        void Match(BoundType pattern, BoundType value, OriginVariance polarity)
        {
            if (pattern.Kind != value.Kind || pattern.Semantics != value.Semantics || !ReferenceEquals(pattern.Symbol, value.Symbol))
            {
                return;
            }

            if (pattern.Origin is { } p && value.Origin is { } a)
            {
                Bind(p, a, polarity);
            }

            for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, value.OriginArguments.Count); i++)
            {
                Bind(pattern.OriginArguments[i], value.OriginArguments[i], ComposeVariance(polarity, OriginArgumentVariance(pattern, i, this)));
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, value.Components.Count); i++)
            {
                Match(pattern.Components[i], value.Components[i], ComposeVariance(polarity, ComponentVariance(pattern, i, this)));
            }
        }

        void Bind(BoundOrigin pending, BoundOrigin value, OriginVariance polarity)
        {
            if (pending.Kind == OriginKind.Inference && !IsLocalRegion(pending))
            {
                declaration ??= this.OriginDeclarationFor(owner);
                if (IsMutableDeclaration(owner))
                {
                    var region = declaration.Replacements.TryGetValue(pending, out var previous) && IsLocalRegion(previous)
                        ? previous : this.LocalRegionSlot(owner, slot--);
                    declaration.Replacements[pending] = region;
                    this.originStateVersion++;
                    if (!ReferenceEquals(value, pending))
                    {
                        this.AddOriginFit(value, region, declared, owner.InitializerKoto!, polarity);
                    }
                }
                else
                {
                    // SPEC 15.3.6: the slot itself is no candidate, so a slot bound only to itself is a local region (DECISIONS R4-M112).
                    declaration.Replacements[pending] = declaration.Replacements.TryGetValue(pending, out var previous) ? this.Meet(previous, value)
                        : ReferenceEquals(value, pending) ? this.LocalRegionSlot(owner, slot--) : value;
                    this.originStateVersion++;
                }

                if (declaration.Relations.Count != 0)
                {
                    // Local clauses use this composed variance when solving their bounds.
                    var inferred = declaration.Inferred ??= new(ReferenceEqualityComparer.Instance);
                    inferred[pending] = inferred.TryGetValue(pending, out var seen) && seen != polarity ? OriginVariance.Invariant : polarity;
                }
            }
        }
    }

    // SPEC 15.3.6, 15.4.4: covariant positions meet upper bounds; contravariant positions select an existing lower bound proven to
    // outlive the others. Repeated passes propagate dependent slots without inventing unions. Once every lower bound is seen, a
    // contravariant slot whose candidate is not proven to outlive each of them has no candidate and is a local region of `owner`, at
    // `slot` (DECISIONS R4-M112): one decision, whatever the clause order.
    private void SolveLocalBounds(OriginDeclaration declaration, Dictionary<BoundOrigin, OriginVariance> inferred, VariableKoto owner, ref int slot)
    {
        for (var pass = 0; pass <= declaration.Relations.Count; pass++)
        {
            var changed = false;
            foreach (var relation in declaration.Relations)
            {
                if (relation.Equality)
                {
                    continue;
                }

                if (inferred.TryGetValue(relation.Longer, out var lowerVariance) && lowerVariance == OriginVariance.Contravariant &&
                    declaration.Replacements.TryGetValue(relation.Longer, out var lowerCandidate) && !IsLocalRegion(lowerCandidate))
                {
                    var lower = this.ResolveOrigin(relation.Shorter, declaration);
                    if (!this.ProvesOriginOutlives(lowerCandidate, lower, relation.Syntax) && this.ProvesOriginOutlives(lower, lowerCandidate, relation.Syntax))
                    {
                        declaration.Replacements[relation.Longer] = lower;
                        this.originStateVersion++;
                        changed = true;
                    }
                }

                if (!inferred.TryGetValue(relation.Shorter, out var variance) || variance != OriginVariance.Covariant ||
                    !declaration.Replacements.TryGetValue(relation.Shorter, out var current))
                {
                    continue;
                }

                var longer = this.ResolveOrigin(relation.Longer, declaration);
                if (IsLocalRegion(current))
                {
                    this.AddObligation(new(BindingObligationKind.OriginOutlives, relation.Syntax, BindingDeadline.BodyOrigins, null, longer, current, Clause: relation.Syntax));
                    continue;
                }

                var met = this.Meet(current, longer);
                if (!ReferenceEquals(met, current))
                {
                    declaration.Replacements[relation.Shorter] = met;
                    this.originStateVersion++;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        foreach (var relation in declaration.Relations)
        {
            if (relation.Equality || inferred.GetValueOrDefault(relation.Longer) != OriginVariance.Contravariant ||
                !declaration.Replacements.TryGetValue(relation.Longer, out var candidate) || IsLocalRegion(candidate))
            {
                continue;
            }

            if (!this.ProvesOriginOutlives(candidate, this.ResolveOrigin(relation.Shorter, declaration), relation.Syntax))
            {
                declaration.Replacements[relation.Longer] = this.LocalRegionSlot(owner, slot--);
                this.originStateVersion++;
            }
        }
    }
}
