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

    private static bool HasOriginDependentAssociatedIdentity(BoundConstraint constraint)
        => constraint.RequiredType?.CarriesOrigin == true ||
        (constraint.Left is { } left && HasOriginDependentAssociatedIdentity(left)) ||
        (constraint.Right is { } right && HasOriginDependentAssociatedIdentity(right));

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
                    if (enclosing.Origins?.ContainsKey(name.IdentifierName) == true || enclosing.Values.ContainsKey(name.IdentifierName))
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
