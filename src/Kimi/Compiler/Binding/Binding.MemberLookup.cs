// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundMemberPath? Parent, Koto Base, BoundType Type), BoundMemberPath> memberPaths = new();
    private readonly Dictionary<MemberAccessKoto, MemberSelection> memberSelections = new(ReferenceEqualityComparer.Instance);

    // The side of each member access whose member was selected (PLAN G724, B4): published by PublishPath's MemberAccess step of the former
    // that selected it (SelectMember, BaseMember, ConstructorMember, a length argument's group member), its own fact beside the lookup's
    // selection, since a constructor access has a side and no selection.
    private readonly Dictionary<MemberAccessKoto, MemberSide> memberSides = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<BindingSymbol, byte> inheritanceStates = new(ReferenceEqualityComparer.Instance);
    private int memberWalks;
    private int unreadIdentityWork;

    /// <summary>Gets the layers LookupMember searched in the latest pass, read by the scaling tests.</summary>
    internal int MemberWalkWork => this.memberWalks;

    /// <summary>Gets the clauses UnreadIdentity read in the latest pass, read by the scaling tests.</summary>
    internal int UnreadIdentityWork => this.unreadIdentityWork;

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
            if (this.nodes[i] is StructKoto { BaseClause: { } syntax } structure)
            {
                var scope = this.scopes[structure];
                var type = this.BindType(syntax, scope);
                if (mode is { } bindingMode && type is not null)
                {
                    this.RequireConstraint(syntax, this.CheckTypeConstraints(type, scope), bindingMode);
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
        var valid = true;
        var scope = this.scopes[structure];
        if (structure.BaseClause is { } syntax)
        {
            var type = this.BindType(syntax, scope);
            if (syntax.BindingState == BindingState.Invalid ||
                type is not { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Symbol.Declaration: StructKoto parent } ||
                (parent.Modifier & ModifierKind.Open) == 0 || !this.Accessible(parent.BoundSymbol!, scope) || !this.TypeAccessCovers(type, symbol, symbol) ||
                !this.ValidateBaseDeclaration(parent))
            {
                this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                valid = false;
            }
            else if (structure.BindingState != BindingState.Invalid && !this.ValidateInheritedNames(structure, type, scope))
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

    // SPEC 9.5 (PLAN G602): a projection T.(C).X in a struct's base clause is an unresolvable dependency cycle when T is the struct or
    // derives from it, with any Type arguments, since T's paths to C through the struct's base must agree with its others (8.6.2, 8.9.2).
    // Decided at the projection from its subject's declaration chain, which stops at the struct, so never from the clause's progress.
    private bool ProjectsThroughItsOwnClause(Koto projection, BoundType subject, BindingScope scope)
    {
        if (scope.Owner is not StructKoto { BaseClause: { } clause, BoundSymbol: { } own } || !IsWithin(projection, clause) || subject.Symbol is not { } start)
        {
            return false;
        }

        if (ReferenceEquals(start, own))
        {
            return true;
        }

        foreach (var layer in new BaseChain(this, start))
        {
            if (ReferenceEquals(layer, own))
            {
                return true;
            }
        }

        return false;
    }

    private bool ValidateInheritedNames(StructKoto structure, BoundType baseType, BindingScope scope)
    {
        for (var i = 0; i < structure.NestedContainers.Count; i++)
        {
            var nested = structure.NestedContainers[i];

            // Any inherited Type member reserves the name (Qualifier: every Type member but a pair binder, which no struct declares).
            if (this.LookupMember(baseType, nested.Name, scope, LookupRole.Qualifier).Member is not null)
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
            if (this.LookupMember(baseType, entry.Key, scope, LookupRole.Value, this.SelfType(structure.BoundSymbol!)).Member is not null)
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

        // SPEC 9.6: the left's reading is decided before either publishes (ReadLeft); its former then selects the member.
        var name = right.IdentifierName;
        switch (this.ReadLeft(member, name, scope, out var qualifierPath))
        {
            case LeftReading.Value:
                return this.ValueSideMember(member, name, scope) is { } valueSide ? this.SelectMember(member, null, default, valueSide, scope) : null;
            case LeftReading.Unsupported:
                // A member-access left of both readings: its value is its own access's selection, which publishes as it selects, so the
                // value path is not evaluated first (BU-117's remainder, PLAN's row: until a member access's value Type is evaluated
                // before publication).
                this.Fail(member.Left, BindingFailure.Unsupported);
                this.Fail(member, BindingFailure.Unsupported);
                return null;
            case LeftReading.Ambiguous:
                this.Fail(member, BindingFailure.Ambiguous, true);
                return null;
            case LeftReading.ValueFailure:
                return null;
        }

        var qualifier = qualifierPath.Symbol;
        if (qualifierPath.End == PathEnd.Projection && UseNode(member.Left) is MemberAccessKoto projection && this.BindAssociatedProjection(projection, scope) is { } projected)
        {
            // SPEC 8.5.2, 9.6 (PLAN G724 (a)): a projection qualifier is a Type whose members are its normal form's and its Constraints'
            // requirements: the Type path over the formed projection; construction (`.init`) is ConstructorMember's. A member missing
            // through a projection that stays one rests on the projection's use (SPEC 23.1.2; as ConstructorMember, DECISIONS BU-184):
            // an unsatisfied conformance is that use's record, a pending prerequisite here.
            Complete(member.Left, projected);
            var selected = this.SelectMember(member, null, this.QualifierSelection(member, null, name, scope, projected), default, scope);
            if (selected is null && projected.Kind == BoundTypeKind.AssociatedProjection)
            {
                this.AddPendingPrerequisite(member, projection);
            }

            return selected;
        }

        return this.TypeSideMember(member, qualifier, name, scope, expected) is { } typeSide ? this.SelectMember(member, qualifier, typeSide, default, scope) : null;
    }

    // SPEC 9.6: the Type path of `qualifier.name`: a requirement member of the qualifier's Type, a member of the Type or group the
    // qualifier forms, or a nested declaration. Null when the qualifier's formation failed (its record).
    private SideSelection? TypeSideMember(MemberAccessKoto member, BindingSymbol? qualifier, string name, BindingScope scope, BoundType? expected)
    {
        if (qualifier is null || qualifier.Declaration is ContractKoto || !this.scopes.ContainsKey(qualifier.Declaration))
        {
            return this.QualifierSelection(member, qualifier, name, scope, null);
        }

        if (qualifier.Type is not null)
        {
            var originQualifier = qualifier.Declaration is not EnumKoto && qualifier.Schema is { Origins.Count: > 0 };
            var callQualifier = originQualifier && IsCallee(member);
            if ((qualifier.Declaration is EnumKoto ? this.EnumQualifierType(member.Left, qualifier, scope, expected)
                : this.BindType(member.Left, scope, this.TypeContext(member.Left, scope) with { CallQualifier = callQualifier })) is not { } qualifiedType)
            {
                return null;
            }

            var selection = this.QualifierSelection(member, qualifier, name, scope, qualifiedType);
            return originQualifier && this.RejectedOriginQualifier(member, qualifiedType, callQualifier && !selection.Selection.Ambiguous ? selection.Selection.Member : null)
                ? null : selection;
        }

        if (qualifier.Declaration is GroupKoto { IsRoot: false })
        {
            if (this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope)) is not { } groupType)
            {
                return null;
            }

            if (groupType.OriginArguments.Count != (qualifier.Schema?.Origins.Count ?? 0) || groupType.OriginArguments.Contains(null!))
            {
                this.Fail(member.Left, BindingFailure.InvalidOrigin);
                return null;
            }

            return this.QualifierSelection(member, qualifier, name, scope, groupType);
        }

        return this.QualifierSelection(member, qualifier, name, scope, null);
    }

    // SPEC 9.6, 8.6.4: the Type path's member of `qualifier.name` over `type`, the qualifier's Type or group (formed, a projection's, or its
    // declaration's own when PickReading compares the readings: arguments never change the selection): a nominal member over a
    // requirement member of the Type; without a Type, a nested declaration of the qualifier's scope. The member step reads the Type in its
    // class's form in the use's view (ClassForm, SPEC 8.2.3: a Type's members are those of the Type its identities make it; R7-N1 c-b).
    private SideSelection QualifierSelection(MemberAccessKoto member, BindingSymbol? qualifier, string name, BindingScope scope, BoundType? type)
    {
        type = type is null ? null : this.ClassForm(type, scope, out _);
        var requirementType = qualifier is null ? type : qualifier.IsContract ? null : qualifier.Type;
        var requirement = requirementType is null ? null : this.RequirementMember(member, scope, requirementType, true);
        if (type is not null && (qualifier is null || qualifier.Type is not null || qualifier.Declaration is GroupKoto))
        {
            var selection = this.LookupMember(type, name, scope, LookupRole.Value);
            return new(selection.Member ?? (qualifier is null || qualifier.Type is not null ? requirement : null), selection, type);
        }

        var nested = qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope) &&
            typeScope.Values.TryGetValue(name, out var candidate) ? candidate : null;
        return new(nested ?? requirement, default);
    }

    // SPEC 9.6, 3.4.3: the value path of `qualifier.name`, bound: the qualifier's value and its receiver layers, then ReceiverMember.
    // Null when the selection rests on an emptied admitted set.
    private SideSelection? ValueSideMember(MemberAccessKoto member, string name, BindingScope scope)
    {
        var receiverType = this.BindNode(member.Left, scope);
        this.ReceiverThroughLayers(member.Left, receiverType);
        this.ReceiverElement(member.Left, receiverType);
        var side = this.ReceiverMember(member, name, scope, receiverType, out var emptied);
        if (emptied)
        {
            this.RestOnEmptiedSet(member, scope);
            return null;
        }

        return side;
    }

    // SPEC 9.6, 3.4.3: a member of a receiver Type: at the referent of each safe value-reference layer, the payload of an object handle
    // or view and the target of a qualifying pair layer; `emptied` when it rests on an emptied admitted set.
    private SideSelection ReceiverMember(MemberAccessKoto member, string name, BindingScope scope, BoundType? receiverType, out bool emptied)
    {
        emptied = false;
        BindingSymbol? valueMember = null;
        MemberSelection valueSelection = default;
        while (true)
        {
            while (receiverType is not null && this.ViewBelow(receiverType, scope) is { } below)
            {
                receiverType = below;
            }

            if (receiverType is not null)
            {
                // A nominal member takes priority over a requirement member of the same receiver.
                valueMember = this.RequirementMember(member, scope, receiverType, false);
                valueSelection = this.LookupMember(receiverType, name, scope, LookupRole.Value, receiverType);
                valueMember = valueSelection.Member ?? valueMember;
            }

            // SPEC 3.4.3: a qualifying pair layer without the member, declared or published, continues at its target; one admitting no
            // case rests the selection on the emptying environment (SPEC 8.3.4).
            if (valueMember is not null || receiverType is null || this.FollowablePair(receiverType, scope, out var pairTarget) is not { } followed)
            {
                return new(valueMember, valueSelection);
            }

            if (followed == SemanticsMask.None)
            {
                emptied = true;
                return default;
            }

            receiverType = pairTarget;
        }
    }

    // SPEC 9.6: selects the member of `qualifier.name` from the left's one reading and publishes it through PublishPath (a MemberAccess
    // step): the member, its selection, its side and, for a Type-side member, the qualifier.
    private BindingSymbol? SelectMember(MemberAccessKoto member, BindingSymbol? qualifier, SideSelection typeSide, SideSelection valueSide, BindingScope scope)
    {
        var typeMember = typeSide.Member;
        var valueMember = valueSide.Member;
        if (typeSide.Selection.Ambiguous || valueSide.Selection.Ambiguous)
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is null)
        {
            if ((typeSide.Selection.Hidden ?? valueSide.Selection.Hidden) is { } hidden)
            {
                this.PublishHiddenMember(member, hidden);
            }
            else if (this.TypePathFailure(typeSide, scope) == BindingFailure.Unsupported)
            {
                this.Fail(member, BindingFailure.Unsupported); // TypePathFailure's unread identity: the located Unsupported_Kd.
            }

            return null;
        }

        var typeSelection = typeSide.Selection;
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

        // The Type side's verdict is TypePathFailure's, which PickReading reads too; the value side's access is checked against its receiver.
        var failure = typeMember is not null ? this.TypePathFailure(typeSide, scope)
            : selected.Kind != BindingSymbolKind.Function && !this.Accessible(selected, scope, receiverType: member.Left.BoundType) ? BindingFailure.Access : BindingFailure.None;
        if (failure == BindingFailure.QualificationRequired)
        {
            // SPEC 9.6 (F's N1c ruling 38): an instance member a Type path reaches has no receiver: QualificationRequired_Kd at the access,
            // its Container the Type declaring the member, with no qualifying repair (the use is written qualified).
            this.qualificationFailures[member] = new(selected.Scope, scope, selected, selected.Name, LookupRole.Value, 0, member);
        }

        if (failure != BindingFailure.None)
        {
            this.Fail(member, failure);
            return null;
        }

        if (this.Supported(member, selected) is null)
        {
            return null;
        }

        return typeMember is not null
            ? this.PublishMemberAccess(member, selected, qualifier, LookupRole.Qualifier, typeSelection)
            : this.PublishMemberAccess(member, selected, null, LookupRole.Value, valueSide.Selection);
    }

    // SPEC 9.6, 9.6.1: a Type path's verdict at a member step, the one rule PickReading (TypePathMember) and SelectMember read: the
    // failure the path stops at, else None. An ambiguous member (Ambiguous), none (MissingName), a member that needs a receiver and is no
    // function (its static or instance use: the path supplies no receiver, QualificationRequired; a Type-qualified instance function
    // takes its receiver as an argument), an inaccessible member that is no function (Access).
    // A member missing over a projection whose identity this use does not read (UnreadIdentity) is the located Unsupported (F's N1c
    // rulings 42, 43).
    private BindingFailure TypePathFailure(SideSelection side, BindingScope scope)
        => side.Selection.Ambiguous ? BindingFailure.Ambiguous
            : side.Member is not { } found ? (this.UnreadIdentity(side.Over) ? BindingFailure.Unsupported : BindingFailure.MissingName)
            : IsInstanceMember(found) && found.Kind != BindingSymbolKind.Function ? BindingFailure.QualificationRequired
            : found.Kind != BindingSymbolKind.Function && !this.Accessible(found, scope) ? BindingFailure.Access
            : BindingFailure.None;

    // SPEC 8.3.2, 8.5.3 (O-441 (a)'s L1 class; F's N1c rulings 42, 43): whether a Type path's receiver is a projection `S.(Q).X` still in
    // its class form whose identity a declared Contract bound of its subject may give but this use's evaluation does not read: S is an
    // associated Type whose declaring Contract bounds it by a Contract B (`associate Item is B`) that strictly refines Q, and B declares a
    // Type identity clause on X. Reads declared facts only (the clauses' bound Constraints; an unbound or failed clause has none, so it
    // gives nothing), never applies or proves a clause. Removal: R7-C3's order-free step, in which every use reads its subject's bounds'
    // supply whatever the written qualifier, so the class is accepted (PLAN's L1 row).
    private bool UnreadIdentity(BoundType? receiver)
    {
        if (receiver is not { Kind: BoundTypeKind.AssociatedProjection, Symbol: { } associated, Components: [{ Kind: BoundTypeKind.AssociatedProjection, Symbol: { } subject }, { Symbol: { } written }] } ||
            subject.Scope.Owner is not ContractKoto declaring)
        {
            return false;
        }

        var qualifier = written.Declaration.BoundSymbol ?? written;
        var bounds = declaring.ConstraintNodes;
        for (var i = 0; i < bounds.Count; i++)
        {
            this.unreadIdentityWork++;
            if (bounds[i].BoundConstraint is { Kind: ConstraintKind.Contract, Subject.Symbol: { } boundSubject, Contract: { } refined } && ReferenceEquals(boundSubject, subject) &&
                refined.Declaration is ContractKoto refining && refining.BoundSymbol is { } child && !ReferenceEquals(child, qualifier) && IsRefinement(child, qualifier) &&
                this.DeclaresIdentity(refining, associated))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a Contract declares a Type identity clause on an associated Type (UnreadIdentity's second fact), its subject or its required Type.
    private bool DeclaresIdentity(ContractKoto contract, BindingSymbol associated)
    {
        var clauses = contract.ConstraintNodes;
        for (var i = 0; i < clauses.Count; i++)
        {
            this.unreadIdentityWork++;
            if (clauses[i].BoundConstraint is { Kind: ConstraintKind.TypeIdentity } identity &&
                (ReferenceEquals(identity.Subject?.Symbol, associated) || ReferenceEquals(identity.RequiredType?.Symbol, associated)))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 9.6: the layer walk of a member lookup in a role (SPEC 9.2), its namespace the role's: the first layer with accessible,
    // role-compatible declarations. Receiver/argument/accessor checks never reopen it.
    private MemberSelection LookupMember(BoundType type, string name, BindingScope use, LookupRole role, BoundType? receiver = null, BoundMemberPath? path = null)
    {
        // SPEC 22.1 (PLAN G32): a fixed array has no declaration; its members are the functions of the Kimi member group.
        var values = InValueNamespace(role);
        if (type is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } && values && this.Library.FixedArrayMembers is { } fixedMembers &&
            this.scopes.TryGetValue(fixedMembers, out var fixedScope))
        {
            return fixedScope.Values.TryGetValue(name, out var fixedMember) && this.Accessible(fixedMember, use, receiverType: receiver)
                ? new(fixedMember, null, path) : new(null, type, path); // Group functions substitute no declaring Type.
        }

        if (type.Symbol is not { } start || !this.scopes.ContainsKey(start.Declaration))
        {
            return default;
        }

        // SPEC 6.2.2: one clause per struct, so the layers are a chain of declarations (Binding.BaseLayers); the Type of each is its
        // clause's, substituted. A layer that names no struct ends the walk: a clause SPEC 6.2.2 rejects has no layers above it.
        BindingSymbol? hidden = null;
        var walk = new BaseWalk(start);
        var layer = type;
        while (true)
        {
            this.memberWalks++;
            var symbol = layer.Symbol!;
            var scope = this.scopes[symbol.Declaration];
            for (var candidate = (values ? scope.Values : scope.Types).GetValueOrDefault(name); candidate is not null; candidate = candidate.Next)
            {
                if (IsGenericParameter(candidate) || !HasLookupRole(candidate, role))
                {
                    continue;
                }

                if (this.Accessible(candidate, use, receiverType: receiver))
                {
                    return new(candidate, layer, path);
                }

                // Inaccessible declarations are diagnostic evidence, never a layer that stops lookup (SPEC 9.4).
                hidden ??= candidate;
            }

            if (symbol.Declaration is not StructKoto structure)
            {
                return ReferenceEquals(layer, type) ? new(null, type, path, Hidden: hidden) : new(null, null, null, Hidden: hidden);
            }

            if (structure.BaseClause is not { } syntax)
            {
                return new(null, null, null, Hidden: hidden);
            }

            // A clause not ready is not bound here; the walk reaching a clause under binding is the cycle BindType reports (SPEC 9.5).
            var next = this.DirectBase(structure);
            var baseType = syntax.BoundType ?? (next is not null ? this.knownBases?.GetValueOrDefault(structure) ?? this.BindType(syntax, scope) : this.resolvingTypes.Contains(syntax) ? this.BindType(syntax, scope) : null);
            if (baseType is null || this.PartType(baseType, layer) is not { } substituted)
            {
                if (syntax.BindingState != BindingState.Unvisited)
                {
                    this.Consulted(syntax); // A clause not read yet has no failure to rest on.
                }

                return new(null, layer, path, Pending: true);
            }

            if (next?.Declaration is not StructKoto)
            {
                return new(null, null, null, Hidden: hidden);
            }

            if (walk.Repeats(next!))
            {
                return new(null, layer, path, true);
            }

            path = this.MemberPath(path, syntax, substituted);
            layer = substituted;
        }
    }

    internal readonly record struct MemberSelection(BindingSymbol? Member, BoundType? DeclaringType, BoundMemberPath? Path, bool Ambiguous = false, bool Pending = false, BindingSymbol? Hidden = null);

    // One path of a member access (SPEC 9.6): the member it found (a requirement member or a nested declaration when its lookup found
    // none), its member lookup and, for a Type path's lookup over a Type, that Type in its class form (Over); default when the path is not
    // formed.
    private readonly record struct SideSelection(BindingSymbol? Member, MemberSelection Selection, BoundType? Over = null);

    // A member access's side (PLAN G724, B4): its qualifier is a Type, with no receiver, or a value, the call's receiver.
    private enum MemberSide : byte
    {
        Type,
        Value,
    }

    // The published side of a member access whose member was selected; a reader of one without it breaks the formers' contract.
    private MemberSide MemberSideOf(MemberAccessKoto member)
        => this.memberSides.TryGetValue(member, out var side) ? side : throw new UnreachableException("A member access without a published side.");

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
