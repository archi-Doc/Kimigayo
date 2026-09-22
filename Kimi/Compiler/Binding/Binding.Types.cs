// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 5.2: the canonical raw pointer Type of a projected pointee subplace; raw pointers carry no Origin.
    internal BoundType PointerType(BoundType referent) => this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Unsafe, [referent]);

    private static Koto UnwrapTypeSyntax(Koto node)
    {
        while (node is TypeSemanticsKoto { Type: { } inner, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null })
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

    private bool ParameterVisible(BindingSymbol candidate, Koto use)
    {
        if (this.defaultBindingDepth == 0 || candidate.Kind != BindingSymbolKind.Parameter || candidate.Scope.Owner is not FunctionKoto function)
        {
            return true;
        }

        var root = use;
        while (root.Parent is { } parent && !ReferenceEquals(parent, function))
        {
            root = parent;
        }

        if (!ReferenceEquals(root, function.Body) && !ReferenceEquals(root, function.ExpressionBody))
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(root, function.Parameters[i].DefaultValue))
                {
                    return candidate.Slot < i;
                }
            }
        }

        return true;
    }

    private BindingSymbol? Lookup(string name, BindingScope scope, Koto use, bool type, bool core = false, int arity = 0)
    {
        this.importCandidates?.GetValueOrDefault(use)?.Clear();
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (type)
            {
                var candidates = default(TypeCandidates);
                this.AddTypeCandidates(ref candidates, current.Types.GetValueOrDefault(name), scope, core, arity);
                if (candidates.First is not null)
                {
                    return this.SelectTypeCandidate(candidates, use);
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

                    if (!this.ParameterVisible(candidate, use))
                    {
                        continue;
                    }

                    if (!this.Accessible(candidate, scope))
                    {
                        continue;
                    }

                    return candidate;
                }
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

        BindingSymbol? imported = null;
        var importedTypes = default(TypeCandidates);
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        if (type && use.CodeContext.SourceDocument is { } source &&
            this.namedAliases.TryGetValue((source, name), out var named) && named.BindingState == BindingState.Resolved)
        {
            this.AddTypeCandidates(ref importedTypes, named.BoundSymbol, scope, core, arity, named.BoundType);
        }

        for (var i = 0; documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias);
            if (target is null || !(type ? target.Types : target.Values).TryGetValue(name, out var candidate))
            {
                continue;
            }

            if (type)
            {
                this.AddTypeCandidates(ref importedTypes, candidate, scope, core, arity, alias.BoundType);
                continue;
            }

            candidate = this.AccessibleImport(candidate, scope);
            if (candidate is null || (core && candidate.Kind == BindingSymbolKind.Container))
            {
                continue;
            }

            if (!this.MergeImport(use, candidate, ref imported))
            {
                return null;
            }

            if (alias.BoundType is { } importedEnvironment)
            {
                if (this.importedContainerEnvironments.TryGetValue(use, out var previous) && !ReferenceEquals(previous, importedEnvironment))
                {
                    Fail(use, BindingFailure.Ambiguous, true);
                    return null;
                }

                this.importedContainerEnvironments[use] = importedEnvironment;
            }
        }

        if (importedTypes.First is not null)
        {
            return this.SelectTypeCandidate(importedTypes, use);
        }

        if (imported is not null)
        {
            return imported;
        }

        foreach (var path in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var target = this.DefaultAliasTarget(use, path);
            if (type)
            {
                this.AddTypeCandidates(ref importedTypes, target?.Types.GetValueOrDefault(name), scope, core, arity);
                continue;
            }

            if (target is not null && (type ? target.Types : target.Values).TryGetValue(name, out var candidate) &&
                this.AccessibleImport(candidate, scope) is { } accessible && (!core || accessible.Kind != BindingSymbolKind.Container) && !this.MergeImport(use, accessible, ref imported))
            {
                return null;
            }
        }

        if (importedTypes.First is not null)
        {
            return this.SelectTypeCandidate(importedTypes, use);
        }

        if (imported is not null)
        {
            return imported;
        }

        return null;
    }

    private BindingScope? AliasTarget(AliasKoto alias)
    {
        if (this.aliasTargets.TryGetValue(alias, out var cached))
        {
            return alias.BindingState == BindingState.Invalid ? null : cached;
        }

        this.aliasTargets.Add(alias, null);
        var scope = this.ModuleScope(alias);
        var declarationScope = scope;
        if (alias.TargetSyntax is { } targetSyntax)
        {
            this.aliasResolutionDepth++;
            try
            {
                var originDeclaration = this.BeginOriginDeclaration(alias, scope);
                var symbol = this.TypeName(targetSyntax, scope, false);
                if (symbol?.Declaration is not DeclarationContainerKoto || (alias.Name is not null && symbol.Declaration is not GroupKoto) ||
                    this.BindContainerQualifier(targetSyntax, symbol, scope, this.TypeContext(targetSyntax, scope)) is not { } reference)
                {
                    Fail(alias, BindingFailure.InvalidTypeFormation, true);
                    return null;
                }

                alias.BoundSymbol = symbol;
                Complete(alias, reference);
                this.CompleteOriginDeclaration(originDeclaration);
                reference = alias.BoundType!;
                if (reference.OriginArguments.Count != (symbol.Schema?.Origins.Count ?? 0) || reference.OriginArguments.Contains(null!))
                {
                    Fail(alias, BindingFailure.InvalidOrigin);
                    return null;
                }

                this.aliasTargets[alias] = this.scopes[symbol.Declaration];
                return this.aliasTargets[alias];
            }
            finally
            {
                this.aliasResolutionDepth--;
            }
        }

        for (var i = 0; i < alias.QualifiedName.Count; i++)
        {
            var name = alias.QualifiedName[i];
            var symbol = i == 0 && name == "Kimi" ? this.Library.Module : this.SelectTypeCandidate(scope.Types.GetValueOrDefault(name), declarationScope, alias, false) ?? (i == 0 ? this.ModuleReference(alias, name) : null);
            if (symbol is null || !this.Accessible(symbol, declarationScope) || !this.scopes.TryGetValue(symbol.Declaration, out var next))
            {
                Fail(alias, BindingFailure.MissingName, true);
                return null;
            }

            if (symbol.Declaration is DeclarationContainerKoto { GenericParameterNodes.Count: > 0 } or DeclarationContainerKoto { OriginNames.Count: > 0 })
            {
                Fail(alias, BindingFailure.InvalidTypeFormation, true);
                return null;
            }

            if (symbol.Declaration is DeclarationContainerKoto { HasIncompatibleBindingHeader: true })
            {
                Fail(alias, BindingFailure.InvalidTypeFormation, true);
                return null;
            }

            alias.BoundSymbol = symbol;
            scope = next;
        }

        if (alias.QualifiedName.Count == 0 || (alias.Name is not null && scope.Owner is not GroupKoto))
        {
            Fail(alias, BindingFailure.InvalidTypeFormation, true);
            return null;
        }

        alias.BindingState = BindingState.Resolved;
        this.aliasTargets[alias] = scope;
        return scope;
    }

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
                this.SelectTypeCandidate(members.Types.GetValueOrDefault(rightName), scope, syntax, core, arity) is { } target)
            {
                var right = member.Right;
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
                right.BoundSymbol = target;
                right.BindingState = BindingState.Resolved;
                return target;
            }

            if (qualifier?.Declaration is StructKoto && TypeSpelling(member.Right) is { } inheritedName &&
                this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope)) is { } declaring &&
                this.LookupTypeMember(declaring, inheritedName, scope, typeRole: true) is { Member: { } inherited })
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
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        if (!this.resolvingTypes.Add(syntax))
        {
            return Fail(syntax, BindingFailure.Cycle, true);
        }

        try
        {
            var owner = OriginOwner(syntax);
            var originDeclaration = owner is not null and not (FunctionKoto or PropertyAccessorKoto) ? this.BeginOriginDeclaration(owner, scope) : null;
            var type = this.BindTypeStructure(syntax, scope, context);
            if (type is null)
            {
                return null;
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
        }
    }

    private BoundType? BindTypeStructure(Koto syntax, BindingScope scope, TypeBindingContext context)
    {
        switch (syntax)
        {
            case SyntaxFormKoto { Akind: KotoKind.InferredType } when
                scope.Owner is FunctionKoto { IsAnonymous: false, IsConstructor: false, BoundSymbol: { ReceiverIndex: >= 0 } functionSymbol } function &&
                ReferenceEquals(function.Parameters[functionSymbol.ReceiverIndex].Type, syntax):
                // Bare self has a fixed shared receiver Type; the body supplies no inference.
                return this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [this.SelfType(functionSymbol.Scope.Owner.BoundSymbol!)]);
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

                        var parameter = this.Lookup(semantics.SemanticsParameter, scope, syntax, true);
                        if (parameter?.Kind != BindingSymbolKind.SemanticsParameter)
                        {
                            return Fail(syntax, BindingFailure.InvalidTypeFormation);
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
                        return this.InternType(BoundTypeKind.SemanticsApplication, parameter.Pair, SemanticsKind.Parameter, [inner]);
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
                        if (inner.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection)
                        {
                            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, inner));
                        }
                        else if (inner.Semantics != SemanticsKind.Owner || ReferenceEquals(inner, BoundType.Never) || inner.Symbol?.Declaration is ContractKoto)
                        {
                            return Fail(syntax, BindingFailure.InvalidTypeFormation);
                        }
                    }

                    return this.InternType(BoundTypeKind.Semantics, null, kind, [inner]);
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

                return this.BindTypeList(syntax, tuple.ElementNodes, scope, context.Nested, BoundTypeKind.Tuple);
            case FunctionTypeKoto function:
                return this.BindFunctionType(function, scope, context);
            case FixedArrayTypeKoto array:
                var element = this.BindType(array.ElementType, scope, context.Nested);
                var length = this.BindLength(array.Length, scope);
                if (length is null)
                {
                    return Fail(array, BindingFailure.InvalidTypeFormation);
                }

                return element is null ? null : this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
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

        if (symbol is null && EnclosingContractSelf(scope) is { } contractSelf && syntax is TypeSemanticsKoto { Type: null } or IdentifierNameKoto)
        {
            var name = syntax is IdentifierNameKoto identifier ? identifier.IdentifierName : ((TypeSemanticsKoto)syntax).Identifier;
            symbol = this.FindAssociated(contractSelf, scope, name, null, syntax);
        }

        if (symbol is null)
        {
            return Fail(syntax, BindingFailure.MissingType, true);
        }

        syntax.BoundSymbol = symbol;
        var isSelf = syntax is TypeSemanticsKoto { Identifier: "Self" } or IdentifierNameKoto { IdentifierName: "Self" };
        if (symbol.Kind is BindingSymbolKind.Container or BindingSymbolKind.SemanticsParameter || (symbol.Declaration is ContractKoto && !isSelf))
        {
            return Fail(syntax, BindingFailure.InvalidTypeFormation);
        }

        if (symbol.Kind == BindingSymbolKind.AssociatedType)
        {
            var self = EnclosingContractSelf(scope);
            if (self is null)
            {
                return Fail(syntax, BindingFailure.InvalidAssociatedType);
            }

            symbol = this.FindAssociated(self, scope, symbol.Name, null, syntax);
            if (symbol is null)
            {
                return null;
            }

            syntax.BoundSymbol = symbol;

            var projected = this.InternType(BoundTypeKind.AssociatedProjection, symbol, SemanticsKind.Owner, [self]);
            return this.bindingConstraintTypes ? projected : this.ContractType(projected, scope);
        }

        if (symbol.Kind == BindingSymbolKind.SemanticsTarget)
        {
            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, symbol.Type));
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
        if (definition?.Declaration is not DeclarationContainerKoto container || container is ContractKoto or GroupKoto)
        {
            return Fail(generic, BindingFailure.InvalidTypeFormation);
        }

        if (generic.TypeArguments.Count != container.GenericParameterNodes.Count)
        {
            return Fail(generic, BindingFailure.TypeMismatch);
        }

        generic.Identifier!.BoundSymbol = definition;
        generic.Identifier.BindingState = BindingState.Resolved;
        generic.BoundSymbol = definition;
        var own = this.BindTypeList(generic, generic.TypeArguments, scope, context.Nested, BoundTypeKind.Constructed, definition);
        var bound = own is null ? null : Complete(generic, this.BindContainerReference(generic, definition, scope, context, (BoundType[])own.Components));
        if (bound is not null && container is StructKoto { AttributeChain: not null } structure && HasCLayout(structure))
        {
            this.CheckCLayoutInstance(generic, bound);
        }

        return bound;
    }

    private BoundType? BindTypeList(Koto node, IReadOnlyList<Koto> elements, BindingScope scope, TypeBindingContext context, BoundTypeKind kind, BindingSymbol? symbol = null)
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

            return complete ? this.InternType(kind, symbol, SemanticsKind.Owner, buffer.AsSpan(0, elements.Count)) : null;
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }
    }

    private BoundType InternType(BoundTypeKind kind, BindingSymbol? symbol, SemanticsKind semantics, ReadOnlySpan<BoundType> components, long length = 0, BoundOrigin? origin = null, ReadOnlySpan<BoundOrigin> originArguments = default, BoundLength? lengthExpression = null)
    {
        if (kind == BoundTypeKind.Slice || (symbol is not null && ReferenceEquals(symbol.Declaration, this.Library.Slice.Declaration)))
        {
            kind = BoundTypeKind.Slice;
            symbol = this.Library.Slice.Declaration.BoundSymbol ?? this.Library.Slice;
            if (originArguments.Length == 1)
            {
                origin ??= originArguments[0];
                originArguments = default;
            }
        }

        var hash = default(HashCode);
        hash.Add(kind);
        hash.Add(symbol is null ? 0 : RuntimeHelpers.GetHashCode(symbol));
        hash.Add(semantics);
        hash.Add(length);
        hash.Add(lengthExpression is null ? 0 : RuntimeHelpers.GetHashCode(lengthExpression));
        hash.Add(origin is null ? 0 : RuntimeHelpers.GetHashCode(origin));
        for (var i = 0; i < originArguments.Length; i++)
        {
            hash.Add(RuntimeHelpers.GetHashCode(originArguments[i]));
        }

        for (var i = 0; i < components.Length; i++)
        {
            hash.Add(RuntimeHelpers.GetHashCode(components[i]));
        }

        var key = hash.ToHashCode();
        if (this.types.TryGetValue(key, out var bucket))
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                var type = bucket[i];
                if (type.Kind != kind || type.Symbol != symbol || type.Semantics != semantics || type.Length != length || !ReferenceEquals(type.LengthExpression, lengthExpression) || type.Components.Count != components.Length || !ReferenceEquals(type.Origin, origin) || type.OriginArguments.Count != originArguments.Length)
                {
                    continue;
                }

                var equal = true;
                for (var j = 0; j < components.Length; j++)
                {
                    equal &= ReferenceEquals(type.Components[j], components[j]);
                }

                for (var j = 0; j < originArguments.Length; j++)
                {
                    equal &= ReferenceEquals(type.OriginArguments[j], originArguments[j]);
                }

                if (equal)
                {
                    return type;
                }
            }
        }
        else
        {
            this.types.Add(key, bucket = new(1));
        }

        var created = new BoundType(symbol?.Name ?? kind.ToString(), kind, symbol, semantics, components.ToArray(), length, origin, originArguments.ToArray(), lengthExpression);
        bucket.Add(created);
        return created;
    }
}
