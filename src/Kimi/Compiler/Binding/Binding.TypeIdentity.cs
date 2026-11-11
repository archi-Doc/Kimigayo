// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.2, 8.8.1: an available Type-identity premise `X is U` makes X and U one Type in its scope. Expressions bound
// there substitute U for the Type parameter X, so fitting, member lookup and overload resolution see one Type. This is
// substitution, not an equation solver: only premises whose subject is a Type parameter are applied.
public sealed partial class Binding
{
    private readonly HashSet<BoundType> identityExpansions = new(ReferenceEqualityComparer.Instance);

    private static bool HasParameterIdentity(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true })
            {
                return true;
            }
        }

        return false;
    }

    // The index pass's reset of a member or element access's identity premise, which each pass publishes again (DECISIONS BU-72).
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private static void ClearIdentityPremise(Koto node)
    {
        if (node is MemberAccessKoto or IndexKoto)
        {
            HirTables.PlanFor(node, written: false).Second = null;
        }
    }

    /// <summary>Substitutes the Type-identity premises available in <paramref name="scope"/> into <paramref name="type"/>.</summary>
    /// <param name="type">A bound expression Type.</param>
    /// <param name="scope">The scope whose Constraint environments supply the premises.</param>
    /// <returns><paramref name="type"/> itself when no premise applies; otherwise the substituted interned Type.</returns>
    private BoundType SubstituteIdentityPremises(BoundType type, BindingScope scope)
    {
        if (!type.ContainsParameter || !HasParameterIdentity(scope))
        {
            return type;
        }

        return this.SubstituteIdentity(type, scope, parametersOnly: false, out _);
    }

    // DECISIONS BU-72: a member access or an element access whose receiver's Type, through its Semantics layers, is a Type parameter P
    // that the premises make the complete Type U was resolved on U; it publishes the interned `P is U` (HirTables.IdentityPremise), since
    // a MIR Place keeps P.
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private void PublishIdentityPremise(Koto receiver, BoundType actual, BoundType substituted)
    {
        var access = receiver.Parent;
        while (access is ParenthesizedKoto)
        {
            access = access.Parent;
        }

        if (access is not (MemberAccessKoto or IndexKoto) || !ReferenceEquals(KotoHelper.UnwrapParentheses(((BinaryKoto)access).Left), receiver))
        {
            return;
        }

        while (actual is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && substituted is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            (actual, substituted) = (actual.Components[0], substituted.Components[0]);
        }

        if (actual.Kind == BoundTypeKind.Parameter && substituted.Kind != BoundTypeKind.Parameter)
        {
            HirTables.PlanFor(access, written: true).Second = this.InternConstraint(new(ConstraintKind.TypeIdentity, actual, substituted));
        }
    }

    // W2 (ContractSolver's Completion): Complete without a parameter or at the formation's own recursion (identityExpansions), else
    // the meet of IdentityPremise's, the components' and the held Type's.
    private BoundType SubstituteIdentity(BoundType type, BindingScope scope, bool parametersOnly, out Completion completion)
    {
        completion = Completion.Complete;
        if (!type.ContainsParameter)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (!this.identityExpansions.Add(type))
            {
                return type; // A recursive structural premise must not expand without bound.
            }

            try
            {
                var required = this.IdentityPremise(type, scope, parametersOnly, out completion);
                if (required.Kind == BoundTypeKind.Parameter)
                {
                    return required;
                }

                var image = this.SubstituteIdentity(required, scope, parametersOnly, out var part);
                completion = Meet(completion, part);
                return image;
            }
            finally
            {
                this.identityExpansions.Remove(type);
            }
        }

        var count = type.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.SubstituteIdentity(type.Components[i], scope, parametersOnly, out var part);
                completion = Meet(completion, part);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            // DECISIONS BU-60: the Type instance a closure's context holds is substituted with the Closure Type.
            var context = type.ClosureContext;
            if (TyCtxt.HeldType(type) is { } held)
            {
                var image = this.SubstituteIdentity(held, scope, parametersOnly, out var heldPart);
                completion = Meet(completion, heldPart);
                context = ReferenceEquals(image, held) ? context : this.types.HeldContext(type.Symbol!, image);
            }

            if (!changed && ReferenceEquals(context, type.ClosureContext))
            {
                return type;
            }

            // A substituted struct or enum is another Type: its stored fields and cases follow the substitution, as for the
            // declared Types whose storage is prepared, so a field of it keeps the substituted Type (SPEC 8.2).
            var substituted = this.WithContext(this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments), context);
            this.PrepareTypeStorage(substituted);
            return substituted;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // Parameter identities form equivalence classes, not directed rewrite chains. Close only over the finite
    // parameter premises in scope, then choose the same representative for every member. No structural unification
    // or inference of missing premises is performed. Scratch storage is reused across queries and rebinding.
    // W3 (ContractSolver's Completion): Complete when every fact whose availability it asks is direct (W6).
    private BoundType IdentityPremise(BoundType parameter, BindingScope scope, bool parametersOnly, out Completion completion)
    {
        completion = Completion.Complete;
        var capacity = 1;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true } environment)
            {
                capacity += environment.Facts.Count * 2;
            }
        }

        var members = this.RentTypes(capacity);
        var count = 1;
        members[0] = parameter;
        try
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType.Kind: BoundTypeKind.Parameter })
                        {
                            continue;
                        }

                        var available = this.AvailableConstraintFact(environment, fact, out var availability);
                        completion = Meet(completion, availability);
                        if (!available)
                        {
                            continue;
                        }

                        var hasSubject = members.AsSpan(0, count).Contains(fact.Subject);
                        var hasRequired = members.AsSpan(0, count).Contains(fact.RequiredType);
                        if (hasSubject != hasRequired)
                        {
                            members[count++] = hasSubject ? fact.RequiredType : fact.Subject;
                            changed = true;
                        }
                    }
                }
            }

            BoundType? representative = null;
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType: { } required } ||
                        !members.AsSpan(0, count).Contains(fact.Subject))
                    {
                        continue;
                    }

                    var available = this.AvailableConstraintFact(environment, fact, out var availability);
                    completion = Meet(completion, availability);
                    if (available)
                    {
                        if (required.Kind != BoundTypeKind.Parameter)
                        {
                            if (!parametersOnly)
                            {
                                return required;
                            }

                            continue;
                        }

                        representative ??= required;
                    }
                }
            }

            return representative ?? parameter;
        }
        finally
        {
            this.typeScratch.Return(members, clearArray: true);
        }
    }
}
