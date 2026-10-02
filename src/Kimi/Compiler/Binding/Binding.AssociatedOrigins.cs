// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundOrigin[]> associatedOrigins = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto Use, BoundType Projection, BindingSymbol Contract)> associatedApplications = new();

    private static bool IsAssociatedRequirement(Koto node)
        => node.Parent is ContractKoto && AssociatedHead(node) is OriginApplicationKoto application &&
            UnwrapAssociatedHead(application.Type) is IdentifierNameKoto or TypeSemanticsKoto { Type: null };

    private static Koto? AssociatedHead(Koto declaration)
    {
        var head = declaration switch
        {
            IsKoto { IsAssociatedConstraint: true } clause => clause.Left,
            SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: > 0 } syntax => syntax.Operands[0],
            _ => null,
        };
        return UnwrapAssociatedHead(head);
    }

    private static Koto? UnwrapAssociatedHead(Koto? head)
    {
        while (head is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            head = inner;
        }

        return head;
    }

    private static bool HasUnsupportedAssociatedIdentity(BoundConstraint constraint)
        => (constraint.RequiredType is { } type && !HasSupportedAssociatedFormation(type)) ||
        (constraint.Left is { } left && HasUnsupportedAssociatedIdentity(left)) ||
        (constraint.Right is { } right && HasUnsupportedAssociatedIdentity(right));

    // Borrow layers publish their formation premises, including complete Contract Type parameters.
    private static bool HasSupportedAssociatedFormation(BoundType type)
    {
        if (!type.CarriesOrigin)
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            return type.Origin is not null && (type.Components[0].Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.Primitive or BoundTypeKind.Semantics or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Nominal or BoundTypeKind.Constructed or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice) &&
                HasSupportedAssociatedFormation(type.Components[0]);
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice or BoundTypeKind.AssociatedProjection ||
            (type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol?.Declaration is StructKoto or EnumKoto))
        {
            // A forwarded family, `I.(C).Item(a)`, applies the specification's own parameters to another family; its
            // formation conditions are that family's, checked at each application (SPEC 8.4.3).
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!HasSupportedAssociatedFormation(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool AssociatedIdentityMatches(BoundType? pattern, BoundType type)
        => ReferenceEquals(pattern, type) ||
        (pattern is { Kind: BoundTypeKind.AssociatedProjection, OriginArguments.Count: > 0 } && type.Kind == BoundTypeKind.AssociatedProjection &&
         ReferenceEquals(pattern.Symbol, type.Symbol) && ReferenceEquals(pattern.Components[0], type.Components[0]) &&
         pattern.OriginArguments.Count == type.OriginArguments.Count && UniversalAssociatedArguments(pattern));

    private static bool UniversalAssociatedArguments(BoundType type)
    {
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i] is not { Kind: OriginKind.Parameter } origin || !ReferenceEquals(origin.Binder, type.Symbol!.Declaration) || origin.Slot != i)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 8.4.3, 8.7: whether a fact states `type`: its own subject, or a universal family pattern that holds at every
    // application, including the Origins of `type`. `required` is the fact's Type at those Origins.
    private bool FactStates(BoundConstraint fact, BoundType type, out BoundType? required)
    {
        required = fact.RequiredType;
        if (ReferenceEquals(fact.Subject, type))
        {
            return true;
        }

        if (!AssociatedIdentityMatches(fact.Subject, type))
        {
            return false;
        }

        if (required is not null)
        {
            required = this.SubstituteStoredOrigins(required, type.Symbol!.Declaration, (BoundOrigin[])type.OriginArguments);
        }

        return true;
    }

    // A universal family capability is applied at the same Origins as its subject projection.
    private BindingSymbol AppliedAssociatedContract(BoundConstraint fact, BoundType subject)
    {
        var contract = fact.Contract!;
        return IsBoundContractReference(contract) &&
            fact.Subject is { Kind: BoundTypeKind.AssociatedProjection, OriginArguments.Count: > 0 } pattern &&
            subject.Kind == BoundTypeKind.AssociatedProjection && subject.OriginArguments.Count != 0
            ? this.BoundContractReference(this.SubstituteStoredOrigins(contract.Type!, pattern.Symbol!.Declaration, (BoundOrigin[])subject.OriginArguments))
            : contract;
    }

    private ReadOnlySpan<BoundOrigin> AssociatedParameters(Koto declaration)
        => this.associatedOrigins.TryGetValue(declaration, out var origins) && AssociatedHead(declaration) is OriginApplicationKoto ? origins : [];

    private BoundConstraint CanonicalAssociatedOrigins(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundOrigin> parameters)
    {
        var required = constraint.RequiredType is { } type ? this.SubstituteStoredOrigins(type, binder, parameters) : null;
        var left = constraint.Left is { } a ? this.CanonicalAssociatedOrigins(a, binder, parameters) : null;
        var right = constraint.Right is { } b ? this.CanonicalAssociatedOrigins(b, binder, parameters) : null;
        return ReferenceEquals(required, constraint.RequiredType) && ReferenceEquals(left, constraint.Left) && ReferenceEquals(right, constraint.Right)
            ? constraint : this.InternConstraint(new(constraint.Kind, constraint.Subject, required, constraint.Contract, constraint.Mask, left, right));
    }

    private BoundType? BindAssociatedRefinement(IsKoto clause, OriginApplicationKoto application, MemberAccessKoto member, BindingScope scope)
    {
        var owner = scope.Owner.BoundSymbol!;
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier?.Declaration is not ContractKoto || ReferenceEquals(qualifier, owner) || !IsRefinement(owner, qualifier) || TypeSpelling(member.Right) is not { } name)
        {
            return this.Fail(clause, BindingFailure.InvalidAssociatedType);
        }

        var self = this.ContractSelfType(owner);
        var associated = this.FindAssociated(self, scope, name, qualifier, clause, out var reference);
        if (associated is null || this.AssociatedParameters(associated.Declaration).Length != application.ArgumentNodes.Count)
        {
            return this.Fail(clause, BindingFailure.InvalidAssociatedType);
        }

        var projection = this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self, this.ProjectionContract(associated, reference ?? qualifier)], originArguments: this.AssociatedParameters(associated.Declaration));
        clause.BoundSymbol = member.BoundSymbol = member.Right.BoundSymbol = application.BoundSymbol = associated;
        member.Left.BoundSymbol = qualifier;
        Complete(member.Left, BoundType.Unit);
        Complete(member.Right, projection);
        Complete(member, projection);
        Complete(application, projection);
        return projection;
    }

    private void PrepareAssociatedOrigins()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            var node = this.nodes[n];
            if (AssociatedHead(node) is not OriginApplicationKoto application)
            {
                continue;
            }

            var scope = this.scopes[node];
            if (!this.associatedOrigins.TryGetValue(node, out var parameters) || parameters.Length != application.ArgumentNodes.Count)
            {
                this.associatedOrigins[node] = parameters = new BoundOrigin[application.ArgumentNodes.Count];
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                var syntax = application.ArgumentNodes[i];
                if (syntax is not IdentifierNameKoto { IdentifierName: not ("_" or "static") } name)
                {
                    this.AddPrerequisite(node, syntax); // A parameter the parser rejected explains the failure.
                    this.Fail(node, BindingFailure.InvalidOrigin);
                    continue;
                }

                var origin = parameters[i] = this.OriginAtom(node, OriginKind.Parameter, i, name.IdentifierName);
                for (var enclosing = scope.Parent; enclosing is not null; enclosing = enclosing.Parent)
                {
                    if (enclosing.Origins?.ContainsKey(name.IdentifierName) == true ||
                        (enclosing.Values.TryGetValue(name.IdentifierName, out var value) && value.Kind is BindingSymbolKind.Parameter or BindingSymbolKind.Local or BindingSymbolKind.Capture) ||
                        enclosing.OriginSets?.ContainsKey(name.IdentifierName) == true ||
                        this.originDeclarations.GetValueOrDefault(enclosing.Owner)?.Sets.ContainsKey(name.IdentifierName) == true)
                    {
                        this.Fail(node, BindingFailure.Duplicate);
                    }
                }

                scope.Origins ??= new(StringComparer.Ordinal);
                if (!scope.Origins.TryAdd(name.IdentifierName, origin))
                {
                    this.Fail(node, BindingFailure.Duplicate);
                }

                Complete(syntax, BoundType.Unit);
                syntax.BoundOrigin = origin;
            }
        }
    }

    private BoundType? BindAssociatedApplication(OriginApplicationKoto application, BindingScope scope)
    {
        var target = UnwrapAssociatedHead(application.Type)!;
        var projection = target is MemberAccessKoto member ? this.BindAssociatedProjection(member, scope, applyingOrigins: true) : this.BindTypeStructure(target, scope, this.TypeContext(application, scope), applyingOrigins: true);
        if (projection?.Kind != BoundTypeKind.AssociatedProjection || projection.Symbol is not { } associated ||
            this.AssociatedParameters(associated.Declaration).Length != application.ArgumentNodes.Count || application.ArgumentNodes.Count == 0)
        {
            return this.Fail(application, BindingFailure.InvalidAssociatedType);
        }

        var arguments = this.originScratch.Rent(application.ArgumentNodes.Count);
        try
        {
            for (var i = 0; i < application.ArgumentNodes.Count; i++)
            {
                if (this.BindOrigin(application.ArgumentNodes[i], scope) is not { } origin)
                {
                    this.AddPrerequisite(application, application.ArgumentNodes[i]);
                    return this.Fail(application, BindingFailure.InvalidOrigin);
                }

                arguments[i] = origin;
            }

            projection = this.WithOrigins(projection, null, arguments.AsSpan(0, application.ArgumentNodes.Count));
            var contract = associated.Scope.Owner.BoundSymbol!;
            for (var i = this.projectionUses.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(this.projectionUses[i].Use, target))
                {
                    contract = this.projectionUses[i].Contract;
                    break;
                }
            }

            this.associatedApplications.Add((application, projection, contract));
            application.BoundSymbol = target.BoundSymbol = associated;
            Complete(target, projection);
            return this.bindingConstraintTypes ? projection : this.ContractType(projection, scope);
        }
        finally
        {
            this.originScratch.Return(arguments, clearArray: true);
        }
    }

    private void ValidateAssociatedApplications()
    {
        foreach (var node in this.nodes)
        {
            if (AssociatedHead(node) is OriginApplicationKoto && !IsAssociatedRequirement(node) &&
                node.BoundSymbol is { Kind: BindingSymbolKind.AssociatedType } associated && AssociatedFormationType(node) is { } definition)
            {
                definition = this.SubstituteStoredOrigins(definition, associated.Declaration, this.AssociatedParameters(node));
                if (!this.CheckAssociatedFormation(definition, node))
                {
                    this.Fail(node, BindingFailure.InvalidOrigin);
                }
            }
        }

        foreach (var (use, projection, contract) in this.associatedApplications)
        {
            var declaration = projection.Symbol!.Declaration;
            var formation = AssociatedFormationType(declaration);
            if (formation is not null)
            {
                formation = this.SubstituteStoredOrigins(formation, declaration, (BoundOrigin[])projection.OriginArguments);
                formation = this.SubstituteContractReference(formation, contract);
                formation = this.StoredType(formation, projection.Components[0]) ?? formation;
                formation = this.ContractType(formation, this.ConstraintScope(use), projection.Components[0]);
            }

            if (this.CheckTypeOriginRelations(projection, this.ConstraintScope(use)) != ConstraintProof.Proven ||
                (formation is not null && !this.CheckAssociatedFormation(formation, use)))
            {
                this.Fail(use, BindingFailure.InvalidOrigin);
            }
        }
    }

    private bool ProvesAssociatedRequirementRelation(Koto node, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (AssociatedHead(node) is not OriginApplicationKoto || node.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } associated ||
            ReferenceEquals(associated.Declaration, node))
        {
            return false;
        }

        var arguments = this.AssociatedParameters(node);
        if (this.InheritedAssociatedFormation(node) is { } formation &&
            this.ProvesTypeOriginPremise(formation, longer, shorter, use))
        {
            return true;
        }

        if (!this.originDeclarations.TryGetValue(associated.Declaration, out var requirement) || requirement.State != 3)
        {
            return false;
        }

        foreach (var relation in requirement.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, associated.Declaration, arguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, associated.Declaration, arguments);
            if ((this.ProvesOriginOutlives(longer, a, use) && this.ProvesOriginOutlives(b, shorter, use)) ||
                (relation.Equality && this.ProvesOriginOutlives(longer, b, use) && this.ProvesOriginOutlives(a, shorter, use)))
            {
                return true;
            }
        }

        return false;
    }
}
