// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<(BoundOrigin Longer, BoundOrigin Shorter)> originProofPath = new();

    internal bool IsVerifiedOriginObligation(in BindingObligation obligation)
    {
        if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending)
        {
            return this.OriginAtUse(pending, obligation.Use).Kind is not (OriginKind.Inference or OriginKind.Unbound);
        }

        if (obligation.Kind == BindingObligationKind.OriginOutlives && obligation.Longer is { } longer && obligation.Shorter is { } shorter)
        {
            return this.ProvesOriginOutlives(longer, shorter, obligation.Use);
        }

        return false;
    }

    // SPEC 15.6.5: an unproven relation fails only when it is Refuted, a finite Origin outliving a fixed one, or Unknown between two
    // fixed Origins.
    internal bool OriginRelationFails(BoundOrigin longer, BoundOrigin shorter, Koto use)
        => this.JudgeOriginRelation(longer, shorter, use) is OriginJudgment.Refuted or OriginJudgment.Unknown;

    // SPEC 15.6.5: Proven by the solver's premises, or because static, which holds no Loan of the body, outlives a finite Origin or
    // an inferred region; Refuted when a finite Origin must outlive a fixed one; Unknown between fixed Origins. Any other chain with a
    // finite Origin or an inferred region at one end is Proven by the spec and constrains region inference, which ownership analysis
    // does not perform: its Loans follow only the Origins written in a holder's Type, so the longer end's Loans, those of a Borrow
    // or of the body's own inputs, would be lost. Such a chain is Unrepresentable, a located limit.
    internal OriginJudgment JudgeOriginRelation(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (this.ProvesOriginOutlives(longer, shorter, use))
        {
            return OriginJudgment.Proven;
        }

        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (RefutesOriginRelation(longer, shorter))
        {
            return OriginJudgment.Refuted;
        }

        return !FixedOrigin(longer) ? OriginJudgment.Unrepresentable
            : FixedOrigin(shorter) ? OriginJudgment.Unknown
            : longer.Kind == OriginKind.Static && shorter.Kind != OriginKind.Anchor ? OriginJudgment.Proven
            : OriginJudgment.Unrepresentable;
    }

    // SPEC 15.6.5: a fixed Origin is fixed by the body's contract: a signature, parameter or receiver Origin, a projection of one,
    // static, or a meet of them.
    private static bool FixedOrigin(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input;
    }

    private static bool IsWithin(Koto use, Koto declaration)
    {
        for (var current = use; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, declaration))
            {
                return true;
            }
        }

        return false;
    }

    // A local's omitted Origins remain open while its initializer acquires values, whether written in its annotation or
    // in a construction qualifier. Published contracts and subsequent uses of the local never open inference again.
    private OriginDeclaration? OpenInitializerInference(BoundOrigin atom, Koto use)
    {
        for (var node = atom.Binder; node is not null; node = node.Parent)
        {
            if (node is VariableKoto { InitializerKoto: { } initializer } variable && IsWithin(use, initializer) &&
                (IsWithin(atom.Binder!, initializer) || (variable.TypeKoto is { } annotation && IsWithin(atom.Binder!, annotation))))
            {
                if (!this.initializerOrigins.TryGetValue(variable, out var declaration))
                {
                    this.initializerOrigins.Add(variable, declaration = new(variable));
                }

                return declaration.State < 2 ? declaration : null;
            }
        }

        return null;
    }

    private BoundOrigin OriginAtUse(BoundOrigin origin, Koto use)
    {
        var initializers = this.initializerOrigins.Count != 0;
        for (var node = use; node is not null; node = node.Parent)
        {
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State >= 2)
            {
                origin = this.ResolveOrigin(origin, declaration);
            }

            if (initializers && this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.State >= 2)
            {
                origin = this.ResolveOrigin(origin, inferred);
            }
        }

        return origin;
    }

    private bool ProvesOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (OriginOutlives(longer, shorter))
        {
            return true;
        }

        if (shorter.Kind == OriginKind.Inference && this.OpenInitializerInference(shorter, use) is { } pending)
        {
            // SPEC 15.4.4: an Origin omitted in a local annotation or initializer Type expression is
            // inferred from the values fitted to it: each one bounds it, and the relation is proven again once the local's
            // initializer has been bound and the Origin resolved.
            pending.Replacements[shorter] = pending.Replacements.TryGetValue(shorter, out var previous) ? this.Meet(previous, longer) : longer;
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, longer, shorter));
            return true;
        }

        if (!this.originProofPath.Add((longer, shorter)))
        {
            return false;
        }

        try
        {
            // A borrow of a complete local Place is usable only while every stored
            // dependency is valid. Ownership verifies that availability at each use.
            // This is a premise of borrowing the Place, not a relation declared by
            // the annotation currently being checked.
            if (shorter is { Kind: OriginKind.Projection, Binder: VariableKoto variable } &&
                this.symbols.TryGetValue(variable, out var local) && local.Type is { Semantics: SemanticsKind.Owner or SemanticsKind.Ref or SemanticsKind.Uniq } stored &&
                !IsWithin(use, variable) && this.ProvesStoredOriginPremise(stored, longer, use))
            {
                return true;
            }

            if (longer.Kind == OriginKind.Intersection)
            {
                var all = true;
                for (var i = 0; i < longer.Operands.Count; i++)
                {
                    all &= this.ProvesOriginOutlives(longer.Operands[i], shorter, use);
                }

                if (all)
                {
                    return true;
                }
            }

            if (shorter.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < shorter.Operands.Count; i++)
                {
                    if (this.ProvesOriginOutlives(longer, shorter.Operands[i], use))
                    {
                        return true;
                    }
                }
            }

            for (var node = use; node is not null; node = node.Parent)
            {
                if (IsAssociatedRequirement(node) && AssociatedFormationType(node) is { } formation &&
                    this.ProvesTypeOriginPremise(formation, longer, shorter, use))
                {
                    return true;
                }

                if (this.ProvesAssociatedRequirementRelation(node, longer, shorter, use))
                {
                    return true;
                }

                if (node is FunctionKoto or PropertyAccessorKoto)
                {
                    for (var i = 0; i < InputCount(node); i++)
                    {
                        if (BoundInputType(node, i) is { } input && this.ProvesTypeOriginPremise(input, longer, shorter, use))
                        {
                            return true;
                        }
                    }
                }

                // Only declaration contracts are assumptions. A field or local relation
                // being checked must never prove itself.
                if ((node is not (FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto) && !IsAssociatedRequirement(node)) ||
                    !this.originDeclarations.TryGetValue(node, out var declaration) || declaration.State != 3)
                {
                    continue;
                }

                foreach (var relation in declaration.Relations)
                {
                    if (this.ProvesOriginOutlives(longer, relation.Longer, use) && this.ProvesOriginOutlives(relation.Shorter, shorter, use))
                    {
                        return true;
                    }

                    if (relation.Equality && this.ProvesOriginOutlives(longer, relation.Shorter, use) && this.ProvesOriginOutlives(relation.Longer, shorter, use))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            this.originProofPath.Remove((longer, shorter));
        }
    }

    private bool ProvesStoredOriginPremise(BoundType type, BoundOrigin longer, Koto use)
    {
        if (type.Origin is { } origin && this.ProvesOriginOutlives(longer, origin, use))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.ProvesOriginOutlives(longer, type.OriginArguments[i], use))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.ProvesStoredOriginPremise(type.Components[i], longer, use))
            {
                return true;
            }
        }

        return false;
    }

    private bool ProvesTypeOriginPremise(BoundType type, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (type.Symbol is { } symbol && this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                if (this.ProvesOriginOutlives(longer, a, use) && this.ProvesOriginOutlives(b, shorter, use))
                {
                    return true;
                }

                if (relation.Equality && this.ProvesOriginOutlives(longer, b, use) && this.ProvesOriginOutlives(a, shorter, use))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if ((IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer && this.ProvesOriginOutlives(outer, shorter, use))
            {
                if (this.ProvesStoredOriginPremise(inner, longer, use))
                {
                    return true;
                }
            }

            if (this.ProvesTypeOriginPremise(inner, longer, shorter, use))
            {
                return true;
            }
        }

        return false;
    }

    private ConstraintProof CheckTypeOriginRelations(BoundType type, BindingScope scope)
    {
        if (type.Symbol is not { } symbol || !this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) || declaration.State != 3)
        {
            return ConstraintProof.Proven;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            if (!this.ProvesOriginOutlives(a, b, scope.Owner) || (relation.Equality && !this.ProvesOriginOutlives(b, a, scope.Owner)))
            {
                return ConstraintProof.Unknown;
            }
        }

        return ConstraintProof.Proven;
    }

    private bool CheckCallOriginRelations(Koto function, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType)
    {
        if (!this.originDeclarations.TryGetValue(function, out var declaration))
        {
            return true;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = Substitute(relation.Longer);
            var b = Substitute(relation.Shorter);
            if (!this.ProvesOriginOutlives(a, b, use) || (relation.Equality && !this.ProvesOriginOutlives(b, a, use)))
            {
                return false;
            }
        }

        return true;

        BoundOrigin Substitute(BoundOrigin origin)
        {
            if (declaringType?.Symbol is { } owner)
            {
                origin = this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
            }

            return this.SubstituteStoredOrigin(origin, function, origins.AsSpan(0, function.BoundSymbol?.Schema?.Origins.Count ?? 0), inputs.AsSpan(0, InputOriginCount(function)));
        }
    }

    private void BindTypeOriginContracts()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is DeclarationContainerKoto container && this.scopes.TryGetValue(container, out var scope) &&
                this.BeginOriginDeclaration(container, scope) is { } declaration)
            {
                for (var b = 0; b < container.Bases.Count; b++)
                {
                    this.BindType(container.Bases[b], scope);
                }

                this.CompleteOriginDeclaration(declaration);
            }
            else if (AssociatedHead(this.nodes[i]) is OriginApplicationKoto)
            {
                var node = this.nodes[i];
                var associatedScope = this.scopes[node];
                var associatedOrigins = this.BeginOriginDeclaration(node, associatedScope);
                this.BindAssociatedFormation(node, associatedScope);
                this.CompleteOriginDeclaration(associatedOrigins);
            }
        }
    }

    private void ValidateOriginRelations()
    {
        foreach (var declaration in this.originDeclarations.Values)
        {
            if (declaration.State != 3 || declaration.Owner is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto || IsAssociatedRequirement(declaration.Owner))
            {
                continue;
            }

            foreach (var relation in declaration.Relations)
            {
                if (!this.ProvesOriginOutlives(relation.Longer, relation.Shorter, relation.Syntax) ||
                    (relation.Equality && !this.ProvesOriginOutlives(relation.Shorter, relation.Longer, relation.Syntax)))
                {
                    this.Fail(relation.Syntax, BindingFailure.InvalidOrigin);
                }
            }
        }
    }
}
