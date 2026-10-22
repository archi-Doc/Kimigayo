// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A local's omitted Origins (SPEC 15.3.6, 15.4.4): the Origins its initializer infers while it binds, and the Origins its annotation
// omits, inferred from the initializer's Type and solved with the local's clauses.
// - Responsibility: ResolveInitializerOrigins closes the initializer's acquisition (initializerOrigins, whose bounds the fits record in
//   Binding.OriginProof.cs) and rewrites the initializer's Type; InferLocalOrigins matches the annotation against that Type, giving a
//   mutable local's slots regions (LocalRegionSlot); SolveLocalBounds solves the slots its clauses bound.
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

    private BoundType ResolveInitializerOrigins(BoundType type, OriginDeclaration declaration, BindingScope scope)
    {
        declaration.Scope = scope;
        declaration.State = 3;
        this.originStateVersion++;
        foreach (var replacement in declaration.Replacements)
        {
            this.RecordRegionBound(this.ResolveOrigin(replacement.Value, declaration), replacement.Key, declaration.Owner);
        }

        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending && declaration.Replacements.ContainsKey(pending))
            {
                this.obligationSet.Remove(obligation);
                this.obligations.RemoveAt(i);
            }
        }

        return this.RewriteOrigins(type, declaration);
    }

    private BoundType InferLocalOrigins(BoundType declared, BoundType actual, VariableKoto owner, BindingScope scope)
    {
        if (!declared.CarriesOrigin || !actual.CarriesOrigin)
        {
            return declared;
        }

        OriginDeclaration? declaration = null;
        var localSlot = -1;
        Match(declared, actual, OriginVariance.Covariant);
        if (declaration is null)
        {
            return declared;
        }

        if (declaration.Inferred is { Count: > 0 } inferred)
        {
            this.SolveLocalBounds(declaration, inferred);
        }

        declaration.Scope = scope;
        declaration.State = 3;
        this.originStateVersion++;
        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(owner.TypeKoto!);
        visitor.Declaration = null;
        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending && declaration.Replacements.ContainsKey(pending))
            {
                this.obligationSet.Remove(obligation);
                this.obligations.RemoveAt(i);
            }
        }

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
                Bind(pattern.OriginArguments[i], value.OriginArguments[i], ComposeVariance(polarity, pattern.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant));
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, value.Components.Count); i++)
            {
                Match(pattern.Components[i], value.Components[i], ComposeVariance(polarity, this.ComponentVariance(pattern, i)));
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
                        ? previous : this.LocalRegionSlot(owner, localSlot--);
                    declaration.Replacements[pending] = region;
                    this.originStateVersion++;
                    if (!ReferenceEquals(value, pending))
                    {
                        this.AddOriginFit(value, region, declared, owner.InitializerKoto!, polarity);
                    }
                }
                else
                {
                    declaration.Replacements[pending] = declaration.Replacements.TryGetValue(pending, out var previous) ? this.Meet(previous, value) : value;
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

    // SPEC 15.3.6, 15.4.4: covariant positions meet upper bounds; contravariant positions select an existing
    // lower bound proven to outlive the others. Repeated passes propagate dependent slots without inventing unions.
    private void SolveLocalBounds(OriginDeclaration declaration, Dictionary<BoundOrigin, OriginVariance> inferred)
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
    }
}
