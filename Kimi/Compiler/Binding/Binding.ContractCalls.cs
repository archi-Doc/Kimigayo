// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<MemberAccessKoto, RequirementGroup> requirementGroups = new(ReferenceEqualityComparer.Instance);

    private static bool HasUnsubstitutedOrigin(BoundType type, Koto binder)
    {
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
        if (type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection) && type.Symbol?.Declaration is not ContractKoto)
        {
            return null;
        }

        if (!this.requirementGroups.TryGetValue(member, out var group))
        {
            this.requirementGroups.Add(member, group = new());
        }

        group.Members.Clear();
        group.Seen.Clear();
        group.Self = type;
        group.Active = true;
        group.TypeAccess = typeAccess;
        if (type.Symbol?.Contract is { } own)
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
                if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, type) && fact.Contract?.Contract is { } shape)
                {
                    Add(shape);
                }
            }
        }

        return group.Members.Count == 0 ? null : group.Members[0];

        void Add(BoundContract shape)
        {
            if (TypeSpelling(member.Right) is not { } name || !shape.MembersByName.TryGetValue(name, out var members))
            {
                return;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var requirement = members[i];
                if (requirement.Declaration is FunctionKoto && group.Seen.Add(requirement))
                {
                    group.Members.Add(requirement);
                }
            }
        }
    }

    private BoundType? CallType(BoundType type, FunctionKoto function, BoundType?[] arguments, BindingScope scope, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType = null)
    {
        if (this.MemberType(type, declaringType) is not { } memberType)
        {
            return null;
        }

        type = memberType;
        if (self is not null)
        {
            type = this.ContractType(type, scope, self);
        }

        var result = this.Substitute(type, function, arguments);
        if (result is not null)
        {
            result = this.ContractType(result, scope, self);
            result = this.SubstituteStoredOrigins(result, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, Math.Min(inputs.Length, function.Parameters.Count)));
        }

        return result;
    }

    private sealed class RequirementGroup
    {
        internal List<BindingSymbol> Members { get; } = new();

        internal HashSet<BindingSymbol> Seen { get; } = new(ReferenceEqualityComparer.Instance);

        internal BoundType Self { get; set; } = null!;

        internal bool Active { get; set; }

        internal bool TypeAccess { get; set; }
    }

    private readonly struct CallCandidates(BindingSymbol first, RequirementGroup? requirements)
    {
        public Enumerator GetEnumerator() => new(first, requirements);

        internal struct Enumerator(BindingSymbol first, RequirementGroup? requirements)
        {
            private BindingSymbol? next = first;
            private int index;

            public BindingSymbol Current { get; private set; } = null!;

            public bool MoveNext()
            {
                if (requirements is not null)
                {
                    if (this.index == requirements.Members.Count)
                    {
                        return false;
                    }

                    this.Current = requirements.Members[this.index++];
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
