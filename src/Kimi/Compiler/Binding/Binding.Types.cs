// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Tests whether a Type name is written directly as the target of an object form such as <c>objref/T</c>.</summary>
    private static bool IsDirectObjectTarget(Koto syntax)
        => syntax.Parent is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } application &&
        ReferenceEquals(application.Type, syntax);

    private static Koto UnwrapTypeSyntax(Koto node)
    {
        while (node is TypeSemanticsKoto { Type: { } inner, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, ConversionOperation: null })
        {
            node = inner;
        }

        return node;
    }

    private static string? TypeSpelling(Koto syntax) => syntax switch
    {
        IdentifierNameKoto identifier => identifier.IdentifierName,
        TypeSemanticsKoto { Type: null } simple => simple.Identifier,
        _ => null,
    };

    // A qualified Type Name selects the declarations of the qualifier's scope, never its generic parameters (SPEC 9.1).
    private static BindingSymbol? QualifiedTypeMember(BindingSymbol? head)
    {
        while (head is not null && IsGenericParameter(head))
        {
            head = head.Next;
        }

        return head;
    }

    private BindingSymbol? Lookup(string name, BindingScope scope, Koto use, bool type, bool core = false, int arity = 0, TypeLookupRole role = TypeLookupRole.Any)
    {
        this.importCandidates?.GetValueOrDefault(use)?.Clear();
        if (role != TypeLookupRole.Semantics)
        {
            this.qualificationStops?.Remove((use, type));
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (type)
            {
                var candidates = default(TypeCandidates);
                this.AddTypeCandidates(ref candidates, current.Types.GetValueOrDefault(name), scope, core, arity, role: role);
                if (candidates.First is not null)
                {
                    return this.SelectTypeCandidate(candidates, use);
                }

                if (this.StopInheritedLookup(current, scope, use, name, type, core, arity, role))
                {
                    return null;
                }

                continue;
            }

            if (current.Values.TryGetValue(name, out var symbol))
            {
                for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
                {
                    if (core && candidate.Kind == BindingSymbolKind.Container)
                    {
                        continue;
                    }

                    if (candidate.Kind == BindingSymbolKind.Local && candidate.Declaration.Span.End > use.Span.Start)
                    {
                        continue;
                    }

                    if (!this.ParameterVisible(candidate))
                    {
                        continue;
                    }

                    if (!this.Accessible(candidate, scope))
                    {
                        continue;
                    }

                    if (current.Owner is DeclarationContainerKoto && IsInstanceMember(candidate) && !this.HasReceiverlessCandidate(symbol, scope, core))
                    {
                        this.RecordQualificationStop(use, type, new(current, scope, candidate, name, type, core, arity));
                        return null;
                    }

                    return candidate;
                }
            }

            if (this.StopInheritedLookup(current, scope, use, name, type, core, arity))
            {
                return null;
            }
        }

        if (type && !core && this.ModuleReference(use, name) is { } reference)
        {
            return reference;
        }

        if (this.aliasResolutionDepth != 0)
        {
            return null;
        }

        return type
            ? this.LookupImportedType(name, scope, use, core, arity, role)
            : this.LookupImportedValue(name, scope, use, core);
    }

    // SPEC 13.5.1: each role fixes its own first eligible lookup stage before the two roles are compared.
    // Operand or target fitting never resolves an ambiguity or reopens either search.
    private BindingSymbol? LookupAdaptationTarget(Koto syntax, BindingScope scope, out bool semantics)
    {
        semantics = false;
        if (TypeSpelling(syntax) is not { } name)
        {
            return null;
        }

        if (name == "Self")
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Owner is DeclarationContainerKoto and not GroupKoto)
                {
                    return current.Owner.BoundSymbol;
                }
            }

            return null;
        }

        var target = this.Lookup(name, scope, syntax, true, true, role: TypeLookupRole.Type);
        if (syntax.BindingState == BindingState.Invalid)
        {
            return null;
        }

        var parameter = this.Lookup(name, scope, syntax, true, true, role: TypeLookupRole.Semantics);
        if (syntax.BindingState == BindingState.Invalid)
        {
            return null;
        }

        if (target is not null && parameter is not null)
        {
            this.Fail(syntax, BindingFailure.Ambiguous, true);
            return null;
        }

        semantics = parameter is not null;
        // Preserve the ordinary wrong-role or missing-name diagnostic when neither role applies.
        var selected = parameter ?? target ?? this.Lookup(name, scope, syntax, true, true);
        syntax.BoundSymbol = selected;
        return selected;
    }

    // The symbol a Type name selects. It binds none of the name's syntax: a qualifier is bound once, in the role of the form its name
    // selects, by BindContainerReference for a nested Type and by BindAssociatedProjection for a projection base, which the projection's
    // position completes (SPEC 15.4.2).
    private BindingSymbol? TypeName(Koto syntax, BindingScope scope, bool core, int arity = 0)
    {
        while (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            syntax = inner;
        }

        if (syntax is GenericsKoto generic)
        {
            return this.TypeName(generic.Identifier!, scope, core, generic.TypeArguments.Count);
        }

        var name = TypeSpelling(syntax);
        if (name is not null)
        {
            if (name == "Self")
            {
                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Owner is DeclarationContainerKoto and not GroupKoto)
                    {
                        return current.Owner.BoundSymbol;
                    }
                }
            }

            var target = syntax;
            while (target.Parent is TypeSemanticsKoto { IsTransparentWrapper: true } wrapper)
            {
                target = wrapper;
            }

            if (target.Parent is ConversionKoto conversion && ReferenceEquals(conversion.Right, target))
            {
                return this.LookupAdaptationTarget(syntax, scope, out _);
            }

            return this.Lookup(name, scope, syntax, true, core, arity);
        }

        if (syntax is ParenthesizedTypeKoto parentheses)
        {
            return this.TypeName(parentheses.Type, scope, core, arity);
        }

        if (syntax is MemberAccessKoto member)
        {
            var qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier is not null && this.scopes.TryGetValue(qualifier.Declaration, out var members) && TypeSpelling(member.Right) is { } rightName &&
                this.SelectTypeCandidate(QualifiedTypeMember(members.Types.GetValueOrDefault(rightName)), scope, syntax, core, arity) is { } target)
            {
                var right = member.Right;
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
                right.BoundSymbol = target;
                right.BindingState = BindingState.Resolved;
                return target;
            }

            // The declaration's own Type selects the inherited member: a qualifier's arguments and Origins never change the selection.
            if (qualifier?.Declaration is StructKoto && TypeSpelling(member.Right) is { } inheritedName &&
                this.LookupTypeMember(this.SelfType(qualifier), inheritedName, scope, typeRole: true) is { Member: { } inherited })
            {
                member.Left.BoundSymbol = qualifier;
                member.Right.BoundSymbol = inherited;
                member.Right.BindingState = BindingState.Resolved;
                return this.SelectTypeCandidate(inherited, scope, syntax, core, arity);
            }
        }

        if (syntax is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1)
        {
            var resolved = this.RootTypeName(root.Operands[0], core, arity);
            if (resolved is not null)
            {
                root.Operands[0].BoundSymbol = resolved;
                root.Operands[0].BindingState = BindingState.Resolved;
            }

            return resolved;
        }

        return null;
    }

    private BindingSymbol? RootTypeName(Koto syntax, bool core, int arity = 0)
    {
        syntax = UnwrapTypeSyntax(syntax);
        if (syntax is GenericsKoto generic)
        {
            return this.RootTypeName(generic.Identifier!, core, generic.TypeArguments.Count);
        }

        if (TypeSpelling(syntax) == "Kimi")
        {
            return this.Library.Module;
        }

        var scope = this.ModuleScope(syntax);
        if (syntax is MemberAccessKoto member && this.RootTypeName(member.Left, false) is { } qualifier && this.scopes.TryGetValue(qualifier.Declaration, out var members) && TypeSpelling(member.Right) is { } rightName)
        {
            var target = this.SelectTypeCandidate(members.Types.GetValueOrDefault(rightName), scope, syntax, core, arity);
            if (target is not null && (!this.Accessible(target, scope) || (core && target.Kind == BindingSymbolKind.Container)))
            {
                return null;
            }

            member.Left.BoundSymbol = qualifier;
            member.Left.BindingState = BindingState.Resolved;
            member.Right.BoundSymbol = target;
            member.Right.BindingState = target is null ? BindingState.Unresolved : BindingState.Resolved;
            member.BoundSymbol = target;
            member.BindingState = target is null ? BindingState.Unresolved : BindingState.Resolved;
            return target;
        }

        if (TypeSpelling(syntax) is { } name)
        {
            var target = this.SelectTypeCandidate(scope.Types.GetValueOrDefault(name), scope, syntax, core, arity) ?? this.ModuleReference(syntax, name);
            return target is not null && (!core || target.Kind != BindingSymbolKind.Container) && this.Accessible(target, scope) ? target : null;
        }

        return null;
    }

    private BoundType? BindType(Koto syntax, BindingScope scope)
        => this.BindType(syntax, scope, this.TypeContext(syntax, scope));

    private BoundType? BindType(Koto syntax, BindingScope scope, TypeBindingContext context)
    {
        if (syntax.BoundType is { } known && !(this.closureHeader?.ReturnType is { } result && IsWithin(syntax, result)))
        {
            return known;
        }

        if (IsArrayHole(syntax) && syntax.BindingState != BindingState.Unvisited)
        {
            this.Consulted(syntax);
            return null; // A failed local inference request is not a Type name to look up again.
        }

        if (!this.resolvingTypes.Add(syntax))
        {
            return this.Fail(syntax, BindingFailure.Cycle, true);
        }

        // A Type expression is checked like an expression: the components that failed are its prerequisites.
        var frame = this.BeginConsultation(syntax);
        try
        {
            var owner = OriginOwner(syntax);
            var originDeclaration = owner is not null and not (FunctionKoto or PropertyAccessorKoto) ? this.BeginOriginDeclaration(owner, scope) : null;
            var type = this.BindTypeStructure(syntax, scope, context);
            if (IsRecovery(syntax, out _))
            {
                // The children were checked in their Type/length contexts; the rejected combination establishes no Type.
                RestPendingOn(originDeclaration, syntax);
                return Complete(syntax, null);
            }

            if (type is null)
            {
                RestPendingOn(originDeclaration, syntax);
                return null;
            }

            if (this.associatedInferenceReady && this.PendingAssociatedType(type))
            {
                return type; // A partial header may inspect structure; syntax is not a completed Type yet.
            }

            var annotated = syntax as TypeSemanticsKoto;
            type = this.CompleteOrigins(type, annotated, syntax, scope, context);
            Complete(syntax, type);
            this.CompleteOriginDeclaration(originDeclaration);
            return syntax.BoundType;
        }
        finally
        {
            this.resolvingTypes.Remove(syntax);
            this.EndConsultation(syntax, frame);
            this.Consulted(syntax);
        }
    }

    private BoundType? BindTypeStructure(Koto syntax, BindingScope scope, TypeBindingContext context, bool applyingOrigins = false)
    {
        switch (syntax)
        {
            case SyntaxFormKoto { Akind: KotoKind.InferredType } when
                scope.Owner is FunctionKoto { IsAnonymous: false, IsConstructor: false, BoundSymbol: { ReceiverIndex: >= 0 } functionSymbol } function &&
                ReferenceEquals(function.Parameters[functionSymbol.ReceiverIndex].Type, syntax):
                // Bare self has a fixed shared receiver Type; the body supplies no inference.
                return this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [this.DeclarationSelf(functionSymbol.Scope.Owner.BoundSymbol!)]);
            case SyntaxFormKoto { Akind: KotoKind.RootName } root when root.Operands.Length == 1 && UnwrapTypeSyntax(root.Operands[0]) is GenericsKoto rootedGeneric:
                var rootDefinition = this.RootTypeName(rootedGeneric, true);
                var rootType = this.BindConstructedType(rootedGeneric, rootDefinition, scope, context);
                root.BoundSymbol = rootDefinition;
                root.Operands[0].BoundSymbol = rootDefinition;
                Complete(root.Operands[0], rootType);
                return rootType;
            case GenericParameterKoto:
                return syntax.BoundSymbol?.WholeType;
            case LengthParameterKoto:
                return BoundType.ISize;
            case OptionalTypeKoto optional:
                var payload = this.BindType(optional.Type, scope, context.Nested);
                return payload is null ? null : this.types.Intern(BoundTypeKind.Constructed, this.Library.Option, SemanticsKind.Owner, [payload]);
            case ParenthesizedTypeKoto parentheses:
                return this.BindType(parentheses.Type, scope, context);
            case TypeSemanticsKoto semantics:
                if (semantics.Type is not null)
                {
                    var transparent = semantics.IsTransparentWrapper || (semantics.SemanticsParameter is null && semantics.SemanticsKind == SemanticsKind.Owner);
                    var innerContext = transparent ? context with { SuppressOuter = true } : context.Nested;
                    // A pair target is a projection, not a complete value type until its role is proved.
                    BoundType? inner;
                    if (!semantics.IsTransparentWrapper && semantics.SemanticsParameter is not null)
                    {
                        var targetSyntax = semantics.Type;
                        while (targetSyntax is ParenthesizedTypeKoto grouped)
                        {
                            targetSyntax = grouped.Type;
                        }

                        var target = this.TypeName(targetSyntax, scope, true);
                        if (target?.Kind == BindingSymbolKind.SemanticsTarget &&
                            targetSyntax is not TypeSemanticsKoto { HasOrigin: true })
                        {
                            inner = target.Type;
                            // Grouping preserves the projection's role (SPEC 8.1.1–8.1.2).
                            // An annotated target still needs ordinary complete-Type validation.
                            var groupedTarget = semantics.Type;
                            while (true)
                            {
                                groupedTarget.BoundSymbol = target;
                                Complete(groupedTarget, inner);
                                if (groupedTarget is not ParenthesizedTypeKoto targetParentheses)
                                {
                                    break;
                                }

                                groupedTarget = targetParentheses.Type;
                            }
                        }
                        else
                        {
                            inner = this.BindType(semantics.Type, scope, innerContext);
                        }

                        var parameter = this.Lookup(semantics.SemanticsParameter, scope, syntax, true, role: TypeLookupRole.Semantics);
                        if (parameter?.Kind != BindingSymbolKind.SemanticsParameter)
                        {
                            return this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                        }

                        syntax.BoundSymbol = parameter;
                        if (inner is null)
                        {
                            return null;
                        }

                        if (ReferenceEquals(inner.Symbol, parameter.Pair) && inner.Kind == BoundTypeKind.TargetProjection)
                        {
                            return parameter.Pair!.WholeType;
                        }

                        this.AddObligation(new(BindingObligationKind.TypeFormation, syntax, BindingDeadline.Definition, inner));
                        return this.types.Intern(BoundTypeKind.SemanticsApplication, parameter.Pair, SemanticsKind.Parameter, [inner]);
                    }

                    inner = this.BindType(semantics.Type, scope, innerContext);
                    if (inner is null)
                    {
                        return null;
                    }

                    if (transparent)
                    {
                        return inner;
                    }

                    var kind = semantics.SemanticsKind;
                    if (kind is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq)
                    {
                        // SPEC 8.7.3: the target must be an Object Target. Generic targets and a Contract's
                        // Self are proven from their premises at the definition deadline.
                        if (inner.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection)
                        {
                            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, inner));
                        }
                        else if (inner.Semantics != SemanticsKind.Owner || ReferenceEquals(inner, BoundType.Never))
                        {
                            return this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                        }
                        else if (inner.Symbol?.ObjectPayloadOptOut is { } renounced)
                        {
                            return this.FailObjectPayload(syntax, renounced);
                        }
                    }

                    return this.types.Intern(BoundTypeKind.Semantics, null, kind, [inner]);
                }

                if (BoundType.Primitives.TryGetValue(semantics.Identifier, out var primitive))
                {
                    return primitive;
                }

                break;
            case TupleTypeKoto tuple:
                if (tuple.ElementNodes.Count == 0)
                {
                    return BoundType.Unit;
                }

                return this.BindTypeList(tuple.ElementNodes, scope, context.Nested, BoundTypeKind.Tuple);
            case FunctionTypeKoto function:
                return this.BindFunctionType(function, scope);
            case FixedArrayTypeKoto array:
                var element = this.BindType(array.ElementType, scope, context.Nested);
                var length = this.BindLength(array.Length, scope);
                if (length is null)
                {
                    return array.Length.BindingState != BindingState.Resolved
                        ? this.CompleteDependent(array, array.Length) : this.Fail(array, BindingFailure.InvalidTypeFormation);
                }

                return element is null ? null : this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
            case OriginApplicationKoto application:
                return this.BindAssociatedApplication(application, scope);
            case GenericsKoto generic:
                var definition = this.TypeName(generic, scope, true);
                return this.BindConstructedType(generic, definition, scope, context);
        }

        var symbol = this.TypeName(syntax, scope, true);
        if (syntax is MemberAccessKoto projection && symbol is null)
        {
            var associated = this.BindAssociatedProjection(projection, scope);
            if (associated is not null)
            {
                return associated;
            }
        }

        if (symbol is null && this.EnclosingContractSelf(scope) is { } contractSelf && syntax is TypeSemanticsKoto { Type: null } or IdentifierNameKoto)
        {
            var name = syntax is IdentifierNameKoto identifier ? identifier.IdentifierName : ((TypeSemanticsKoto)syntax).Identifier;
            symbol = this.FindAssociated(contractSelf, scope, name, null, syntax);
        }

        if (symbol is null)
        {
            return this.FailMissingType(syntax, scope);
        }

        syntax.BoundSymbol = symbol;
        var isSelf = syntax is TypeSemanticsKoto { Identifier: "Self" } or IdentifierNameKoto { IdentifierName: "Self" };
        if (symbol.Kind is BindingSymbolKind.Container or BindingSymbolKind.SemanticsParameter || (symbol.Declaration is ContractKoto && !isSelf))
        {
            return this.Fail(syntax, BindingFailure.InvalidTypeFormation);
        }

        if (symbol.Kind == BindingSymbolKind.AssociatedType)
        {
            if (this.UnappliedFamily(symbol.Declaration, applyingOrigins))
            {
                return this.Fail(syntax, BindingFailure.InvalidAssociatedType);
            }

            var self = this.EnclosingContractSelf(scope);
            if (self is null)
            {
                return this.Fail(syntax, BindingFailure.InvalidAssociatedType);
            }

            symbol = this.FindAssociated(self, scope, symbol.Name, null, syntax, out var reference);
            if (symbol is null)
            {
                return null;
            }

            syntax.BoundSymbol = symbol;

            var projected = this.types.Intern(BoundTypeKind.AssociatedProjection, symbol, SemanticsKind.Owner, [self, this.ProjectionContract(symbol, reference)]);
            return this.NormalizedProjection(projected, scope, applyingOrigins);
        }

        if (symbol.Kind == BindingSymbolKind.SemanticsTarget && !IsDirectObjectTarget(syntax))
        {
            // SPEC 8.1.1: a standalone use of the pair target needs the value-Type role. As the direct target
            // of an object form it needs the Object Target role instead, which that form's obligation proves.
            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, symbol.Type));
        }

        if (isSelf)
        {
            return this.DeclarationSelf(symbol);
        }

        return symbol.Declaration is DeclarationContainerKoto
            ? this.BindContainerReference(syntax, symbol, scope, context, [])
            : symbol.Type;
    }

    private BoundType? BindConstructedType(GenericsKoto generic, BindingSymbol? definition, BindingScope scope, TypeBindingContext context)
    {
        if (definition is null)
        {
            // The generic's Name is what is missing; the application is not formed from it.
            return this.FailMissingType(generic.Identifier!, scope);
        }

        if (definition.Declaration is not DeclarationContainerKoto container || container is ContractKoto or GroupKoto)
        {
            return this.Fail(generic, BindingFailure.InvalidTypeFormation);
        }

        if (generic.TypeArguments.Count != container.GenericParameterNodes.Count)
        {
            return this.FailExplained(ref this.arityFailures, generic, BindingFailure.InvalidTypeFormation, (definition, container.GenericParameterNodes.Count, generic.TypeArguments.Count, false));
        }

        generic.Identifier!.BoundSymbol = definition;
        generic.Identifier.BindingState = BindingState.Resolved;
        generic.BoundSymbol = definition;
        var own = this.BindTypeList(generic.TypeArguments, scope, context.Nested, BoundTypeKind.Constructed, definition);
        if (own is { IsWrappingInteger: true })
        {
            // SPEC 3.1.1: Wrapping<T> over an integer Type is already the interned Scalar, and that argument satisfies the
            // declaration's only Constraint, so no container reference or obligation is formed.
            return Complete(generic, own);
        }

        // SPEC 15.3.5: Loan<T> is formed only over a complete ref, uniq, objref or objuniq borrow Type.
        if (definition.LibraryDeclaration == KimiDeclarationId.Loan && own is { Components: [var borrowed] } &&
            borrowed is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 })
        {
            return this.Fail(generic, BindingFailure.InvalidTypeFormation);
        }

        return own is null ? null : Complete(generic, this.BindContainerReference(generic, definition, scope, context, (BoundType[])own.Components));
    }

    private BoundType? BindTypeList(
        IReadOnlyList<Koto> elements,
        BindingScope scope,
        TypeBindingContext context,
        BoundTypeKind kind,
        BindingSymbol? symbol = null)
    {
        var buffer = this.RentTypes(elements.Count);
        try
        {
            var complete = true;
            for (var i = 0; i < elements.Count; i++)
            {
                var type = this.BindType(elements[i], scope, context);
                complete &= type is not null;
                buffer[i] = type!;
            }

            return complete ? this.types.Intern(kind, symbol, SemanticsKind.Owner, buffer.AsSpan(0, elements.Count)) : null;
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }
    }
}
