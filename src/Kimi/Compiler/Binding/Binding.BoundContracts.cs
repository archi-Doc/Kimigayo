// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, BindingSymbol> boundContracts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundType, BoundType> collisionSubstitutions = new(ReferenceEqualityComparer.Instance);

    private BindingSymbol? BindContractReference(Koto syntax, BindingSymbol declaration, BindingScope scope)
    {
        if (declaration.Schema is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 })
        {
            return declaration;
        }

        var context = this.TypeContext(syntax, scope) with { SuppressOuter = true };
        // SPEC 8.4: a Contract's own Type parameters are supplied at the reference, as for a constructed struct Type.
        var unwrapped = syntax;
        while (unwrapped is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            unwrapped = inner;
        }

        var container = (DeclarationContainerKoto)declaration.Declaration;
        BoundType? own = null;
        if (container.GenericParameterNodes.Count != 0)
        {
            if (unwrapped is not GenericsKoto generic || generic.TypeArguments.Count != container.GenericParameterNodes.Count)
            {
                this.Fail(syntax, BindingFailure.TypeMismatch);
                return null;
            }

            generic.Identifier!.BoundSymbol = declaration;
            generic.Identifier.BindingState = BindingState.Resolved;
            own = this.BindTypeList(generic, generic.TypeArguments, scope, context.Nested, BoundTypeKind.Constructed, declaration);
            if (own is null)
            {
                return null;
            }
        }

        var reference = this.BindContainerReference(syntax, declaration, scope, context, own is null ? [] : (BoundType[])own.Components);
        reference = reference is null ? null : this.CompleteOrigins(reference, syntax as TypeSemanticsKoto, syntax, scope, context);
        if (reference is null || reference.OriginArguments.Count != declaration.Schema.Origins.Count || reference.OriginArguments.Contains(null!))
        {
            this.Fail(syntax, BindingFailure.InvalidOrigin);
            return null;
        }

        if (!ReferenceEquals(unwrapped, syntax))
        {
            Complete(unwrapped, reference);
        }

        Complete(syntax, reference);
        return this.BoundContractReference(reference);
    }

    private BindingSymbol BoundContractReference(BoundType reference)
    {
        if (this.boundContracts.TryGetValue(reference, out var cached) && cached.Contract!.State == 2)
        {
            return cached;
        }

        var declaration = reference.Symbol!;
        var bound = cached ?? new BindingSymbol(declaration.Name, declaration.Kind, declaration.Declaration, declaration.Scope);
        bound.Type = reference;
        bound.Schema = declaration.Schema;
        bound.Scope = declaration.Scope;
        var shape = bound.Contract ??= new(bound);
        shape.State = 2;
        shape.RequirementStorage.Clear();
        shape.ClauseStorage.Clear();
        shape.Seen.Clear();
        foreach (var members in shape.MembersByName.Values)
        {
            members.Clear();
        }

        shape.AncestorStorage.Clear();
        shape.AssociatedStorage.Clear();
        if (declaration.Contract is { } original)
        {
            // Indexed loops: a warm rebind rebuilds every bound reference without allocating.
            shape.RequirementStorage.AddRange(original.RequirementStorage);
            shape.ClauseStorage.AddRange(original.ClauseStorage);
            for (var i = 0; i < original.RequirementStorage.Count; i++)
            {
                var requirement = original.RequirementStorage[i];
                shape.Seen.Add(requirement);
                if (!shape.MembersByName.TryGetValue(requirement.Name, out var members))
                {
                    shape.MembersByName.Add(requirement.Name, members = new());
                }

                members.Add(requirement);
            }

            // SPEC 8.4.2, 8.4.9: a bound reference keeps the declaration's associated identities, and its ancestors are the
            // parents' bound references under this reference's substitution (Indexable<Key> of UniqIndexable<isize> is
            // Indexable<isize>); an ancestor without Type arguments keeps its declaration identity.
            for (var i = 0; i < original.AssociatedStorage.Count; i++)
            {
                if (shape.Seen.Add(original.AssociatedStorage[i]))
                {
                    shape.AssociatedStorage.Add(original.AssociatedStorage[i]);
                }
            }

            for (var i = 0; i < original.AncestorStorage.Count; i++)
            {
                var ancestor = original.AncestorStorage[i];
                var boundAncestor = ancestor.Type is { Components.Count: > 0 } ancestorReference && !ReferenceEquals(ancestorReference.Symbol, ancestor) &&
                    this.StoredType(ancestorReference, reference) is { } substituted && !ReferenceEquals(substituted, ancestorReference)
                    ? this.BoundContractReference(substituted) : ancestor;
                if (shape.Seen.Add(boundAncestor))
                {
                    shape.AncestorStorage.Add(boundAncestor);
                }
            }

            shape.AncestorStorage.Sort(static (left, right) => left.Contract!.Ancestors.Count.CompareTo(right.Contract!.Ancestors.Count));
        }

        if (cached is null)
        {
            this.boundContracts.Add(reference, bound);
        }

        return bound;
    }

    private BoundType ApplyContractEnvironment(BoundType type, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath?.Contract is { Type: { } reference } contract && !ReferenceEquals(reference.Symbol, contract))
            {
                return this.SubstituteContractReference(type, contract);
            }
        }

        return type;
    }

    // SPEC 8.4.2: a bound reference substitutes its own Type parameters and, through its bound ancestors, each
    // inherited requirement's parameters.
    private BoundType SubstituteContractReference(BoundType type, BindingSymbol contract)
    {
        if (contract.Type is not { } reference || ReferenceEquals(reference.Symbol, contract))
        {
            return type;
        }

        var result = this.StoredType(type, reference) ?? type;
        if (contract.Contract is { } shape)
        {
            for (var i = 0; i < shape.Ancestors.Count; i++)
            {
                var ancestor = shape.Ancestors[i];
                if (ancestor.Type is { } ancestorReference && !ReferenceEquals(ancestorReference.Symbol, ancestor))
                {
                    result = this.StoredType(result, ancestorReference) ?? result;
                }
            }
        }

        return result;
    }

    private bool ContractBindingsMayCollide(BindingSymbol a, BindingSymbol b, BindingScope scope)
    {
        if (!ReferenceEquals(a.Declaration, b.Declaration))
        {
            return false;
        }

        return this.MayUnify(this.ContractType(a.Type!, scope), this.ContractType(b.Type!, scope));
    }

    // SPEC 8.4.9.1 steps 2-4, shared with the parameter acquisition shape (SPEC 7.3.1): Type parameters are variables with an
    // occurs check, residual terms unify, and only a fixed-structure mismatch proves two Types apart. Unification calls back into
    // nothing, so one scratch map serves every check (a warm bind allocates none).
    private bool MayUnify(BoundType left, BoundType right)
    {
        this.collisionSubstitutions.Clear();
        return this.UnifyTypes(left, right);
    }

    private BoundType ResolveSubstitution(BoundType type)
    {
        while (this.collisionSubstitutions.TryGetValue(type, out var target))
        {
            type = target;
        }

        return type;
    }

    private bool ContainsVariable(BoundType value, BoundType variable)
    {
        value = this.ResolveSubstitution(value);
        if (ReferenceEquals(value, variable))
        {
            return true;
        }

        for (var i = 0; i < value.Components.Count; i++)
        {
            if (this.ContainsVariable(value.Components[i], variable))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFreeTerm(BoundType value)
    {
        value = this.ResolveSubstitution(value);
        if (value.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
        {
            return false;
        }

        for (var i = 0; i < value.Components.Count; i++)
        {
            if (!this.IsFreeTerm(value.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool UnifyTypes(BoundType left, BoundType right)
    {
        left = this.ResolveSubstitution(left);
        right = this.ResolveSubstitution(right);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Kind == BoundTypeKind.Parameter || right.Kind == BoundTypeKind.Parameter)
        {
            var variable = left.Kind == BoundTypeKind.Parameter ? left : right;
            var value = ReferenceEquals(variable, left) ? right : left;
            if (this.ContainsVariable(value, variable))
            {
                // An occurs check proves inequality only for free constructors.
                return !this.IsFreeTerm(value);
            }

            this.collisionSubstitutions.Add(variable, value);
            return true;
        }

        if (left.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication ||
            right.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
        {
            return true; // Residual, non-free terms are not injective constructors.
        }

        if (left.Kind != right.Kind || left.Symbol != right.Symbol || left.Semantics != right.Semantics || left.Components.Count != right.Components.Count ||
            (left.Kind == BoundTypeKind.Primitive && left.Name != right.Name) ||
            (left.Kind == BoundTypeKind.FixedArray && left.LengthExpression is null && right.LengthExpression is null && left.Length != right.Length))
        {
            return false;
        }

        for (var i = 0; i < left.Components.Count; i++)
        {
            if (!this.UnifyTypes(left.Components[i], right.Components[i]))
            {
                return false;
            }
        }

        return true; // Origin/order and unresolved length equalities remain possible.
    }
}
