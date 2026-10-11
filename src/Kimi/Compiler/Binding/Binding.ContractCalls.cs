// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<MemberAccessKoto, RequirementGroup> requirementGroups = new(ReferenceEqualityComparer.Instance);

    // SPEC 8.4.2: the bound Contract reference whose requirement is the candidate under evaluation or the selected
    // callee; its Type arguments substitute the requirement's signature (Key of Indexable<isize>) in ContractType.
    private BindingSymbol? activeRequirementContract;

    private static bool HasUnsubstitutedOrigin(BoundType type, Koto binder)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (type.Origin is { } origin && HasOrigin(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (HasOrigin(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasUnsubstitutedOrigin(type.Components[i], binder))
            {
                return true;
            }
        }

        return false;

        bool HasOrigin(BoundOrigin origin)
        {
            if (ReferenceEquals(origin.Binder, binder))
            {
                return true;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (HasOrigin(origin.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private BindingSymbol? RequirementMember(MemberAccessKoto member, BindingScope scope, BoundType type, bool typeAccess)
    {
        if (!FormattingTypes.IsBuiltin(type) && !ComparisonTypes.IsComposite(type) && !this.IsGenericWrapping(type, scope) && type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection) && type.Symbol?.Declaration is not ContractKoto)
        {
            return null;
        }

        if (!this.requirementGroups.TryGetValue(member, out var group))
        {
            this.requirementGroups.Add(member, group = new());
        }

        group.Members.Clear();
        group.Contracts.Clear();
        group.Seen.Clear();
        group.Self = type;
        group.Active = true;
        group.TypeAccess = typeAccess;
        group.PropertyRequirement = false;
        group.Properties.Clear();
        group.PropertyContracts.Clear();
        if (FormattingTypes.IsBuiltin(type) && this.Library.GetSymbol(KimiDeclarationId.Utf8Format)?.Contract is { } formatting)
        {
            Add(formatting);
        }

        if (this.Library.GetSymbol(KimiDeclarationId.Equatable) is { Contract: { } equality } equatable &&
            (ComparisonTypes.IsBuiltin(type, KimiDeclarationId.Equatable) || (ComparisonTypes.IsComposite(type) && this.ComparisonProof(type, equatable, scope, false) == ConstraintProof.Proven)))
        {
            Add(equality);
        }

        if (this.Library.GetSymbol(KimiDeclarationId.Comparable) is { Contract: { } ordering } comparable &&
            (ComparisonTypes.IsBuiltin(type, KimiDeclarationId.Comparable) || (ComparisonTypes.IsComposite(type) && this.ComparisonProof(type, comparable, scope, false) == ConstraintProof.Proven)))
        {
            Add(ordering);
        }

        if ((type.Symbol?.SelfOf ?? type.Symbol)?.Contract is { } own)
        {
            Add(own);
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && AssociatedIdentityMatches(fact.Subject, type) && fact.Contract?.Contract is not null)
                {
                    Add(this.AppliedAssociatedContract(fact, type).Contract!);
                }
            }
        }

        // SPEC 8.4.7.3: PrimitiveInteger implies Position, whose requirement is called through an integer Type parameter;
        // concrete integers gain no members.
        if (this.Library.Position.Contract is { } position && this.IsGenericInteger(type, scope))
        {
            Add(position);
        }

        // Numeric witnesses are requirements under generic evidence, not named numeric members.
        if (this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope))
        {
            for (var id = KimiDeclarationId.Addable; id <= KimiDeclarationId.RemainderProvider; id++)
            {
                if (TypeSpelling(member.Right) == ArithmeticContracts.Method(id))
                {
                    Add(this.ArithmeticReference(id, type).Contract!);
                    break;
                }
            }

            if (this.IsGenericWrapping(type, scope) && TypeSpelling(member.Right) == "negated")
            {
                Add(this.ArithmeticReference(KimiDeclarationId.Negatable, type).Contract!);
            }
        }

        // Expand the proved contracts of this referenced receiver only, as for an explicit associated
        // projection. Inherited signatures can depend on their refining contract's associated identities.
        for (var i = 0; i < group.Contracts.Count + group.PropertyContracts.Count; i++)
        {
            var contract = i < group.Contracts.Count ? group.Contracts[i] : group.PropertyContracts[i - group.Contracts.Count];
            if (this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract)), scope) == ConstraintProof.Proven)
            {
                this.AddContractPremises(contract.Contract!, type, scope);
            }
        }

        // SPEC 9.5: requirements gathered from constraints must share one receiver shape; a mismatch is an error at the use.
        SemanticsKind? expected = null;
        for (var i = 0; i < group.Members.Count; i++)
        {
            if (ReceiverShape(group.Members[i]) is not { } current)
            {
                continue;
            }

            if (expected is null)
            {
                expected = current;
            }
            else if (expected != current)
            {
                this.Fail(member, BindingFailure.ReceiverShapeMismatch);
                group.Active = false;
                return null;
            }
        }

        // SPEC 7.3.1, 9.5: nor may they acquire corresponding parameters of overlapping Types in different modes.
        if (this.CheckGatheredParameterShapes(member, group.Members, aliasStage: false))
        {
            group.Active = false;
            return null;
        }

        if (group.Properties.Count != 0 && group.Members.Count == 0)
        {
            group.Active = false;
            if (group.Properties.Count == 1 && !typeAccess)
            {
                return group.Properties[0];
            }

            // SPEC 11.4: a Property requirement is instance-only, so a Type names none; several identities are not implemented.
            this.Fail(member, typeAccess ? BindingFailure.MissingName : BindingFailure.Unsupported, true);
            return null;
        }

        group.Active = group.Members.Count != 0;
        return group.Active ? group.Members[0] : null;

        void Add(BoundContract shape)
        {
            if (TypeSpelling(member.Right) is not { } name || !shape.MembersByName.TryGetValue(name, out var members))
            {
                return;
            }

            for (var i = 0; i < members.Count; i++)
            {
                // SPEC 8.4.9: one requirement reached through distinct bound references (Indexable<isize>, Indexable<Name>)
                // is one candidate per reference; a refinement reaches the same reference as its ancestor.
                var identity = members[i];
                var requirement = identity.Symbol;
                if (requirement.Declaration is FunctionKoto && group.Seen.Add(identity))
                {
                    group.Members.Add(requirement);
                    group.Contracts.Add(identity.Contract);
                }
                else if (requirement.Kind == BindingSymbolKind.Property)
                {
                    group.PropertyRequirement = true;
                    if (group.Seen.Add(identity))
                    {
                        group.Properties.Add(requirement);
                        group.PropertyContracts.Add(identity.Contract);
                    }
                }
            }
        }
    }

    private BoundType? CallType(BoundType type, FunctionKoto function, BoundType?[] arguments, BindingScope scope, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengths = default)
    {
        if (this.CallMemberPattern(type, function, declaringType) is not { } memberType)
        {
            return null;
        }

        type = memberType;
        if (self is not null)
        {
            type = this.ContractType(type, scope, self);
        }

        var result = this.SubstituteType(type, CallSlotOwner(function), arguments, lengths);
        if (result is not null)
        {
            for (var i = 0; i < function.Parameters.Count && i < inputs.Length; i++)
            {
                if (function.Parameters[i].Type.BoundType?.Origin is { BorrowCondition: { } selector } &&
                    ContainerSlot(CallSlotOwner(function), selector) is var slot && slot >= 0 && slot < arguments.Length &&
                    arguments[slot] is { Semantics: not SemanticsKind.Parameter } binding && !IsBorrow(binding.Semantics))
                {
                    inputs[i] = BoundOrigin.Static;
                }
            }

            result = this.ContractType(result, scope, self);
            result = this.SubstituteStoredOrigins(result, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(function))));
        }

        return result;
    }

    private sealed class RequirementGroup
    {
        internal List<BindingSymbol> Members { get; } = new();

        /// <summary>Gets the Contract, a bound reference where it takes Type arguments, that supplied each member.</summary>
        internal List<BindingSymbol> Contracts { get; } = new();

        internal List<BindingSymbol> Properties { get; } = new();

        internal List<BindingSymbol> PropertyContracts { get; } = new();

        internal HashSet<BoundRequirement> Seen { get; } = new();

        internal BoundType Self { get; set; } = null!;

        internal bool Active { get; set; }

        internal bool TypeAccess { get; set; }

        /// <summary>Gets or sets a value indicating whether a Property requirement matched the name.</summary>
        internal bool PropertyRequirement { get; set; }
    }

    private readonly struct CallCandidates(BindingSymbol first, RequirementGroup? requirements, List<BindingSymbol>? imports)
    {
        public Enumerator GetEnumerator() => new(first, requirements, imports);

        internal struct Enumerator(BindingSymbol first, RequirementGroup? requirements, List<BindingSymbol>? imports)
        {
            private BindingSymbol? next = first;
            private int index;

            public BindingSymbol Current { get; private set; } = null!;

            public bool MoveNext()
            {
                var members = requirements?.Members ?? (imports is { Count: > 0 } ? imports : null);
                if (members is not null)
                {
                    if (this.index == members.Count)
                    {
                        return false;
                    }

                    this.Current = members[this.index++];
                    return true;
                }

                if (this.next is null)
                {
                    return false;
                }

                this.Current = this.next;
                this.next = this.next.Next;
                return true;
            }
        }
    }
}
