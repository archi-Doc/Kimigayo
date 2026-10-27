// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, AdtDef.InstanceStorage> instances = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeclarationContainerKoto> storageDeclarations = new();
    private readonly List<PropertyKoto> storedFields = new();
    private readonly List<BoundEnumCase?> storedCases = new();
    private readonly List<DeclarationContainerKoto> inlineLayoutStack = new();
    private readonly List<bool> inlineLayoutOwnDeclaration = new();
    private readonly List<DeclarationContainerKoto> cyclicInlineLayouts = new();
    private readonly HashSet<BoundType> finiteInlineLayouts = new(ReferenceEqualityComparer.Instance);
    private readonly List<(GenericsKoto Syntax, BoundType Type)> cLayoutInstances = [];
    private readonly List<BoundType> enumPayloadTypes = new();
    private readonly HashSet<(BoundType Projection, BindingScope Scope)> completingProjections = new();
    private bool storagePrepared;

    // The stored Types of a struct or enum instance: its declaration's base, Fields and Case payloads under the instance's
    // substitution. They are substituted once per pass after PrepareStorage refilled the declaration, and on every request before; an
    // instance first requested after the last pass on that request. One substitution, two forms (DECISIONS BU-41): the kept parts
    // (Parts, PartsBase; TyCtxt.StoredParts) with the raw Origins it gives, `self` as Holder and Unstated on each raw layer the declaration
    // writes without `during` (KeptType, DECISIONS BU-46), each associated projection of a struct or enum subject completed by the subject's
    // conformance (CompleteProjections, DECISIONS BU-50), which StoredLayer and the hold walk read, and AdtDef's view, the same Types
    // WithoutRawOrigins (DECISIONS BU-24; design-final §15): an erased instance is often the user's own
    // Type (no Origins, or only `static`), so the erasure of an AdtDef stored Type is the stored Type of the erased instance only then
    // (R5-M39).
    internal AdtDef.InstanceStorage Instance(BoundType type)
    {
        var adt = type.Symbol!.Adt;
        if (!this.instances.TryGetValue(type, out var instance))
        {
            this.instances.Add(type, instance = new());
        }

        if (instance.Version != this.storageVersion || adt?.Version != this.storageVersion)
        {
            this.Substitute(type, adt, instance);
            instance.Version = adt?.Version ?? 0;
        }

        return instance;
    }

    internal BoundType? StoredType(Koto syntax, BoundType owner)
    {
        if (WrittenType(syntax) is not { } field || owner.Symbol is null)
        {
            return null;
        }

        return this.StoredType(field, owner);
    }

    // The Type a Field or Case payload declaration writes, before any instance substitutes it; null for none.
    private static BoundType? WrittenType(Koto? declaration) => declaration?.BoundType ?? (declaration as PropertyKoto)?.BoundSymbol?.Type;

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

    private void Substitute(BoundType type, AdtDef? adt, AdtDef.InstanceStorage instance)
    {
        if (type.Symbol!.Declaration is StructKoto declaration)
        {
            instance.PartsBase = declaration.Bases.Count == 1 ? this.KeptType(declaration.Bases[0], type) : null;
            instance.Base = this.WithoutRawOrigins(instance.PartsBase);
            var count = adt?.Fields.Length ?? 0;
            if (instance.Fields.Length != count)
            {
                (instance.Fields, instance.Parts) = (new BoundType?[count], new BoundType?[count]);
            }

            for (var i = 0; i < count; i++)
            {
                instance.Parts[i] = this.KeptType(adt!.Fields[i], type);
                instance.Fields[i] = this.WithoutRawOrigins(instance.Parts[i]);
            }

            return;
        }

        var cases = adt?.Cases ?? [];
        if (instance.Cases.Length != cases.Length)
        {
            (instance.Cases, instance.Parts) = (new BoundType?[cases.Length], new BoundType?[cases.Length]);
        }

        for (var i = 0; i < cases.Length; i++)
        {
            instance.Parts[i] = cases[i] is { } selected ? this.PayloadTuple(selected, type) : null;
            instance.Cases[i] = this.WithoutRawOrigins(instance.Parts[i]);
        }
    }

    // The kept part of a declared Field, Case payload or base clause (Substitute): its written Type with each raw layer it writes without
    // `during` stating Unstated (WithUnstatedOrigins), under the instance's substitution, each associated projection of a struct or enum
    // subject completed as a kept part of its subject (CompleteProjections); null where it does not substitute. The one producer of a
    // Type stating Unstated (DECISIONS BU-46, BU-50).
    private BoundType? KeptType(Koto syntax, BoundType owner)
        => WrittenType(syntax) is { } written && owner.Symbol is not null ? this.KeptType(written, owner) : null;

    private BoundType? KeptType(BoundType written, BoundType owner)
        => this.StoredType(this.WithUnstatedOrigins(written), owner) is not { } stored ? null
            : stored.HasAbstractPart ? this.CompleteProjections(stored, this.scopes[owner.Symbol!.Declaration]) : stored;

    // SPEC 8.6.1, 5.7 Calls (DECISIONS BU-50): a kept part with each associated projection of a struct or enum subject replaced by what the
    // subject specifies (ProjectionSpecification): a builtin output or fixed-array iterator Type as it is, a conformance binding as a kept
    // part of the subject (KeptType), so a raw layer the subject's declaration writes (`Src { Item is raw/u8 }`, a base clause's argument)
    // states Unstated and one the subject's argument supplies (`Src2<raw/u8>`, `Item is T`) carries the argument's mark; a projection whose
    // subject is a parameter, or whose conformance binds nothing yet, stays a projection. A part without an abstract part is its own
    // result, and a projection is completed after its components, so a subject that is itself a projection is completed first; a binding
    // that exposes another projection is completed again, each projection once at a time per owner scope (completingProjections, as
    // ContractTypeCore's guard). `scope` is the owner declaration's, where PrepareStorage binds its parts.
    private BoundType CompleteProjections(BoundType part, BindingScope scope)
    {
        if (!part.HasAbstractPart)
        {
            return part;
        }

        var components = this.RentTypes(part.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < part.Components.Count; i++)
            {
                components[i] = this.CompleteProjections(part.Components[i], scope);
                changed |= !ReferenceEquals(components[i], part.Components[i]);
            }

            var completed = changed ? this.types.Rebuild(part, components.AsSpan(0, part.Components.Count), part.Origin, (BoundOrigin[])part.OriginArguments) : part;
            return completed.Kind == BoundTypeKind.AssociatedProjection ? this.CompletedProjection(completed, scope) : completed;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private BoundType CompletedProjection(BoundType projection, BindingScope scope)
    {
        if (!this.completingProjections.Add((projection, scope)))
        {
            return projection;
        }

        try
        {
            return this.ProjectionSpecification(projection, scope, out var subject) is not { } specified ? projection
                : subject is null ? specified : this.KeptType(specified, subject) ?? projection;
        }
        finally
        {
            this.completingProjections.Remove((projection, scope));
        }
    }

    // SPEC 5.7 Fits, Calls (DECISIONS BU-46): a declared Type with each raw layer it writes without `during` stating Unstated, the Origin
    // such a layer counts as stating, so that a kept part tells a layer a declaration writes from one a Type argument supplies at any depth.
    // Provenance follows the binding: substitution copies a layer with its mark (a Type slot's argument, a component), and a layer formed
    // from a pair binder's binding reads it through TyCtxt.PairBinding, which keeps the mark (`s/T`, and `s/U` in ApplySemantics). A layer
    // stating an Origin, an abstract part but a pair binder's application `s/U` (whose `U` the declaration writes) and a callable are kept
    // as written; an Unbound layer, a reported error's dependent, is not marked. A declared part is never a Semantics family (a family
    // comes only from an expression's adaptation): one is a broken contract.
    private BoundType WithUnstatedOrigins(BoundType declared)
    {
        if (declared.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            throw new InvalidOperationException($"A declared part `{declared}` is a Semantics family (InternalInvariant; DECISIONS BU-46).");
        }

        if (declared.Components.Count == 0 || (declared.IsAbstract && declared.Kind != BoundTypeKind.SemanticsApplication) ||
            declared.Kind is BoundTypeKind.Function or BoundTypeKind.Closure or BoundTypeKind.FunctionItem)
        {
            return declared;
        }

        var components = this.RentTypes(declared.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < declared.Components.Count; i++)
            {
                components[i] = this.WithUnstatedOrigins(declared.Components[i]);
                changed |= !ReferenceEquals(components[i], declared.Components[i]);
            }

            var parts = components.AsSpan(0, declared.Components.Count);
            if (declared is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, RawOrigin: null })
            {
                return this.types.Pointer(parts[0], BoundOrigin.Unstated);
            }

            return changed ? this.types.Rebuild(declared, parts, declared.Origin, (BoundOrigin[])declared.OriginArguments) : declared;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // A Case's payload Types under an instance's substitution, with the raw Origins it gives, as one Tuple; null when one does not
    // substitute.
    private BoundType? PayloadTuple(BoundEnumCase selected, BoundType type)
    {
        this.enumPayloadTypes.Clear();
        foreach (var syntax in selected.Payload)
        {
            if (this.KeptType(syntax, type) is not { } payload)
            {
                this.enumPayloadTypes.Clear();
                return null;
            }

            this.enumPayloadTypes.Add(payload);
        }

        var tuple = this.types.Tuple(CollectionsMarshal.AsSpan(this.enumPayloadTypes));
        this.enumPayloadTypes.Clear();
        return tuple;
    }

    // SPEC 21.3.2: a struct or enum that contains itself by value (through stored Fields and inline
    // bases, payloads, Tuple/array components and other by-value containers, under the substitution
    // each use supplies) has no finite inline layout; a reference or object Semantics layer is an
    // indirection. Every declaration on such a cycle is diagnosed once and published as AdtDef.InlineCycle.
    private void ValidateInlineLayouts()
    {
        this.cyclicInlineLayouts.Clear();
        this.finiteInlineLayouts.Clear();
        foreach (var container in this.storageDeclarations)
        {
            container.BoundSymbol!.Adt!.InlineCycle = false;
            if (container.BoundSymbol.Type is { } type)
            {
                this.inlineLayoutStack.Clear();
                this.inlineLayoutOwnDeclaration.Clear();
                this.VisitInlineLayout(type);
            }
        }

        for (var i = 0; i < this.cyclicInlineLayouts.Count; i++)
        {
            this.cyclicInlineLayouts[i].BoundSymbol!.Adt!.InlineCycle = true;
            this.Fail(this.cyclicInlineLayouts[i], BindingFailure.InvalidInlineLayout);
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

        if ((!AdtDef.IsStruct(type) && !AdtDef.IsEnum(type)) || type.Symbol!.Adt is not { } shape)
        {
            return; // Parameters, primitives, references, objects, Slices and pointers store no inline copy of this Type.
        }

        var container = (DeclarationContainerKoto)type.Symbol.Declaration;
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
        this.storageDeclarations.Clear();
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not (StructKoto or EnumKoto))
            {
                continue;
            }

            var container = (DeclarationContainerKoto)this.nodes[n];
            var shape = container.BoundSymbol!.Adt ??= new();
            shape.Types.Clear();
            shape.DestructorKoto = null;
            this.storedFields.Clear();
            this.storedCases.Clear();
            this.storageDeclarations.Add(container);
            var scope = this.scopes[container];
            if (container is EnumKoto && (container.Bases.Count != 0 || container.NestedContainers.Count != 0 || (container.Modifier & ModifierKind.Open) != 0))
            {
                this.Fail(container, BindingFailure.InvalidTypeFormation);
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
                if (member is PropertyKoto field && IsStoredVariable(field))
                {
                    if (container is EnumKoto)
                    {
                        this.Fail(member, BindingFailure.InvalidTypeFormation);
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
                    this.storedFields.Add(field);
                }
                else if (TryEnumPayload(member, out var payload))
                {
                    if (member.AttributeChain is not null)
                    {
                        this.Fail(member, BindingFailure.InvalidTypeFormation);
                    }

                    if (member.BoundSymbol?.EnumCase is { } enumeration)
                    {
                        enumeration.Ordinal = this.storedCases.Count;
                    }

                    this.storedCases.Add(member.BoundSymbol?.EnumCase);

                    // SPEC 15.3.3: the Case's sets and clauses bind every payload together, as a Field's bind its Type. As for a
                    // Field, a payload Type that failed leaves the clauses unbound, so they add no record derived from that failure.
                    var originDeclaration = this.BeginOriginDeclaration(member, scope);
                    var payloadsBound = true;
                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        payloadsBound &= this.BindType(payload.Operands[j], scope) is not null;
                    }

                    if (payloadsBound)
                    {
                        this.CompleteOriginDeclaration(originDeclaration);
                    }

                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        var syntax = payload.Operands[j];
                        shape.Types.Add(syntax);
                        if (syntax.BoundType is { } type && !TypeAccessCovers(type, container.BoundSymbol!, container.BoundSymbol!))
                        {
                            this.Fail(member, BindingFailure.Access);
                        }
                    }
                }
                else if (member is FunctionKoto { IsDestructor: true } destructor)
                {
                    shape.DestructorKoto ??= destructor;
                    if (container is EnumKoto)
                    {
                        this.Fail(member, BindingFailure.InvalidTypeFormation);
                    }
                }
                else if (container is EnumKoto && member is not (FunctionKoto { IsConstructor: false, IsDestructor: false } or IsKoto or EffectBoundKoto or
                    SyntaxFormKoto { Akind: KotoKind.ConditionalConformance or KotoKind.AssociatedType }))
                {
                    // An effect item is judged by its owner alone (Binding.Misplaced); it leaves the enum's formation valid.
                    this.Fail(member, BindingFailure.InvalidTypeFormation);
                }
            }

            if (container is EnumKoto && this.storedCases.Count == 0)
            {
                this.Fail(container, BindingFailure.InvalidTypeFormation);
            }

            shape.SetParts(this.storedFields, this.storedCases);
            shape.Version = this.storageVersion;
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

    // Substitutes a stored Origin's declared slots and inputs. A pair's outer Origin `o`, a negative slot of its binder, is bound
    // through the Type arguments when they are given (SPEC 8.1.1, 8.1.2) and is otherwise kept.
    private BoundOrigin SubstituteStoredOrigin(BoundOrigin origin, Koto binder, ReadOnlySpan<BoundOrigin> arguments, ReadOnlySpan<BoundOrigin> inputs = default, ReadOnlySpan<BoundType?> types = default)
    {
        if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder))
        {
            if ((uint)origin.Slot < (uint)arguments.Length && arguments[origin.Slot] is { } argument)
            {
                return argument;
            }

            if (origin.Slot < 0 && this.PairOuterImage(origin, binder, types) is { } image)
            {
                return image;
            }
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

        if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && (uint)origin.Slot < (uint)inputs.Length && inputs[origin.Slot] is { } input)
        {
            return input;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.SubstituteStoredOrigin(origin.Operands[0], binder, arguments, inputs, types);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.SubstituteStoredOrigin(origin.Operands[i], binder, arguments, inputs, types));
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
        if (!type.CarriesAnyOrigin && correspondingBinder is null)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.SubstituteStoredOrigin(outer, binder, arguments, inputs) : null;
        if (type.RawOrigin is { } raw)
        {
            origin = this.SubstituteStoredOrigin(raw, binder, arguments, inputs);
        }

        var length = correspondingBinder is null ? type.LengthExpression : this.CorrespondingLength(type.LengthExpression, binder, correspondingBinder);
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        var lengths = correspondingBinder is null ? type.LengthArguments : this.lengthScratch.Rent(type.LengthArguments.Length);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin ?? type.RawOrigin) || !ReferenceEquals(length, type.LengthExpression);
            if (correspondingBinder is not null)
            {
                for (var i = 0; i < type.LengthArguments.Length; i++)
                {
                    lengths[i] = this.CorrespondingLength(type.LengthArguments[i], binder, correspondingBinder);
                    changed |= !ReferenceEquals(lengths[i], type.LengthArguments[i]);
                }
            }

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

            return changed ? this.types.Intern(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), length, type.ClosureContext, lengths.AsSpan(0, type.LengthArguments.Length)) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            if (correspondingBinder is not null)
            {
                this.lengthScratch.Return(lengths, clearArray: true);
            }
        }
    }
}
