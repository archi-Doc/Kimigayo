// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundMemberPath? Parent, Koto Base, BoundType Type), BoundMemberPath> memberPaths = new();
    private readonly HashSet<BindingSymbol> memberLookupVisiting = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MemberAccessKoto, MemberSelection> memberSelections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, byte> inheritanceStates = new(ReferenceEqualityComparer.Instance);

    // SPEC 9.1: a generic parameter belongs to its declaration's own scope and is no member of the Type.
    private static bool IsGenericParameter(BindingSymbol symbol)
        => symbol.Kind is BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter or BindingSymbolKind.LengthParameter;

    private void ValidateBaseDeclarations(BindingMode? mode = null)
    {
        this.inheritanceStates.Clear();
        // Bind all bases before checking retained projections, then propagate
        // invalid bases through the ordinary inheritance walk.
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto structure)
            {
                for (var b = 0; b < structure.Bases.Count; b++)
                {
                    var syntax = structure.Bases[b];
                    var scope = this.scopes[structure];
                    var type = this.BindType(syntax, scope);
                    if (mode is { } bindingMode && type is not null)
                    {
                        this.RequireConstraint(syntax, this.CheckTypeConstraints(type, scope), bindingMode);
                    }
                }
            }
        }

        this.ValidateBaseProjections(mode);

        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto structure)
            {
                this.ValidateBaseDeclaration(structure);
            }
        }
    }

    private bool ValidateBaseDeclaration(StructKoto structure)
    {
        var symbol = structure.BoundSymbol!;
        if (this.inheritanceStates.TryGetValue(symbol, out var state))
        {
            return state == 2;
        }

        this.inheritanceStates.Add(symbol, 1);
        var valid = structure.Bases.Count <= 1;
        var scope = this.scopes[structure];
        for (var i = 0; i < structure.Bases.Count; i++)
        {
            var syntax = structure.Bases[i];
            var type = this.BindType(syntax, scope);
            if (syntax.BindingState == BindingState.Invalid ||
                type is not { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Symbol.Declaration: StructKoto parent } ||
                (parent.Modifier & ModifierKind.Open) == 0 || !this.Accessible(parent.BoundSymbol!, scope) || !this.TypeAccessCovers(type, symbol, symbol) ||
                !this.ValidateBaseDeclaration(parent))
            {
                this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                valid = false;
            }
            else if (valid && structure.BindingState != BindingState.Invalid && !this.ValidateInheritedNames(structure, type, scope))
            {
                valid = false;
            }
        }

        // An earlier rejection prevents use of the declaration; it does not fail a base rule.
        var available = valid && structure.BindingState != BindingState.Invalid;
        this.inheritanceStates[symbol] = available ? (byte)2 : (byte)3;
        if (!valid)
        {
            this.Fail(structure, BindingFailure.InvalidTypeFormation);
        }

        return available;
    }

    private bool ValidateInheritedNames(StructKoto structure, BoundType baseType, BindingScope scope)
    {
        foreach (var nested in structure.NestedContainers)
        {
            if (this.LookupTypeMember(baseType, nested.Name, scope, typeRole: true).Member is not null)
            {
                this.Fail(nested, BindingFailure.Duplicate);
                this.Fail(structure, BindingFailure.Duplicate);
                return false;
            }
        }

        foreach (var entry in scope.Values)
        {
            var member = entry.Value;
            while (member is not null && member.Declaration is FunctionKoto { IsConstructor: true } or FunctionKoto { IsDestructor: true } or FunctionKoto { IsSpecialization: true })
            {
                member = member.Next;
            }

            if (member?.Kind is not (BindingSymbolKind.Function or BindingSymbolKind.Property))
            {
                continue;
            }

            // Names reserve the declaration layer regardless of signatures or conditions.
            // The original derived receiver supplies protected access, including Properties.
            if (this.LookupTypeMember(baseType, entry.Key, scope, this.SelfType(structure.BoundSymbol!)).Member is not null)
            {
                this.Fail(structure, BindingFailure.Duplicate);
                return false;
            }
        }

        return true;
    }

    private BindingSymbol? Member(MemberAccessKoto member, BindingScope scope, BoundType? expected = null)
    {
        if (member.Left is BaseReferenceKoto baseReference)
        {
            return this.BaseMember(member, baseReference, scope);
        }

        if (member.Right.Akind == KotoKind.ConstructorReference)
        {
            return this.ConstructorMember(member, scope);
        }

        if (member.Right is not IdentifierNameKoto right)
        {
            return null;
        }

        BindingSymbol? typeMember = null;
        BindingSymbol? valueMember = null;
        MemberSelection typeSelection = default;
        MemberSelection valueSelection = default;
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier?.Type is { } type && qualifier.Declaration is not ContractKoto)
        {
            typeMember = this.RequirementMember(member, scope, type, true);
        }

        if (qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (qualifier.Type is not null)
            {
                var originQualifier = qualifier.Declaration is not EnumKoto && qualifier.Schema is { Origins.Count: > 0 };
                var callQualifier = originQualifier && IsCallee(member);
                if ((qualifier.Declaration is EnumKoto ? this.EnumQualifierType(member.Left, qualifier, scope, expected)
                    : this.BindType(member.Left, scope, this.TypeContext(member.Left, scope) with { CallQualifier = callQualifier })) is not { } qualifiedType)
                {
                    return null;
                }

                typeSelection = this.LookupTypeMember(qualifiedType, right.IdentifierName, scope);
                typeMember = typeSelection.Member ?? typeMember;
                if (originQualifier && this.RejectedOriginQualifier(member, qualifiedType, callQualifier && !typeSelection.Ambiguous ? typeSelection.Member : null))
                {
                    return null;
                }
            }
            else if (qualifier.Declaration is GroupKoto { IsRoot: false } &&
                this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope)) is { } groupType)
            {
                if (groupType.OriginArguments.Count != (qualifier.Schema?.Origins.Count ?? 0) || groupType.OriginArguments.Contains(null!))
                {
                    this.Fail(member.Left, BindingFailure.InvalidOrigin);
                    return null;
                }

                typeSelection = this.LookupTypeMember(groupType, right.IdentifierName, scope);
                typeMember = typeSelection.Member;
            }
            else if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = this.MayBeValueQualifier(member.Left is GenericsKoto constructed ? constructed.Identifier! : member.Left, scope);
        if (valuePossible)
        {
            var receiverType = this.BindNode(member.Left, scope);
            this.ReceiverThroughLayers(member.Left, receiverType);
            this.ReceiverElement(member.Left, receiverType);
            // SPEC 3.4.3: selection continues at the referent of each safe value-reference layer and, under the
            // object view rules, at the payload of an object handle or view.
            while (true)
            {
                while (receiverType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && (IsBorrow(receiverType.Semantics) || IsObjectSemantics(receiverType.Semantics)))
                {
                    receiverType = receiverType.Components[0];
                }

                if (receiverType is not null)
                {
                    // A nominal member takes priority over a requirement member of the same receiver.
                    valueMember = this.RequirementMember(member, scope, receiverType, false);
                    valueSelection = this.LookupTypeMember(receiverType, right.IdentifierName, scope, receiverType);
                    valueMember = valueSelection.Member ?? valueMember;
                }

                // SPEC 3.4.3: a qualifying pair layer without the member, declared or published, continues at its target.
                if (valueMember is not null || receiverType is null || this.FollowablePair(receiverType, scope, out var pairTarget) == SemanticsMask.None)
                {
                    break;
                }

                receiverType = pairTarget;
            }
        }

        if (typeSelection.Ambiguous || valueSelection.Ambiguous || (typeMember is not null && valueMember is not null))
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is null && (typeSelection.Hidden ?? valueSelection.Hidden) is { } hidden)
        {
            member.BoundSymbol = hidden;
            this.Fail(member, BindingFailure.Access);
            return null;
        }

        if (selected is not null)
        {
            if (typeMember is not null && selected.EnumCase is null && qualifier?.Declaration is EnumKoto)
            {
                // Only Case construction may infer the enum qualifier's missing arguments.
                var qualifiedType = this.BindTypeStructure(member.Left, scope, this.TypeContext(member.Left, scope));
                if (qualifiedType is null || this.CompleteOrigins(qualifiedType, member.Left as TypeSemanticsKoto, member.Left, scope, this.TypeContext(member.Left, scope)) is not { } completeType)
                {
                    return null;
                }

                typeSelection = typeSelection with { DeclaringType = completeType };
                Complete(member.Left, completeType);
            }

            if (selected.Kind != BindingSymbolKind.Function && !this.Accessible(selected, scope, receiverType: typeMember is null ? member.Left.BoundType : null))
            {
                this.Fail(member, BindingFailure.Access);
                return null;
            }

            this.memberSelections[member] = typeMember is not null ? typeSelection : valueSelection;
            if (typeMember is not null)
            {
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
            }

            member.BoundSymbol = selected;
            right.BoundSymbol = selected;
            right.BindingState = BindingState.Resolved;
        }

        return selected;
    }

    private bool MayBeValueQualifier(Koto node, BindingScope scope)
    {
        if (node is TypeKoto)
        {
            return false;
        }

        if (node is IdentifierNameKoto name)
        {
            return this.Lookup(name.IdentifierName, scope, node, false) is not null;
        }

        if (node is SyntaxFormKoto { Akind: KotoKind.RootName })
        {
            return false;
        }

        if (node is GenericsKoto generic)
        {
            return this.MayBeValueQualifier(generic.Identifier!, scope);
        }

        if (node is MemberAccessKoto member)
        {
            return this.MayBeValueQualifier(member.Left, scope) ||
                (this.TypeName(member.Left, scope, false) is { } type && this.scopes.TryGetValue(type.Declaration, out var members) && member.Right is IdentifierNameKoto right && members.Values.ContainsKey(right.IdentifierName));
        }

        return true;
    }

    // Access and namespace select the layer. Receiver/argument/accessor checks never reopen it.
    private MemberSelection LookupTypeMember(BoundType type, string name, BindingScope use, BoundType? receiver = null, BoundMemberPath? path = null, bool typeRole = false)
    {
        // SPEC 22.1 (PLAN G32): a fixed array has no declaration; its members are the functions of the Kimi member group.
        if (type is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } && !typeRole && this.Library.FixedArrayMembers is { } fixedMembers &&
            this.scopes.TryGetValue(fixedMembers, out var fixedScope))
        {
            return fixedScope.Values.TryGetValue(name, out var fixedMember) && this.Accessible(fixedMember, use, receiverType: receiver)
                ? new(fixedMember, null, path) : new(null, type, path); // Group functions substitute no declaring Type.
        }

        if (type.Symbol is not { } symbol || !this.scopes.TryGetValue(symbol.Declaration, out var scope))
        {
            return default;
        }

        BindingSymbol? hidden = null;
        if ((typeRole ? scope.Types : scope.Values).TryGetValue(name, out var member))
        {
            for (var candidate = member; candidate is not null; candidate = candidate.Next)
            {
                if (IsGenericParameter(candidate))
                {
                    continue;
                }

                if (this.Accessible(candidate, use, receiverType: receiver))
                {
                    return new(candidate, type, path);
                }

                // Inaccessible declarations are diagnostic evidence, never a layer that stops lookup (SPEC 9.4).
                hidden ??= candidate;
            }
        }

        if (symbol.Declaration is not StructKoto structure)
        {
            return new(null, type, path, Hidden: hidden);
        }

        if (!this.memberLookupVisiting.Add(symbol))
        {
            return new(null, type, path, true);
        }

        try
        {
            MemberSelection result = default;
            for (var i = 0; i < structure.Bases.Count; i++)
            {
                var syntax = structure.Bases[i];
                var baseType = syntax.BoundType ?? this.BindType(syntax, scope);
                if (baseType is null || this.PartType(baseType, type) is not { } substituted)
                {
                    return new(null, type, path, Pending: true);
                }

                var next = this.MemberPath(path, syntax, substituted);
                var candidate = this.LookupTypeMember(substituted, name, use, receiver, next, typeRole);
                if (candidate.Ambiguous || candidate.Pending)
                {
                    return candidate;
                }

                if (candidate.Member is null)
                {
                    hidden ??= candidate.Hidden;
                    continue;
                }

                if (result.Member is not null)
                {
                    return new(null, type, path, true);
                }

                result = candidate;
            }

            return result.Member is null ? result with { Hidden = result.Hidden ?? hidden } : result;
        }
        finally
        {
            this.memberLookupVisiting.Remove(symbol);
        }
    }

    private readonly record struct MemberSelection(BindingSymbol? Member, BoundType? DeclaringType, BoundMemberPath? Path, bool Ambiguous = false, bool Pending = false, BindingSymbol? Hidden = null);

    private BoundMemberPath MemberPath(BoundMemberPath? parent, Koto syntax, BoundType type)
    {
        var key = (parent, syntax, type);
        if (!this.memberPaths.TryGetValue(key, out var path))
        {
            this.memberPaths.Add(key, path = new(parent, syntax, type));
        }

        return path;
    }
}
