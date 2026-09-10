// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Associated), AssociatedBinding> associatedBindings = new();
    private readonly HashSet<BoundType> normalizingAssociated = new(ReferenceEqualityComparer.Instance);

    private static BoundType? EnclosingContractSelf(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is ContractKoto)
            {
                return current.Owner.BoundSymbol!.Type;
            }
        }

        return null;
    }

    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use)
    {
        BindingSymbol? found = null;
        var ambiguous = false;
        if (qualifier?.Contract is { } qualified)
        {
            Search(qualified);
        }
        else
        {
            if (type.Symbol?.Contract is { } own)
            {
                Search(own);
            }

            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, type) && fact.Contract?.Contract is { } shape)
                    {
                        Search(shape);
                    }
                }
            }

            if (type.Symbol is { } symbol && this.conformancesByType.TryGetValue(symbol, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    Search(list[i].Contract.Contract!);
                }
            }
        }

        if (ambiguous)
        {
            Fail(use, BindingFailure.Ambiguous);
            return null;
        }

        return found;

        void Search(BoundContract shape)
        {
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                var associated = shape.AssociatedTypes[i];
                if (associated.Name == name)
                {
                    ambiguous |= found is not null && !ReferenceEquals(found, associated);
                    found = associated;
                }
            }
        }
    }

    private BoundType? BindAssociatedProjection(MemberAccessKoto syntax, BindingScope scope)
    {
        if (TypeSpelling(syntax.Right) is not { } name)
        {
            return null;
        }

        Koto receiver = syntax.Left;
        BindingSymbol? qualifier = null;
        if (receiver is MemberAccessKoto qualified && this.ProjectionQualifier(qualified, scope, out var baseSyntax) is { Declaration: ContractKoto } contract)
        {
            qualifier = contract;
            receiver = baseSyntax!;
            qualified.Right.BoundSymbol = contract;
            Complete(qualified.Right, BoundType.Unit);
        }

        var receiverSymbol = this.TypeName(receiver, scope, false);
        if (receiverSymbol?.Kind == BindingSymbolKind.Container || (receiverSymbol?.Declaration is ContractKoto && TypeSpelling(receiver) != "Self"))
        {
            return null;
        }

        var type = this.BindType(receiver, scope);
        if (type is null || type.Semantics != SemanticsKind.Owner)
        {
            return null;
        }

        var associated = this.FindAssociated(type, scope, name, qualifier, syntax);
        if (associated is null)
        {
            return null;
        }

        var evidence = qualifier ?? associated.Scope.Owner.BoundSymbol!;
        this.projectionUses.Add((syntax, type, evidence));
        if (!this.bindingConstraintTypes && this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: evidence)), scope) == ConstraintProof.Proven)
        {
            // Expand only a referenced receiver's proved contract. This avoids eagerly generating
            // an unbounded chain for recursive associated requirements such as E is SelfContract.
            this.AddContractPremises(evidence.Contract!, type, scope);
        }

        var projection = this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [type]);
        syntax.BoundSymbol = associated;
        syntax.Right.BoundSymbol = associated;
        Complete(syntax.Right, projection);
        if (!ReferenceEquals(receiver, syntax.Left))
        {
            Complete(syntax.Left, type);
        }

        return this.bindingConstraintTypes ? projection : this.ContractType(projection, scope);
    }

    private BindingSymbol? ProjectionQualifier(MemberAccessKoto syntax, BindingScope scope, out Koto? receiver)
    {
        if (syntax.Left is MemberAccessKoto left && this.ProjectionQualifier(left, scope, out receiver) is { Kind: BindingSymbolKind.Container } prefix && this.scopes.TryGetValue(prefix.Declaration, out var members) && TypeSpelling(syntax.Right) is { } name && members.Types.TryGetValue(name, out var target) && this.Accessible(target, scope))
        {
            syntax.Right.BoundSymbol = target;
            Complete(syntax.Right, BoundType.Unit);
            Complete(syntax.Left, BoundType.Unit);
            return target;
        }

        var symbol = this.TypeName(syntax.Right, scope, false);
        receiver = syntax.Left;
        if (symbol?.Kind == BindingSymbolKind.Container || symbol?.Declaration is ContractKoto)
        {
            syntax.Right.BoundSymbol = symbol;
            Complete(syntax.Right, BoundType.Unit);
        }

        return symbol?.Kind == BindingSymbolKind.Container || symbol?.Declaration is ContractKoto ? symbol : null;
    }

    private void BindAssociatedSpecification(IsKoto clause, BindingScope scope)
    {
        var self = this.SelfType(scope.Owner.BoundSymbol!);
        var name = clause.Left as IdentifierNameKoto;
        BindingSymbol? qualifier = null;
        if (clause.Left is MemberAccessKoto member)
        {
            name = member.Right as IdentifierNameKoto;
            qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier?.Declaration is not ContractKoto)
            {
                Fail(clause, BindingFailure.InvalidAssociatedType);
                return;
            }

            member.Left.BoundSymbol = qualifier;
            Complete(member.Left, BoundType.Unit);
        }

        var associated = name is null ? null : this.FindAssociated(self, scope, name.IdentifierName, qualifier, clause);
        if (associated is null || !this.conformances.TryGetValue((self.Symbol!, qualifier ?? associated.Scope.Owner.BoundSymbol!), out var conformance) || !conformance.Active)
        {
            Fail(clause, BindingFailure.InvalidAssociatedType);
            return;
        }

        var projection = this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self]);
        clause.BoundSymbol = associated;
        clause.Left.BoundSymbol = associated;
        name!.BoundSymbol = associated;
        Complete(name, projection);
        Complete(clause.Left, projection);
        clause.BoundConstraint = this.BindRequirement(clause.Right, projection, false, scope);
        Complete(clause, BoundType.Boolean);
    }

    private void PrepareAssociatedBindings()
    {
        for (var i = 0; i < this.activeConformances.Count; i++)
        {
            var conformance = this.activeConformances[i];
            var shape = conformance.Contract.Contract!;
            for (var j = 0; j < shape.AssociatedTypes.Count; j++)
            {
                var key = (conformance.Type, shape.AssociatedTypes[j]);
                if (!this.associatedBindings.ContainsKey(key))
                {
                    this.associatedBindings.Add(key, new());
                }
            }

            var self = this.SelfType(conformance.Type);
            for (var j = 0; j < shape.ClauseStorage.Count; j++)
            {
                if (shape.ClauseStorage[j].BoundConstraint is { } constraint)
                {
                    Collect(constraint, self);
                }
            }
        }

        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is IsKoto { IsAssociatedConstraint: true, Parent: DeclarationContainerKoto container, BoundConstraint: { } constraint } && container is StructKoto or EnumKoto)
            {
                Collect(constraint, this.SelfType(container.BoundSymbol!));
            }
        }

        for (var i = 0; i < this.activeConformances.Count; i++)
        {
            var conformance = this.activeConformances[i];
            var shape = conformance.Contract.Contract!;
            for (var j = 0; j < shape.AssociatedTypes.Count; j++)
            {
                var associated = shape.AssociatedTypes[j];
                var result = this.ResolveAssociated(conformance.Type, associated);
                if (result is not null)
                {
                    conformance.AssociatedStorage[associated] = result;
                }
            }
        }

        void Collect(BoundConstraint constraint, BoundType self)
        {
            if (constraint.Kind == ConstraintKind.And)
            {
                Collect(constraint.Left!, self);
                Collect(constraint.Right!, self);
            }
            else if (constraint is { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.AssociatedProjection, RequiredType: { } required } && this.associatedBindings.TryGetValue((self.Symbol!, constraint.Subject.Symbol!), out var binding))
            {
                if (!binding.Candidates.Contains(required))
                {
                    binding.Candidates.Add(required);
                }
            }
        }
    }

    private BoundType? ResolveAssociated(BindingSymbol owner, BindingSymbol associated)
    {
        if (!this.associatedBindings.TryGetValue((owner, associated), out var binding))
        {
            return null;
        }

        if (binding.State != 0)
        {
            return binding.State == 2 ? binding.Result : null;
        }

        binding.State = 1;
        var scope = this.scopes[owner.Declaration];
        var self = this.SelfType(owner);
        BoundType? result = null;
        var valid = binding.Candidates.Count != 0;
        for (var i = 0; i < binding.Candidates.Count; i++)
        {
            var type = this.ContractType(binding.Candidates[i], scope, self);
            valid &= this.IsAssociatedCore(type, scope) && (result is null || ReferenceEquals(result, type));
            result = type;
        }

        binding.State = valid ? (byte)2 : (byte)3;
        binding.Result = valid ? result : null;
        return binding.Result;
    }

    private bool IsAssociatedCore(BoundType type, BindingScope scope)
    {
        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (this.HasSemanticsRole(type, SemanticsMask.Owner, scope))
            {
                return true;
            }

            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind == ConstraintKind.TypeIdentity && ReferenceEquals(fact.Subject, type) && fact.RequiredType is { Kind: not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) } required)
                    {
                        return this.IsAssociatedCore(required, scope);
                    }
                }
            }

            return false;
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection && type.Components[0].Symbol?.Declaration is StructKoto or EnumKoto)
        {
            return false;
        }

        return type.Kind != BoundTypeKind.TargetProjection || this.HasValueRole(type, scope, false);
    }

    /// <summary>Substitutes Contract Self and normalizes explicit associated identities without member inference.</summary>
    private BoundType ContractType(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
    {
        if (type.Symbol?.Declaration is ContractKoto && type.Kind == BoundTypeKind.Nominal)
        {
            return self ?? EnclosingContractSelf(scope) ?? type;
        }

        var count = type.Components.Count;
        var components = count == 0 ? null : this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components![i] = this.ContractType(type.Components[i], scope, self, normalize);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression) : type;
            if (!normalize || result.Kind != BoundTypeKind.AssociatedProjection || !this.normalizingAssociated.Add(result))
            {
                return result;
            }

            try
            {
                var receiver = result.Components[0];
                if (receiver.Symbol?.Declaration is StructKoto or EnumKoto && this.ResolveAssociated(receiver.Symbol, result.Symbol!) is { } fixedType)
                {
                    return this.StoredType(fixedType, receiver) ?? result;
                }

                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact.Kind == ConstraintKind.TypeIdentity && ReferenceEquals(fact.Subject, result) && fact.RequiredType is { } required)
                        {
                            return this.ContractType(required, scope, self);
                        }
                    }
                }

                return result;
            }
            finally
            {
                this.normalizingAssociated.Remove(result);
            }
        }
        finally
        {
            if (components is not null)
            {
                this.typeScratch.Return(components, clearArray: true);
            }
        }
    }

    private sealed class AssociatedBinding
    {
        internal List<BoundType> Candidates { get; } = new();

        internal BoundType? Result { get; set; }

        internal byte State { get; set; }
    }
}
