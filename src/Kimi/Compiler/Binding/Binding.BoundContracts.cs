// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, BindingSymbol> boundContracts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CollisionTerm, CollisionTerm> collisionSubstitutions = new();

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
    private bool MayUnify(BoundType left, BoundType right, FunctionKoto? leftBinder = null, FunctionKoto? rightBinder = null, bool shared = false)
    {
        this.collisionSubstitutions.Clear();
        this.collisionLeft = leftBinder;
        this.collisionRight = rightBinder;
        this.collisionShared = shared;
        this.shapeSharedSlots.Clear();
        return this.UnifyTypes(new(left, leftBinder), new(right, rightBinder));
    }

    // A candidate identity qualifies only its inferred variables; fixed enclosing variables keep
    // one identity. No synthetic Type tree or declaration is needed for candidate independence.
    private readonly record struct CollisionTerm(BoundType Type, FunctionKoto? Binder);

    private CollisionTerm NormalizeCollisionTerm(CollisionTerm term)
    {
        if (term.Type.Kind != BoundTypeKind.Parameter)
        {
            return term;
        }

        if (term.Binder is not { } binder || term.Type.Symbol is not { } symbol ||
            !ReferenceEquals(symbol.Scope.Owner, CallSlotOwner(binder)) || symbol.Slot >= CallOwnSlots(binder).Count)
        {
            return new(term.Type, null);
        }

        if (this.collisionShared && this.collisionLeft is { } left && this.collisionRight is { } right && PermittedSlotSharing(left, right))
        {
            if (!this.shapeSharedSlots.Contains(symbol.Slot))
            {
                this.shapeSharedSlots.Add(symbol.Slot);
            }

            return new(CallOwnSlots(left)[symbol.Slot].BoundSymbol!.WholeType!, left);
        }

        return term;
    }

    private CollisionTerm ResolveSubstitution(CollisionTerm term)
    {
        term = this.NormalizeCollisionTerm(term);
        while (this.collisionSubstitutions.TryGetValue(term, out var target))
        {
            term = target;
        }

        return term;
    }

    private bool ContainsVariable(CollisionTerm value, CollisionTerm variable)
    {
        value = this.ResolveSubstitution(value);
        if (value == variable)
        {
            return true;
        }

        for (var i = 0; i < value.Type.Components.Count; i++)
        {
            if (this.ContainsVariable(new(value.Type.Components[i], value.Binder), variable))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFreeTerm(CollisionTerm value)
    {
        value = this.ResolveSubstitution(value);
        if (value.Type.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation)
        {
            return false;
        }

        for (var i = 0; i < value.Type.Components.Count; i++)
        {
            if (!this.IsFreeTerm(new(value.Type.Components[i], value.Binder)))
            {
                return false;
            }
        }

        return true;
    }

    private bool UnifyTypes(CollisionTerm left, CollisionTerm right)
    {
        left = this.ResolveSubstitution(left);
        right = this.ResolveSubstitution(right);
        if (left == right)
        {
            return true;
        }

        var a = left.Type;
        var b = right.Type;
        if (a.Kind == BoundTypeKind.Parameter || b.Kind == BoundTypeKind.Parameter)
        {
            var variable = a.Kind == BoundTypeKind.Parameter ? left : right;
            var value = variable == left ? right : left;
            if (this.ContainsVariable(value, variable))
            {
                return !this.IsFreeTerm(value); // An occurs check proves inequality only for free constructors.
            }

            this.collisionSubstitutions.Add(variable, value);
            return true;
        }

        if (a.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
            b.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation)
        {
            return true; // Residual terms are not injective constructors.
        }

        if (a.Kind != b.Kind || a.Symbol != b.Symbol || a.Semantics != b.Semantics || a.Components.Count != b.Components.Count || a.LengthArguments.Length != b.LengthArguments.Length ||
            (a.Kind == BoundTypeKind.Primitive && a.Name != b.Name) ||
            (a.Kind == BoundTypeKind.FixedArray && a.LengthExpression is null && b.LengthExpression is null && a.Length != b.Length))
        {
            return false;
        }

        for (var i = 0; i < a.LengthArguments.Length; i++)
        {
            if (a.LengthArguments[i] is { IsConstant: true } x && b.LengthArguments[i] is { IsConstant: true } y && !ReferenceEquals(x, y))
            {
                return false;
            }
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!this.UnifyTypes(new(a.Components[i], left.Binder), new(b.Components[i], right.Binder)))
            {
                return false;
            }
        }

        return true;
    }
}
