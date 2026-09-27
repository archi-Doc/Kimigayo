// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundOrigin[]> associatedOrigins = new(ReferenceEqualityComparer.Instance);

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
        => (constraint.RequiredType is { } type && !HasUnconditionalAssociatedFormation(type)) ||
        (constraint.Left is { } left && HasUnsupportedAssociatedIdentity(left)) ||
        (constraint.Right is { } right && HasUnsupportedAssociatedIdentity(right));

    // These complete Types introduce no Origin well-formedness premise. General formation domains stay guarded.
    private static bool HasUnconditionalAssociatedFormation(BoundType type)
    {
        if (!type.CarriesOrigin)
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            return type.Origin is not null && type.Components[0].Kind == BoundTypeKind.Primitive;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!HasUnconditionalAssociatedFormation(type.Components[i]))
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
            return Fail(clause, BindingFailure.InvalidAssociatedType);
        }

        var self = this.SelfType(owner);
        var associated = this.FindAssociated(self, scope, name, qualifier, clause);
        if (associated is null || this.AssociatedParameters(associated.Declaration).Length != application.ArgumentNodes.Count)
        {
            return Fail(clause, BindingFailure.InvalidAssociatedType);
        }

        var projection = this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self], originArguments: this.AssociatedParameters(associated.Declaration));
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
                    Fail(node, BindingFailure.InvalidOrigin);
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
                        Fail(node, BindingFailure.Duplicate);
                    }
                }

                scope.Origins ??= new(StringComparer.Ordinal);
                if (!scope.Origins.TryAdd(name.IdentifierName, origin))
                {
                    Fail(node, BindingFailure.Duplicate);
                }

                Complete(syntax, BoundType.Unit);
                syntax.BoundOrigin = origin;
            }

            // Formation domains and their implementation implication checks are a separate unit.
            if (OriginClauses.Get(node).Count != 0 || node is SyntaxFormKoto { Operands.Length: > 1 } || node is IsKoto { FormationType: not null })
            {
                Fail(node, BindingFailure.Unsupported);
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
            return Fail(application, BindingFailure.InvalidAssociatedType);
        }

        var arguments = this.originScratch.Rent(application.ArgumentNodes.Count);
        try
        {
            for (var i = 0; i < application.ArgumentNodes.Count; i++)
            {
                if (this.BindOrigin(application.ArgumentNodes[i], scope) is not { } origin)
                {
                    return Fail(application, BindingFailure.InvalidOrigin);
                }

                arguments[i] = origin;
            }

            projection = this.WithOrigins(projection, null, arguments.AsSpan(0, application.ArgumentNodes.Count));
            application.BoundSymbol = target.BoundSymbol = associated;
            Complete(target, projection);
            return this.bindingConstraintTypes ? projection : this.ContractType(projection, scope);
        }
        finally
        {
            this.originScratch.Return(arguments, clearArray: true);
        }
    }
}
