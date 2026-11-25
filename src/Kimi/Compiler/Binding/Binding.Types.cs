// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ResolvingTypeRows resolvingTypes;

    /// <summary>Tests whether a Type name is written directly as the target of an object form such as <c>objref/T</c>.</summary>
    private static bool IsDirectObjectTarget(Koto syntax)
        => syntax.Parent is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } application &&
        ReferenceEquals(application.Type, syntax);

    // Whether a walk's failing use is a qualifier of the path `syntax` names (a left of its member steps; `::A.B` and `A<T>.B` name the
    // path of their operand and their identifier), so its record belongs there and not at the path (SPEC 23.1.2).
    private static bool StoppedAtQualifier(Koto use, Koto syntax)
    {
        for (var left = (UseNode(ContainerPath(syntax)) as MemberAccessKoto)?.Left; left is not null; left = (UseNode(left) as MemberAccessKoto)?.Left)
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
    // parameters, which only the scope walk finds at their declaring position (9.5; BindingScope.Parameters).
    private static BindingSymbol? MemberTypes(BindingScope container, string name) => container.Types.GetValueOrDefault(name);

    private BoundType? BindType(Koto syntax, BindingScope scope)
        => this.BindType(syntax, scope, this.TypeContext(syntax, scope));

    // SPEC 8.5.5 (C4 rulings 8-9): a Type-expression node's Type value is published here, once, and only Complete: after the gate a Type
    // holding a pending projection (HoldsPendingProjection) stays unpublished and is returned for a partial header to inspect, so the
    // binding after the requests forms the node and its parts again. A projection's name node and a specification's head keep their own
    // write, the projection the name denotes as written (BindAssociatedProjection, BindAssociatedName; AsWritten's input, SPEC 8.6.1).
    private BoundType? PublishType(Koto syntax, BoundType? type)
        => type is not null && this.HoldsPendingProjection(type) ? type : Complete(syntax, type);

    // The one predicate of a Type value not yet publishable: after the gate, a projection on a struct or enum receiver is a value inference
    // has not determined (formed Types hold a determined projection's value, never the projection).
    private bool HoldsPendingProjection(BoundType type) => this.associatedInferenceReady && this.PendingAssociatedType(type);

    // The Type binding of a written Type occurrence: the one producer of its Type, which it completes on `syntax` and publishes as formed
    // there (FormedTypeRows; PLAN G1278), whatever names it: a declaration, a `Self`, an alias, a Type parameter.
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
            return this.Record(syntax, BindingFailure.Cycle, [], unresolved: true);
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
                return this.PublishType(syntax, null);
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

            if (this.HoldsPendingProjection(type))
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

            FormedTypeRows.Add(syntax);
            this.PublishType(syntax, type);
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
                FormedTypeRows.Add(root.Operands[0]);
                this.PublishType(root.Operands[0], rootType);
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
                                this.PublishType(groupedTarget, inner);
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

    // SPEC 23.1.2: where a Type use that selected nothing fails, at every Type position (BindNamedType, BindConstructedType), a Contract's
    // parent (BuildContract) and an alias target (SelectAliasTarget), read from the use's published lookup alone (NamePath; F's ruling
    // N1-13 B1, PLAN G1298), so every reader names one site whoever evaluated the walk: an Unresolved walk at its failing step when that
    // step is a qualifier of the path, as `S.Missing` of `S.Missing.X` and of `S.Missing.X<T>`; else a generic at its Name (its arguments
    // keep their own records); else the use, where an ambiguity or a qualification stop keeps its own record.
    private Koto UnselectedTypeSite(Koto syntax)
        => UseNode(syntax) is var use && HirTables.LookupOf(use) == PathEnd.Unresolved && this.PublishedStep(use) is var step && StoppedAtQualifier(step, syntax) ? step
            : syntax is GenericsKoto generic ? generic.Identifier! : syntax;

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
            return this.FailMissing(this.UnselectedTypeSite(syntax), scope, BindingFailure.MissingType);
        }

        syntax.BoundSymbol = symbol;
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

            return this.NormalizedProjection(this.ProjectionIdentity(self, symbol, reference, null), scope, applyingOrigins);
        }

        if (symbol.Kind == BindingSymbolKind.SemanticsTarget && !IsDirectObjectTarget(syntax))
        {
            // SPEC 8.1.1: a standalone use of the pair target needs the value-Type role. As the direct target
            // of an object form it needs the Object Target role instead, which that form's obligation proves.
            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, symbol.Type));
        }

        if (symbol.Kind == BindingSymbolKind.TypeParameter || symbol.SelfOf is not null)
        {
            // A Type parameter is its own Type, a Contract's Self among them; a struct's or enum's Self is its declaration applied to its
            // own bindings (SPEC 9.1), the Type its symbol carries.
            return symbol.Type;
        }

        return symbol.Declaration is DeclarationContainerKoto
            ? this.BindContainerReference(syntax, symbol, scope, context, [])
            : symbol.Type;
    }

    private BoundType? BindConstructedType(GenericsKoto generic, BindingSymbol? definition, BindingScope scope, TypeBindingContext context)
    {
        var container = definition?.Declaration as DeclarationContainerKoto;
        if (definition is null || container is null or ContractKoto || generic.TypeArguments.Count != definition.GenericArity)
        {
            // A missing Name, a head that is no Type or a wrong arity (the symbol's own: a Self has none) forms no application (SPEC 9.5,
            // 9.7), and each is its own record.
            this.BindUnappliedArguments(generic, scope, context.Nested);
            return definition is null ? this.FailMissing(this.UnselectedTypeSite(generic), scope, BindingFailure.MissingType)
                : container is null or ContractKoto ? this.Fail(generic, BindingFailure.InvalidTypeFormation)
                : this.RecordArity(generic, definition, definition.GenericArity, generic.TypeArguments.Count);
        }

        generic.Identifier!.BoundSymbol = definition;
        generic.Identifier.BindingState = BindingState.Resolved;
        generic.BoundSymbol = definition;
        var own = this.BindTypeList(generic.TypeArguments, scope, context.Nested, BoundTypeKind.Constructed, definition);
        if (own is { IsWrappingInteger: true })
        {
            // SPEC 3.1.1: Wrapping<T> over an integer Type is already the interned Scalar, and that argument satisfies the
            // declaration's only Constraint, so no container reference or obligation is formed.
            return this.PublishType(generic, own);
        }

        // SPEC 15.3.5: Loan<T> is formed only over a complete ref, uniq, objref or objuniq borrow Type.
        if (definition.LibraryDeclaration == KimiDeclarationId.Loan && own is { Components: [var borrowed] } &&
            borrowed is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 })
        {
            return this.Fail(generic, BindingFailure.InvalidTypeFormation);
        }

        return own is null ? null : this.PublishType(generic, this.BindContainerReference(generic, definition, scope, context, (BoundType[])own.Components));
    }

    // SPEC 9.7, 23.1.2: explicit Type arguments resolve in the use-site context, whatever their head, so a head or arity that forms no
    // application (a Type's or a Contract reference's) never hides their own failures; only a formed application interns them.
    private void BindUnappliedArguments(GenericsKoto? written, BindingScope scope, TypeBindingContext context)
    {
        if (written is null)
        {
            return;
        }

        for (var i = 0; i < written.TypeArguments.Count; i++)
        {
            var argument = written.TypeArguments[i];
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

    /// <summary>The `resolvingTypes` family's one accessor (DECISIONS BU-220): a pass family, BindType's cycle guard, the Type syntax whose
    /// BindType is on the stack, a `bool` row in the syntax's module's NodeColumns at its SyntaxId, set at BindType's start and reset in its
    /// finally. Its rows are the running pass's working state, all reset when no BindType runs, so the freeze releases them
    /// (NodeColumns.Freeze). Its members keep the names its readers use.</summary>
    private readonly struct ResolvingTypeRows(Binding binding)
    {
        // False when the syntax is already on the stack (a cycle), as HashSet.Add.
        internal bool Add(Koto syntax)
        {
            ref var row = ref HirTables.Of(syntax).Families.ResolvingTypes.Ref(syntax.SyntaxId, binding);
            if (row)
            {
                return false;
            }

            row = true;
            return true;
        }

        internal void Remove(Koto syntax) => HirTables.Of(syntax).Families.ResolvingTypes.Ref(syntax.SyntaxId, binding) = false;

        internal bool Contains(Koto syntax) => syntax.SyntaxOwner is { } table && table.Hir.Families.ResolvingTypes.Get(syntax.SyntaxId, binding);
    }

    /// <summary>The `formedTypes` family's one accessor (DECISIONS BU-220; PLAN G1278, SPEC 9.7, 9.7.1, 12.4): a kept family, whether a node
    /// forms its Type. A `bool` row in the node's module's NodeColumns at its SyntaxId, written true by each producer where it completes
    /// the node's Type: BindType's completion of a written occurrence (a `Self`, an alias and a Type parameter included) and of a rooted
    /// name's operand; EnumQualifierType, for a Case or construction qualifier that writes its own list or is its declaration's `Self`;
    /// ConstructionTargetType, for a qualifier with no own parameters, whose path forms the whole Type; BindContractReference, at each
    /// node of a written Contract reference it completes; and the Dictionary literal's binder. A pass's reset empties its modules' rows
    /// (Empty), which its binders write again, so a row is its node's in the latest pass over its module: a writer's decision reads the
    /// resolved declaration (ConstructionTargetType's own parameters, the binder a qualifier's symbol selects), which an edit can change
    /// under an unchanged node. A node of another pass's module (the frozen library's) keeps its row, and a renumbering moves it; read
    /// only with the node's Type. A node that takes its Type from an expected Type, an inference or another node never has one. Read by
    /// ValidateConstraintUses' site test (ConstrainedType): a constructed Type with clauses is judged at the node that forms it, and
    /// every other node carries it.</summary>
    private static class FormedTypeRows
    {
        internal static void Add(Koto node) => HirTables.Of(node).Families.FormedTypes.Ref(node.SyntaxId) = true;

        internal static bool Contains(Koto node) => node.SyntaxOwner is { } table && table.Hir.Families.FormedTypes.Get(node.SyntaxId);

        // The pass reset's step (ResetPass), over the running pass's modules.
        internal static void Empty(BindingPass pass)
        {
            for (var i = 0; i < pass.Count; i++)
            {
                pass[i].Syntax.Hir.Families.FormedTypes.Empty();
            }
        }
    }
}
