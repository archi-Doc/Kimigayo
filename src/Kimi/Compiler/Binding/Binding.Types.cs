// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<Koto> resolvingTypes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Tests whether a Type name is written directly as the target of an object form such as <c>objref/T</c>.</summary>
    private static bool IsDirectObjectTarget(Koto syntax)
        => syntax.Parent is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } application &&
        ReferenceEquals(application.Type, syntax);

    // Whether a walk's failing use is a qualifier of the path `syntax` names (a left of its member steps; `::A.B` and `A<T>.B` name the
    // path of their operand and their identifier), so its record belongs there and not at the path (SPEC 23.1.2).
    private static bool StoppedAtQualifier(Koto use, Koto syntax)
    {
        var path = UnwrapTypeSyntax(syntax);
        if (path is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            path = UnwrapTypeSyntax(root.Operands[0]);
        }

        if (path is GenericsKoto generic)
        {
            path = UnwrapTypeSyntax(generic.Identifier!);
        }

        for (var left = (UseNode(path) as MemberAccessKoto)?.Left; left is not null; left = (UseNode(left) as MemberAccessKoto)?.Left)
        {
            if (ReferenceEquals(UseNode(left), use))
            {
                return true;
            }
        }

        return false;
    }

    private static Koto UnwrapTypeSyntax(Koto node)
    {
        while (node is TypeSemanticsKoto { Type: { } inner, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, ConversionOperation: null })
        {
            node = inner;
        }

        return node;
    }

    // A transparent wrapper (a TypeSemanticsKoto the parser adds around a written Type, IsTransparentWrapper) is the Type it wraps: the one
    // unwrap of it, for an associated head, a qualifier and the node a requirement's failure rests on.
    private static Koto? UnwrapTransparentType(Koto? syntax)
    {
        while (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            syntax = inner;
        }

        return syntax;
    }

    private static string? TypeSpelling(Koto syntax) => syntax switch
    {
        IdentifierNameKoto identifier => identifier.IdentifierName,
        TypeSemanticsKoto { Type: null } simple => simple.Identifier,
        _ => null,
    };

    // A Container's direct Type members named `name`, as a path (SPEC 9.6) or an opening alias (18.2.1) reaches them: never its formal
    // parameters, which only the scope walk finds at their declaring position (9.5).
    private static BindingSymbol? MemberTypes(BindingScope container, string name)
    {
        var head = container.Types.GetValueOrDefault(name);
        while (head is not null && IsGenericParameter(head))
        {
            head = head.Next;
        }

        return head;
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

        // SPEC 15.3.4: a named function's set binds inside its header's Origin declaration, whose relations complete it in any order, so
        // the first binding of one, an occurrence or a use's pull, demands the header (Binding.DeclQuery's Signature): NotStarted binds it;
        // Running (inside it, the common case) and Done return at once. The header is its declaration's, never the consulting Type's
        // prerequisite (SPEC 23.1.2), so the demand runs with the consultation frame suspended. After a Running or Done demand
        // syntax.BoundType is null: the first test above returned a known Type, and its closure-header exception's syntax has an
        // anonymous Origin owner, which this test excludes.
        if (syntax is TypeSemanticsKoto { BindingSetName: not null } &&
            OriginOwner(syntax) is FunctionKoto { IsAnonymous: false, BoundSymbol: { } function })
        {
            var consultation = this.SuspendConsultation();
            try
            {
                this.BindHeader(function);
            }
            finally
            {
                this.ResumeConsultation(consultation);
            }

            if (syntax.BoundType is { } bound)
            {
                return bound;
            }
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
            var type = this.HeightChecked(syntax, this.BindTypeStructure(syntax, scope, context));
            if (IsRecovery(syntax, out _))
            {
                // The children were checked in their Type/length contexts; the rejected combination establishes no Type.
                RestPendingOn(originDeclaration, syntax);
                return Complete(syntax, null);
            }

            if (type is null)
            {
                RestPendingOn(originDeclaration, syntax);
                if (syntax.BindingState == BindingState.Resolved)
                {
                    syntax.BindingState = BindingState.Unresolved; // A lookup's Resolved (PublishQualifier) yields to its binder's outcome.
                }

                return null;
            }

            if (this.associatedInferenceReady && this.PendingAssociatedType(type))
            {
                return type; // A partial header may inspect structure; syntax is not a completed Type yet.
            }

            var annotated = syntax as TypeSemanticsKoto;
            type = this.CompleteOrigins(type, annotated, syntax, scope, context);
            if (OverHeight(type))
            {
                RestPendingOn(originDeclaration, syntax);
                return this.HeightChecked(syntax, type);
            }

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
                var rootDefinition = this.TypeName(root, scope);
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
                    var transparent = TransparentInner(semantics) is not null;
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

                        var target = this.TypeName(targetSyntax, scope);
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

                        var mark = this.nameSteps.Count;
                        var parameter = this.PublishPath(this.ResolveName(semantics.SemanticsParameter, scope, syntax, LookupRole.TypeSemantics, 0), mark);
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
                        if (inner.IsProofDecided)
                        {
                            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, inner));
                        }
                        else if (inner.Semantics != SemanticsKind.Owner || ReferenceEquals(inner, BoundType.Never))
                        {
                            return this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                        }
                        else if (this.RenouncedObjectTarget(syntax, inner))
                        {
                            return null;
                        }
                        else if (!this.clausesBindable)
                        {
                            // A Constraint's Type is formed before the opt-outs are published: judged again once they are (VerifyEarlyObjectForms).
                            (this.earlyObjectForms ??= []).Add((syntax, inner));
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
        }

        return this.BindNamedType(syntax, scope, context, applyingOrigins);
    }

    // The named Type a use's one lookup selected: the lookup published on the use (a requirement's, a qualifier's, a Contract parent's,
    // an Origin set's), else BindType's own, the use's first. Nothing here looks the name up again; a failed lookup's facts (a
    // qualification stop, an ambiguity) are the ones FailMissing explains.
    private BoundType? BindNamedType(Koto syntax, BindingScope scope, TypeBindingContext context, bool applyingOrigins)
    {
        var head = this.NamePath(syntax, scope);
        if (syntax is GenericsKoto generic)
        {
            return this.BindConstructedType(generic, head.Symbol, scope, context);
        }

        var symbol = head.Symbol;
        if (syntax is MemberAccessKoto projection && head.End == PathEnd.Projection)
        {
            return this.BindAssociatedProjection(projection, scope) ?? this.FailMissing(syntax, scope, BindingFailure.MissingType);
        }

        if (symbol is null && this.EnclosingContractSelf(scope) is { } contractSelf && syntax is TypeSemanticsKoto { Type: null } or IdentifierNameKoto)
        {
            var name = syntax is IdentifierNameKoto identifier ? identifier.IdentifierName : ((TypeSemanticsKoto)syntax).Identifier;
            symbol = this.FindAssociated(contractSelf, scope, name, null, syntax);
        }

        if (symbol is null)
        {
            // SPEC 23.1.2: an unresolved walk failed at its failing step (its use), as `S.Missing` of `S.Missing.X`, when that step is a
            // qualifier of the path; an ambiguity or a qualification stop keeps its own record at the path.
            return this.FailMissing(head.End == PathEnd.Unresolved && StoppedAtQualifier(head.Use, syntax) ? head.Use : syntax, scope, BindingFailure.MissingType);
        }

        syntax.BoundSymbol = symbol;
        var isSelf = syntax is TypeSemanticsKoto { Identifier: "Self" } or IdentifierNameKoto { IdentifierName: "Self" };
        if (symbol.Kind is BindingSymbolKind.Container or BindingSymbolKind.SemanticsParameter || symbol.IsContract)
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

        if (symbol.Kind == BindingSymbolKind.TypeParameter)
        {
            // A Type parameter is its own Type, a Contract's Self among them (SPEC 9.1).
            return symbol.Type;
        }

        if (isSelf)
        {
            return this.SelfType(symbol);
        }

        return symbol.Declaration is DeclarationContainerKoto
            ? this.BindContainerReference(syntax, symbol, scope, context, [])
            : symbol.Type;
    }

    private BoundType? BindConstructedType(GenericsKoto generic, BindingSymbol? definition, BindingScope scope, TypeBindingContext context)
    {
        var container = definition?.Declaration as DeclarationContainerKoto;
        if (definition is null || container is null or ContractKoto || generic.TypeArguments.Count != container.GenericParameterNodes.Count)
        {
            // A missing Name, a head that is no Type or a wrong arity forms no application (SPEC 9.5, 9.7), and each is its own record.
            this.BindUnappliedArguments(generic, scope, context.Nested);
            return definition is null ? this.FailMissing(generic.Identifier!, scope, BindingFailure.MissingType)
                : container is null or ContractKoto ? this.Fail(generic, BindingFailure.InvalidTypeFormation)
                : this.FailExplained(ref this.arityFailures, generic, BindingFailure.InvalidTypeFormation, (definition, container.GenericParameterNodes.Count, generic.TypeArguments.Count, false));
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

    // SPEC 9.7, 23.1.2: explicit Type arguments resolve in the use-site context, whatever their head, so a head or arity that forms no
    // application (a Type's or a Contract reference's) never hides their own failures; only a formed application interns them.
    private void BindUnappliedArguments(GenericsKoto? written, BindingScope scope, TypeBindingContext context)
    {
        if (written is null)
        {
            return;
        }

        foreach (var argument in written.TypeArguments)
        {
            this.BindType(argument, scope, context);
        }
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
