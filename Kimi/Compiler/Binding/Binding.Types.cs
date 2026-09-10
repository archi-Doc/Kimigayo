// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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

        return imported;
    }

    private BindingScope? AliasTarget(AliasKoto alias, BindingScope useScope)
    {
        var scope = this.rootScope;
        for (var i = 0; i < alias.QualifiedName.Count; i++)
        {
            if (!scope.Types.TryGetValue(alias.QualifiedName[i], out var symbol) || !this.Accessible(symbol, useScope) || !this.scopes.TryGetValue(symbol.Declaration, out var next))
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

    private bool Accessible(BindingSymbol symbol, BindingScope use)
    {
        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.TypeParameter or BindingSymbolKind.LengthParameter)
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
        var access = (ModifierKind)((byte)modifier & 7);
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
        string? name = syntax switch
        {
            IdentifierNameKoto identifier => identifier.IdentifierName,
            TypeSemanticsKoto { Type: null } simple => simple.Identifier,
            _ => null,
        };
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
            if (qualifier is not null && this.scopes.TryGetValue(qualifier.Declaration, out var members) && member.Right is IdentifierNameKoto right && members.Types.TryGetValue(right.IdentifierName, out var target) && (!core || target.Kind != BindingSymbolKind.Container) && this.Accessible(target, scope))
            {
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
                right.BoundSymbol = target;
                right.BindingState = BindingState.Resolved;
                return target;
            }
        }

        if (syntax is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1)
        {
            return this.TypeName(root.Operands[0], this.rootScope, core);
        }

        return null;
    }

    private BoundType? BindType(Koto syntax, BindingScope scope)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        BoundType? result;
        switch (syntax)
        {
            case GenericParameterKoto parameter:
                if (parameter.SemanticsParameter is not null)
                {
                    return Fail(syntax, BindingFailure.Unsupported, true);
                }

                return Complete(syntax, syntax.BoundSymbol?.Type);
            case ParenthesizedTypeKoto parentheses:
                return Complete(syntax, this.BindType(parentheses.Type, scope));
            case TypeSemanticsKoto semantics:
                // Origin contracts must not be erased to manufacture a complete type.
                if (semantics.OriginName is not null || semantics.OriginExpression is not null || semantics.OriginArguments is not null || semantics.SemanticsParameter is not null)
                {
                    return Fail(syntax, BindingFailure.Unsupported, true);
                }

                if (semantics.Type is not null)
                {
                    result = this.BindType(semantics.Type, scope);
                    if (!semantics.IsTransparentWrapper && semantics.SemanticsKind is not (SemanticsKind.Owner or SemanticsKind.Unsafe))
                    {
                        // Safe handles require Origin elision and ownership obligations, not just a prefix.
                        return Fail(syntax, BindingFailure.Unsupported, true);
                    }

                    if (result is not null && !semantics.IsTransparentWrapper && semantics.SemanticsKind != SemanticsKind.Owner)
                    {
                        result = this.InternType(BoundTypeKind.Semantics, null, semantics.SemanticsKind, [result]);
                    }

                    return Complete(syntax, result);
                }

                if (BoundType.Primitives.TryGetValue(semantics.Identifier, out result))
                {
                    return Complete(syntax, result);
                }

                break;
            case TupleTypeKoto tuple:
                if (tuple.ElementNodes.Count == 0)
                {
                    return Complete(syntax, BoundType.Unit);
                }

                return this.BindTypeList(syntax, tuple.ElementNodes, scope, BoundTypeKind.Tuple);
            case FunctionTypeKoto function:
                var parameters = this.BindType(function.Parameters, scope);
                result = this.BindType(function.ReturnType, scope);
                return Complete(syntax, parameters is null || result is null ? null : this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]));
            case FixedArrayTypeKoto array:
                result = this.BindType(array.ElementType, scope);
                if (!this.TryLength(array.Length, scope, out var length))
                {
                    return Fail(syntax, BindingFailure.Unsupported, true);
                }

                return Complete(syntax, result is null ? null : this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [result], length));
            case GenericsKoto generic:
                var definition = this.TypeName(generic.Identifier!, scope, true);
                if (definition?.Declaration is not DeclarationContainerKoto container)
                {
                    return Fail(syntax, BindingFailure.MissingType, true);
                }

                if (generic.TypeArguments.Count != container.GenericParameterNodes.Count)
                {
                    return Fail(syntax, BindingFailure.TypeMismatch);
                }

                generic.Identifier!.BoundSymbol = definition;
                generic.Identifier.BindingState = BindingState.Resolved;
                generic.BoundSymbol = definition;
                return this.BindTypeList(syntax, generic.TypeArguments, scope, BoundTypeKind.Constructed, definition);
        }

        var symbol = this.TypeName(syntax, scope, true);
        if (symbol is null)
        {
            return Fail(syntax, BindingFailure.MissingType, true);
        }

        syntax.BoundSymbol = symbol;
        if (symbol.Declaration is DeclarationContainerKoto { GenericParameterNodes.Count: > 0 })
        {
            return Fail(syntax, BindingFailure.TypeMismatch);
        }

        return Complete(syntax, symbol.Type);
    }

    private BoundType? BindTypeList(Koto node, IReadOnlyList<Koto> elements, BindingScope scope, BoundTypeKind kind, BindingSymbol? symbol = null)
    {
        // Rent scratch references; only the canonical type owns a retained array.
        var buffer = System.Buffers.ArrayPool<BoundType>.Shared.Rent(elements.Count);
        try
        {
            var complete = true;
            for (var i = 0; i < elements.Count; i++)
            {
                var type = this.BindType(elements[i], scope);
                complete &= type is not null;
                buffer[i] = type!;
            }

            return Complete(node, complete ? this.InternType(kind, symbol, SemanticsKind.Owner, buffer.AsSpan(0, elements.Count)) : null);
        }
        finally
        {
            System.Buffers.ArrayPool<BoundType>.Shared.Return(buffer, clearArray: true);
        }
    }

    private BoundType InternType(BoundTypeKind kind, BindingSymbol? symbol, SemanticsKind semantics, ReadOnlySpan<BoundType> components, long length = 0)
    {
        var hash = default(HashCode);
        hash.Add(kind);
        hash.Add(symbol is null ? 0 : RuntimeHelpers.GetHashCode(symbol));
        hash.Add(semantics);
        hash.Add(length);
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
                if (type.Kind != kind || type.Symbol != symbol || type.Semantics != semantics || type.Length != length || type.Components.Count != components.Length)
                {
                    continue;
                }

                var equal = true;
                for (var j = 0; j < components.Length; j++)
                {
                    equal &= ReferenceEquals(type.Components[j], components[j]);
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

        var created = new BoundType(symbol?.Name ?? kind.ToString(), kind, symbol, semantics, components.ToArray(), length);
        bucket.Add(created);
        return created;
    }

    private bool TryLength(Koto node, BindingScope scope, out long length)
    {
        length = 0;
        if (node is ParenthesizedKoto parent)
        {
            var resolved = this.TryLength(parent.Operand, scope, out length);
            Complete(node, resolved ? BoundType.Primitives["isize"] : null);
            return resolved;
        }

        var maximum = this.compilation.PointerWidth switch
        {
            16 => short.MaxValue,
            32 => int.MaxValue,
            _ => long.MaxValue,
        };
        if (node is NumberLiteralKoto number && number.TryGetIntegerMagnitude(out var magnitude) && magnitude <= (UInt128)maximum)
        {
            length = (long)magnitude;
            Complete(node, BoundType.Primitives["isize"]);
            return true;
        }

        return false;
    }
}
