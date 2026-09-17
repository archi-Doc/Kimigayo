// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, BindingSymbol> boundContracts = new(ReferenceEqualityComparer.Instance);

    private BindingSymbol? BindContractReference(Koto syntax, BindingSymbol declaration, BindingScope scope)
    {
        if (declaration.Schema is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 })
        {
            return declaration;
        }

        var context = this.TypeContext(syntax, scope) with { SuppressOuter = true };
        var reference = this.BindContainerReference(syntax, declaration, scope, context, []);
        reference = reference is null ? null : this.CompleteOrigins(reference, syntax as TypeSemanticsKoto, syntax, scope, context);
        if (reference is null || reference.OriginArguments.Count != declaration.Schema.Origins.Count || reference.OriginArguments.Contains(null!))
        {
            Fail(syntax, BindingFailure.InvalidOrigin);
            return null;
        }

        // Associated requirements and converging bound refinement paths need separate
        // requirement-reference identities; never reuse declaration-only evidence there.
        if (declaration.Contract is { AssociatedTypes.Count: > 0 } or { Ancestors.Count: > 0 })
        {
            Fail(syntax, BindingFailure.Unsupported);
            return null;
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

        if (declaration.Contract is { } original)
        {
            shape.RequirementStorage.AddRange(original.Requirements);
            shape.ClauseStorage.AddRange(original.ClauseStorage);
            foreach (var requirement in original.Requirements)
            {
                shape.Seen.Add(requirement);
                if (!shape.MembersByName.TryGetValue(requirement.Name, out var members))
                {
                    shape.MembersByName.Add(requirement.Name, members = new());
                }

                members.Add(requirement);
            }
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
                return this.StoredType(type, reference) ?? type;
            }
        }

        return type;
    }

    private bool ContractBindingsMayCollide(BindingSymbol a, BindingSymbol b, BindingScope scope)
    {
        if (!ReferenceEquals(a.Declaration, b.Declaration))
        {
            return false;
        }

        var substitutions = new Dictionary<BoundType, BoundType>(ReferenceEqualityComparer.Instance);
        return Unify(this.ContractType(a.Type!, scope), this.ContractType(b.Type!, scope));

        BoundType Resolve(BoundType type)
        {
            while (substitutions.TryGetValue(type, out var target))
            {
                type = target;
            }

            return type;
        }

        bool Contains(BoundType value, BoundType variable)
        {
            value = Resolve(value);
            if (ReferenceEquals(value, variable))
            {
                return true;
            }

            foreach (var component in value.Components)
            {
                if (Contains(component, variable))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsFree(BoundType value)
        {
            value = Resolve(value);
            if (value.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
            {
                return false;
            }

            foreach (var component in value.Components)
            {
                if (!IsFree(component))
                {
                    return false;
                }
            }

            return true;
        }

        bool Unify(BoundType left, BoundType right)
        {
            left = Resolve(left);
            right = Resolve(right);
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Kind == BoundTypeKind.Parameter || right.Kind == BoundTypeKind.Parameter)
            {
                var variable = left.Kind == BoundTypeKind.Parameter ? left : right;
                var value = ReferenceEquals(variable, left) ? right : left;
                if (Contains(value, variable))
                {
                    // An occurs check proves inequality only for free constructors.
                    return !IsFree(value);
                }

                substitutions.Add(variable, value);
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
                if (!Unify(left.Components[i], right.Components[i]))
                {
                    return false;
                }
            }

            return true; // Origin/order and unresolved length equalities remain possible.
        }
    }
}
