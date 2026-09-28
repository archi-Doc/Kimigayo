// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<DeclarationContainerKoto, StorageShape> storageShapes = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeclarationContainerKoto> inlineLayoutStack = new();
    private readonly List<bool> inlineLayoutOwnDeclaration = new();
    private readonly List<DeclarationContainerKoto> cyclicInlineLayouts = new();
    private readonly HashSet<BoundType> finiteInlineLayouts = new(ReferenceEqualityComparer.Instance);
    private readonly List<StructKoto> cLayouts = [];
    private readonly List<(GenericsKoto Syntax, BoundType Type)> cLayoutInstances = [];
    private readonly List<BoundType> enumPayloadTypes = new();
    private bool storagePrepared;

    internal bool PrepareEnumCases(BoundType type)
    {
        var count = Compiler.EnumStorage.Count(type);
        if (type.StoredCases?.Length != count)
        {
            type.StoredCases = new BoundType[count];
        }

        for (var i = 0; i < count; i++)
        {
            this.enumPayloadTypes.Clear();
            foreach (var syntax in Compiler.EnumStorage.Case(type, i)!.Payload)
            {
                if (this.StoredType(syntax, type) is not { } payload)
                {
                    return false;
                }

                this.enumPayloadTypes.Add(payload);
            }

            type.StoredCases[i] = this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, CollectionsMarshal.AsSpan(this.enumPayloadTypes));
        }

        this.enumPayloadTypes.Clear();
        return true;
    }

    internal IReadOnlyList<Koto>? EnumStorage(BoundType type)
        => type.Symbol?.Declaration is EnumKoto declaration && this.storageShapes.TryGetValue(declaration, out var shape) ? shape.Types : null;

    internal BoundType? StoredType(Koto syntax, BoundType owner)
    {
        if ((syntax.BoundType ?? (syntax as PropertyKoto)?.BoundSymbol?.Type) is not { } field || owner.Symbol is null)
        {
            return null;
        }

        return this.StoredType(field, owner);
    }

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

    // SPEC 21.3.5: a struct or enum that contains itself by value (through stored Fields and inline
    // bases, payloads, Tuple/array components and other by-value containers, under the substitution
    // each use supplies) has no finite inline layout; a reference or object Semantics layer is an
    // indirection. Every declaration on such a cycle is diagnosed once.
    private void ValidateInlineLayouts()
    {
        this.cyclicInlineLayouts.Clear();
        this.finiteInlineLayouts.Clear();
        foreach (var container in this.storageShapes.Keys)
        {
            if (container.BoundSymbol?.Type is { } type)
            {
                this.inlineLayoutStack.Clear();
                this.inlineLayoutOwnDeclaration.Clear();
                this.VisitInlineLayout(type);
            }
        }

        for (var i = 0; i < this.cyclicInlineLayouts.Count; i++)
        {
            Fail(this.cyclicInlineLayouts[i], BindingFailure.InvalidInlineLayout);
        }
    }

    private void VisitInlineLayout(BoundType? type)
    {
        if (type is null || this.finiteInlineLayouts.Contains(type))
        {
            return;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                this.VisitInlineLayout(type.Components[i]);
            }

            return;
        }

        if ((!StructStorage.IsStruct(type) && !Compiler.EnumStorage.IsEnum(type)) || type.Symbol!.Declaration is not DeclarationContainerKoto container || !this.storageShapes.TryGetValue(container, out var shape))
        {
            return; // Parameters, primitives, references, objects, Slices and pointers store no inline copy of this Type.
        }

        var index = this.inlineLayoutStack.IndexOf(container);
        if (index >= 0)
        {
            // The cycle's members are the declarations reached as themselves; a generic container reached
            // under an instantiation (Box<E> inside E) merely stores the offending Type argument.
            for (var i = index; i < this.inlineLayoutStack.Count; i++)
            {
                if (this.inlineLayoutOwnDeclaration[i] && !this.cyclicInlineLayouts.Contains(this.inlineLayoutStack[i]))
                {
                    this.cyclicInlineLayouts.Add(this.inlineLayoutStack[i]);
                }
            }

            return;
        }

        // The declaration itself is visited with its declared stored Types (its own parameters stay open);
        // an instantiation substitutes them.
        var own = ReferenceEquals(type, container.BoundSymbol?.Type);
        this.inlineLayoutStack.Add(container);
        this.inlineLayoutOwnDeclaration.Add(own);
        var cycles = this.cyclicInlineLayouts.Count;
        for (var i = 0; i < shape.Types.Count; i++)
        {
            this.VisitInlineLayout(own ? shape.Types[i].BoundType : this.StoredType(shape.Types[i], type));
        }

        this.inlineLayoutStack.RemoveAt(this.inlineLayoutStack.Count - 1);
        this.inlineLayoutOwnDeclaration.RemoveAt(this.inlineLayoutOwnDeclaration.Count - 1);
        if (this.cyclicInlineLayouts.Count == cycles)
        {
            this.finiteInlineLayouts.Add(type);
        }
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

        this.ValidateEnumProjections();
    }

    private BoundType? StoredType(BoundType field, BoundType owner)
    {
        var binder = owner.Symbol!.Declaration;
        var substituted = this.SubstituteType(field, binder, (BoundType[])owner.Components);
        if (substituted is null)
        {
            return null;
        }

        if (owner.Kind == BoundTypeKind.Slice && owner.Origin is { } origin)
        {
            // The one Slice slot is passed on the stack: a conditional with an array branch would allocate it per call.
            ReadOnlySpan<BoundOrigin> single = [origin];
            return this.SubstituteStoredOrigins(substituted, binder, single);
        }

        return this.SubstituteStoredOrigins(substituted, binder, (BoundOrigin[])owner.OriginArguments);
    }

    private BoundOrigin SubstituteStoredOrigin(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundOrigin> arguments, ReadOnlySpan<BoundOrigin> inputs = default)
    {
        if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder) && origin.Slot < arguments.Length && arguments[origin.Slot] is { } argument)
        {
            return argument;
        }

        if (origin.Kind == OriginKind.Parameter && binder is DeclarationContainerKoto && binder.BoundSymbol?.Schema is { } schema)
        {
            for (var i = 0; i < schema.Origins.Count && i < arguments.Length; i++)
            {
                if (ReferenceEquals(schema.Origins[i].Origin, origin) && arguments[i] is { } inherited)
                {
                    return inherited;
                }
            }
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

        // Without a corresponding binder this rewrites Origins only, so an Origin-free
        // subtree is already its own result; skip its scratch frames and recursion.
        if (!type.CarriesOrigin && correspondingBinder is null)
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
