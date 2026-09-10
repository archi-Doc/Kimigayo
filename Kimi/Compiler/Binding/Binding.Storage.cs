// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<DeclarationContainerKoto, StorageShape> storageShapes = new(ReferenceEqualityComparer.Instance);

    private static bool IsStoredVariable(Koto node)
        => node is VariableKoto && node is not PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement };

    private static bool TryEnumPayload(Koto node, out SyntaxFormKoto payload)
    {
        if (node is SyntaxFormKoto { Akind: KotoKind.EnumCase } form && form.Operands.Length == 2 && form.Operands[1] is SyntaxFormKoto { Akind: KotoKind.EnumCase } found)
        {
            payload = found;
            return true;
        }

        payload = null!;
        return false;
    }

    private void PrepareStorage()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not (StructKoto or EnumKoto))
            {
                continue;
            }

            var container = (DeclarationContainerKoto)this.nodes[n];
            if (!this.storageShapes.TryGetValue(container, out var shape))
            {
                this.storageShapes.Add(container, shape = new());
            }

            shape.Types.Clear();
            shape.CaseNames?.Clear();
            shape.HasDestructor = false;
            var scope = this.scopes[container];
            for (var i = 0; i < container.Bases.Count; i++)
            {
                var syntax = container.Bases[i];
                this.BindType(syntax, scope);
                shape.Types.Add(syntax);
            }

            for (var i = 0; i < container.Members.Count; i++)
            {
                var member = container.Members[i];
                if (member is VariableKoto field && IsStoredVariable(field))
                {
                    var syntax = field.TypeKoto ?? field;
                    if (field.TypeKoto is not null)
                    {
                        this.BindType(syntax, scope);
                    }
                    else
                    {
                        this.BindNode(field, scope);
                    }

                    shape.Types.Add(syntax);
                }
                else if (TryEnumPayload(member, out var payload))
                {
                    var name = ((SyntaxFormKoto)member).Operands[0];
                    if (name is IdentifierNameKoto identifier && !(shape.CaseNames ??= new(StringComparer.Ordinal)).Add(identifier.IdentifierName))
                    {
                        Fail(member, BindingFailure.Duplicate);
                        Fail(container, BindingFailure.Duplicate);
                    }

                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        var syntax = payload.Operands[j];
                        this.BindType(syntax, scope);
                        shape.Types.Add(syntax);
                    }
                }
                else if (member is FunctionKoto { IsDestructor: true })
                {
                    shape.HasDestructor = true;
                }
            }

            if (container is EnumKoto && shape.CaseNames is not { Count: > 0 })
            {
                Fail(container, BindingFailure.InvalidTypeFormation);
            }
        }
    }

    private BoundType? StoredType(Koto syntax, BoundType owner)
    {
        if (syntax.BoundType is not { } field || owner.Symbol?.Declaration is not { } binder)
        {
            return null;
        }

        var substituted = this.SubstituteType(field, binder, (BoundType[])owner.Components);
        return substituted is null ? null : this.SubstituteStoredOrigins(substituted, binder, (BoundOrigin[])owner.OriginArguments);
    }

    private BoundOrigin SubstituteStoredOrigin(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundOrigin> arguments)
    {
        if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder) && origin.Slot < arguments.Length)
        {
            return arguments[origin.Slot];
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.SubstituteStoredOrigin(origin.Operands[0], binder, arguments);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.SubstituteStoredOrigin(origin.Operands[i], binder, arguments));
            }

            return result;
        }

        return origin;
    }

    private BoundType SubstituteStoredOrigins(BoundType type, Koto binder, ReadOnlySpan<BoundOrigin> arguments)
    {
        if (arguments.IsEmpty)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.SubstituteStoredOrigin(outer, binder, arguments) : null;
        var components = ArrayPool<BoundType>.Shared.Rent(type.Components.Count);
        var origins = ArrayPool<BoundOrigin>.Shared.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.SubstituteStoredOrigins(type.Components[i], binder, arguments);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.SubstituteStoredOrigin(type.OriginArguments[i], binder, arguments);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression) : type;
        }
        finally
        {
            ArrayPool<BoundType>.Shared.Return(components, clearArray: true);
            ArrayPool<BoundOrigin>.Shared.Return(origins, clearArray: true);
        }
    }

    private sealed class StorageShape
    {
        internal List<Koto> Types { get; } = new();

        internal HashSet<string>? CaseNames { get; set; }

        internal bool HasDestructor { get; set; }
    }
}
