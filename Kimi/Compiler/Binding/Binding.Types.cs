// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static string? TypeSpelling(Koto syntax) => syntax switch
    {
        IdentifierNameKoto identifier => identifier.IdentifierName,
        TypeSemanticsKoto { Type: null } simple => simple.Identifier,
        _ => null,
    };

    private BindingSymbol? Lookup(string name, BindingScope scope, Koto use, bool type, bool core = false)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if ((type ? current.Types : current.Values).TryGetValue(name, out var symbol))
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

                    if (!this.Accessible(candidate, scope))
                    {
                        continue;
                    }

                    return candidate;
                }
            }
        }

        BindingSymbol? imported = null;
        for (var i = 0; i < this.aliases.Count; i++)
        {
            var alias = this.aliases[i];
            if (!ReferenceEquals(alias.CodeContext.SourceDocument, use.CodeContext.SourceDocument))
            {
                continue;
            }

            var target = this.AliasTarget(alias, scope);
            if (target is null || !(type ? target.Types : target.Values).TryGetValue(name, out var candidate))
            {
                continue;
            }

            if ((core && candidate.Kind == BindingSymbolKind.Container) || !this.Accessible(candidate, scope))
            {
                continue;
            }

            if (imported is not null && imported != candidate)
            {
                Fail(use, BindingFailure.Ambiguous, true);
                return null;
            }

            imported = candidate;
        }

        if (imported is not null || !type)
        {
            return imported;
        }

        return name == "Core" ? this.Core.Module : this.Core.Scope.Types.GetValueOrDefault(name);
    }

    private BindingScope? AliasTarget(AliasKoto alias, BindingScope useScope)
    {
        var scope = this.rootScope;
        for (var i = 0; i < alias.QualifiedName.Count; i++)
        {
            var symbol = i == 0 && alias.QualifiedName[i] == "Core" ? this.Core.Module : scope.Types.GetValueOrDefault(alias.QualifiedName[i]);
            if (symbol is null || !this.Accessible(symbol, useScope) || !this.scopes.TryGetValue(symbol.Declaration, out var next))
            {
                Fail(alias, BindingFailure.MissingName, true);
                return null;
            }

            alias.BoundSymbol = symbol;
            scope = next;
        }

        alias.BindingState = BindingState.Resolved;
        return scope;
    }

    private bool Accessible(BindingSymbol symbol, BindingScope use, ModifierKind? operationAccess = null)
    {
        if (ReferenceEquals(symbol, this.Core.Module) || symbol.Intrinsic != IntrinsicKind.None)
        {
            return true;
        }

        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.TypeParameter or BindingSymbolKind.LengthParameter or BindingSymbolKind.AssociatedType || symbol.Declaration is FunctionKoto { IsRequirement: true })
        {
            return true;
        }

        var modifier = symbol.Declaration switch
        {
            DeclarationContainerKoto c => c.Modifier,
            FunctionKoto f => f.Modifier,
            VariableKoto v => v.Modifier,
            _ => ModifierKind.NoModifier,
        };
        var access = operationAccess ?? modifier.ExtractAccessibilityModifiers();
        if (access is ModifierKind.Public or ModifierKind.Internal or ModifierKind.ProtectedOrInternal)
        {
            return true;
        }

        if (symbol.Scope == this.rootScope)
        {
            return true;
        }

        for (var scope = use; scope is not null; scope = scope.Parent)
        {
            if (scope == symbol.Scope)
            {
                return true;
            }
        }

        return false;
    }

    private BindingSymbol? TypeName(Koto syntax, BindingScope scope, bool core)
    {
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

            return this.Lookup(name, scope, syntax, true, core);
        }

        if (syntax is MemberAccessKoto member)
        {
            var qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier is not null && this.scopes.TryGetValue(qualifier.Declaration, out var members) && TypeSpelling(member.Right) is { } rightName && members.Types.TryGetValue(rightName, out var target) && (!core || target.Kind != BindingSymbolKind.Container) && this.Accessible(target, scope))
            {
                var right = member.Right;
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
                right.BoundSymbol = target;
                right.BindingState = BindingState.Resolved;
                return target;
            }
        }

        if (syntax is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1)
        {
            var resolved = this.RootTypeName(root.Operands[0], core);
            if (resolved is not null)
            {
                root.Operands[0].BoundSymbol = resolved;
                root.Operands[0].BindingState = BindingState.Resolved;
            }

            return resolved;
        }

        return null;
    }

    private BindingSymbol? RootTypeName(Koto syntax, bool core)
    {
        if (syntax is IdentifierNameKoto { IdentifierName: "Core" })
        {
            return this.Core.Module;
        }

        if (syntax is MemberAccessKoto member && this.RootTypeName(member.Left, false) is { } qualifier && ReferenceEquals(qualifier, this.Core.Module) && member.Right is IdentifierNameKoto right)
        {
            var target = this.Core.Scope.Types.GetValueOrDefault(right.IdentifierName);
            member.Left.BoundSymbol = qualifier;
            member.Left.BindingState = BindingState.Resolved;
            right.BoundSymbol = target;
            right.BindingState = target is null ? BindingState.Unresolved : BindingState.Resolved;
            return target;
        }

        return this.TypeName(syntax, this.rootScope, core);
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
            var type = this.BindTypeStructure(syntax, scope, context);
            if (type is null)
            {
                return null;
            }

            var annotated = syntax as TypeSemanticsKoto;
            type = this.CompleteOrigins(type, annotated, syntax, scope, context);
            return Complete(syntax, type);
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
                    if (semantics.SemanticsParameter is not null)
                    {
                        var target = this.TypeName(semantics.Type, scope, true);
                        if (target?.Kind == BindingSymbolKind.SemanticsTarget)
                        {
                            inner = target.Type;
                            semantics.Type.BoundSymbol = target;
                            Complete(semantics.Type, inner);
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
                        if (inner.Kind == BoundTypeKind.Parameter)
                        {
                            this.AddObligation(new(BindingObligationKind.TypeRole, syntax, BindingDeadline.Definition, inner));
                        }
                        else if (inner.Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) || inner.Symbol?.Declaration is not StructKoto)
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
                var parameters = this.BindType(function.Parameters, scope, context.Nested);
                var result = this.BindType(function.ReturnType, scope, context.Nested);
                return parameters is null || result is null ? null : this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]);
            case FixedArrayTypeKoto array:
                var element = this.BindType(array.ElementType, scope, context.Nested);
                var length = this.BindLength(array.Length, scope);
                if (length is null)
                {
                    return null;
                }

                return element is null ? null : this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
            case GenericsKoto generic:
                var definition = this.TypeName(generic.Identifier!, scope, true);
                if (definition?.Declaration is not DeclarationContainerKoto container || container is ContractKoto or GroupKoto)
                {
                    return Fail(syntax, BindingFailure.InvalidTypeFormation);
                }

                if (generic.TypeArguments.Count != container.GenericParameterNodes.Count)
                {
                    return Fail(syntax, BindingFailure.TypeMismatch);
                }

                generic.Identifier!.BoundSymbol = definition;
                generic.Identifier.BindingState = BindingState.Resolved;
                generic.BoundSymbol = definition;
                return this.BindTypeList(syntax, generic.TypeArguments, scope, context.Nested, BoundTypeKind.Constructed, definition);
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

        if (symbol.Kind is BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget)
        {
            for (var current = scope; current is not null && !ReferenceEquals(current, symbol.Scope); current = current.Parent)
            {
                if (current.Owner is ContractKoto)
                {
                    return Fail(syntax, BindingFailure.InvalidConstraint);
                }
            }
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

        // A generic declaration without its arguments is not a complete Type.
        return symbol.Declaration is DeclarationContainerKoto { GenericParameterNodes.Count: > 0 }
            ? Fail(syntax, BindingFailure.TypeMismatch)
            : symbol.Type;
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
