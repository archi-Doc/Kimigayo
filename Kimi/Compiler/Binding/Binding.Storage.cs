// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
            shape.CaseCount = 0;
            shape.HasDestructor = false;
            var scope = this.scopes[container];
            if (container is EnumKoto && (container.Bases.Count != 0 || container.NestedContainers.Count != 0 || (container.Modifier & ModifierKind.Open) != 0))
            {
                Fail(container, BindingFailure.InvalidTypeFormation);
            }

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
                    if (container is EnumKoto)
                    {
                        Fail(member, BindingFailure.InvalidTypeFormation);
                    }

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
                    if (member.AttributeChain is not null)
                    {
                        Fail(member, BindingFailure.InvalidTypeFormation);
                    }

                    if (member.BoundSymbol?.EnumCase is { } enumeration)
                    {
                        enumeration.Ordinal = shape.CaseCount;
                    }

                    shape.CaseCount++;

                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        var syntax = payload.Operands[j];
                        this.BindType(syntax, scope);
                        shape.Types.Add(syntax);
                        if (syntax.BoundType is { } type && !TypeAccessCovers(type, container.BoundSymbol!, container.BoundSymbol!))
                        {
                            Fail(member, BindingFailure.Access);
                        }
                    }
                }
                else if (member is FunctionKoto { IsDestructor: true })
                {
                    shape.HasDestructor = true;
                    if (container is EnumKoto)
                    {
                        Fail(member, BindingFailure.InvalidTypeFormation);
                    }
                }
                else if (container is EnumKoto && member is not (FunctionKoto { IsConstructor: false, IsDestructor: false } or IsKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance or KotoKind.AssociatedType }))
                {
                    Fail(member, BindingFailure.InvalidTypeFormation);
                }
            }

            if (container is EnumKoto && shape.CaseCount == 0)
            {
                Fail(container, BindingFailure.InvalidTypeFormation);
            }
        }
    }

    private BoundType? StoredType(Koto syntax, BoundType owner)
    {
        if (syntax.BoundType is not { } field || owner.Symbol is null)
        {
            return null;
        }

        return this.StoredType(field, owner);
    }

    private BoundType? StoredType(BoundType field, BoundType owner)
    {
        var binder = owner.Symbol!.Declaration;
        var substituted = this.SubstituteType(field, binder, (BoundType[])owner.Components);
        return substituted is null ? null : this.SubstituteStoredOrigins(substituted, binder, (BoundOrigin[])owner.OriginArguments);
    }

    private BoundOrigin SubstituteStoredOrigin(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundOrigin> arguments, ReadOnlySpan<BoundOrigin> inputs = default)
    {
        if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder) && origin.Slot < arguments.Length && arguments[origin.Slot] is { } argument)
        {
            return argument;
        }

        if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && origin.Slot < inputs.Length && inputs[origin.Slot] is { } input)
        {
            return input;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.SubstituteStoredOrigin(origin.Operands[0], binder, arguments, inputs);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.SubstituteStoredOrigin(origin.Operands[i], binder, arguments, inputs));
            }

            return result;
        }

        return origin;
    }

    private BoundType SubstituteStoredOrigins(BoundType type, Koto binder, ReadOnlySpan<BoundOrigin> arguments, ReadOnlySpan<BoundOrigin> inputs = default, Koto? correspondingBinder = null)
    {
        if (arguments.IsEmpty && inputs.IsEmpty && correspondingBinder is null)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.SubstituteStoredOrigin(outer, binder, arguments, inputs) : null;
        var length = correspondingBinder is null ? type.LengthExpression : this.CorrespondingLength(type.LengthExpression, binder, correspondingBinder);
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin) || !ReferenceEquals(length, type.LengthExpression);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.SubstituteStoredOrigins(type.Components[i], binder, arguments, inputs, correspondingBinder);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.SubstituteStoredOrigin(type.OriginArguments[i], binder, arguments, inputs);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), length) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private sealed class StorageShape
    {
        internal List<Koto> Types { get; } = new();

        internal int CaseCount { get; set; }

        internal bool HasDestructor { get; set; }
    }
}
