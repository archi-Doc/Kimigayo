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
    private readonly List<BoundType> enumPayloadTypes = new();
    private readonly TypeMemo<BoundType?> completedParts = new(); // CompleteProjections' results.
    private readonly TypeMemo<BoundType> markedParts = new(); // WithUnstatedOrigins' results.

    // The stored Types of a struct or enum instance: its declaration's base, Fields and Case payloads under the instance's
    // substitution. They are substituted once per pass after PrepareStorage refilled the declaration, and on every request before; an
    // instance first requested after the last pass on that request. One substitution, two forms (DECISIONS BU-41): the kept parts
    // (Parts, PartsBase; TyCtxt.StoredParts) with the raw Origins it gives, `self` as Holder and Unstated on each raw layer the declaration
    // writes without `during` (KeptType, DECISIONS BU-46), each associated projection completed as its subject specifies
    // (CompleteProjections, DECISIONS BU-50), which StoredLayer and the hold walk read, and AdtDef's view, the same Types
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

    // SPEC 8.2.3, 5.7 (DECISIONS BU-50): a stored part's Type under an instance (a Field, a Case payload, a base clause) as a reader
    // outside a body reads it (a body reads PartTypeIn): its kept part without the Unstated mark, so every reader gets the one
    // producer's Type (KeptType, its projections completed); null where it does not substitute.
    internal BoundType? StoredType(Koto syntax, BoundType owner) => WrittenType(syntax) is { } written ? this.PartType(written, owner) : null;

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

    // The kept part of a declared Field, Case payload or base clause (Substitute; DECISIONS BU-46, BU-50): its written Type with its
    // completable projections completed (CompleteProjections), then each raw layer it writes without `during` stating Unstated
    // (WithUnstatedOrigins, the one producer of a Type stating Unstated), then under the instance's substitution, completed again for the
    // projections whose subjects the substitution closes, then its closures closed in the instance (a stored Field initializer's closure
    // holds it, DECISIONS BU-60: the closing step is last, on the completed instance); null where it does not substitute or names no Type
    // (a projection that finally names none, whatever its subject: a dependent of Binding's records, so the instance's storage is Rejected,
    // DECISIONS BU-38).
    // `BaseI<Src2<raw/u8>.Item>` is `BaseI<raw/u8>` (SPEC 8.2.3), so its layer is one the declaration writes.
    private BoundType? KeptType(Koto syntax, BoundType owner) => WrittenType(syntax) is { } written ? this.KeptType(written, owner) : null;

    private BoundType? KeptType(BoundType written, BoundType owner)
    {
        if (owner.Symbol is null)
        {
            return null;
        }

        var declaration = owner.Symbol.Declaration;
        if (this.CompleteProjections(written, declaration) is not { } declared)
        {
            return null;
        }

        var stored = this.StoredType(this.WithUnstatedOrigins(declared), owner) is { } substituted ? this.CompleteProjections(substituted, declaration) : null;
        return stored is { ContainsClosure: true } && this.types.Instantiates(owner.Symbol) ? this.CloseHeldClosures(stored, owner) : stored;
    }

    // The read form of a member's declared Type under an instance (StoredType's): KeptType without its marks.
    private BoundType? PartType(BoundType declared, BoundType owner) => this.WithoutUnstated(this.KeptType(declared, owner));

    // SPEC 8.2.3 (DECISIONS BU-50): the read form as a body in `scope` reads it (a member access, a constructor's `self.f` included, a
    // Case payload's construction and pattern, a stored witness Field and its bridge): PartType normalized there as a call's signature is
    // (ContractType), so the use scope's premises complete a projection the storage keeps pending (`HAd<T>`'s `T.Output` is T under
    // `T is PrimitiveInteger`).
    private BoundType? PartTypeIn(BoundType declared, BoundType owner, BindingScope scope)
        => this.PartType(declared, owner) is { } part ? this.ContractType(part, scope) : null;

    private BoundType? PartTypeIn(Koto syntax, BoundType owner, BindingScope scope)
        => WrittenType(syntax) is { } written ? this.PartTypeIn(written, owner, scope) : null;

    // SPEC 5.7, 8.2.3: the Type a member access `node` of `symbol` reads in `scope`: the stored part of the declaring Type member
    // selection published, as a body reads it (PartTypeIn), else the symbol's Type; its `during self` layer read through the access.
    private BoundType? MemberRead(Koto node, BindingSymbol symbol, BindingScope scope)
    {
        var type = symbol.Type is { } declared && node is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) &&
            selection.DeclaringType is { } owner ? this.PartTypeIn(declared, owner, scope) : symbol.Type;
        return type is null ? null : this.ReadHolder(type, node, node);
    }

    // SPEC 8.6.1, 5.7 Calls (DECISIONS BU-50): a part with each associated projection replaced by ProjectionSpecification's outcome: a
    // builtin output or fixed-array iterator Type as it is, a subject's conformance binding as a kept part of the subject (KeptType), so
    // a raw layer the subject's declaration writes (`Src { Item is raw/u8 }`, a base clause's argument) states Unstated and one the
    // subject's argument supplies (`Src2<raw/u8>`, `Item is T`) carries the argument's mark; a pending projection (one with an abstract
    // part, or a binding whose evidence is not ready yet) stays a projection; one that finally names no Type, whatever its subject and
    // reason (SPEC 8.3.3 so ends an expanding or cyclic specification), makes the part null. A part without an abstract part is its own
    // result, and a projection is completed after its components, so a subject that is itself a projection is completed first. A
    // projection is resolved in the scope of the owner's `declaration`, where PrepareStorage binds its parts. A part's result depends on
    // the part alone in one query (its declaration fixed), so each distinct part is completed once (DECISIONS BU-19, BU-55).
    private BoundType? CompleteProjections(BoundType part, Koto declaration)
        => part.HasAbstractPart ? this.CompletedParts(part, declaration, this.completedParts.Begin(this.storageVersion)) : part;

    // A component's result (CompleteProjections), each distinct one with components completed once per query; a leaf is cheaper to
    // complete again than to record.
    private BoundType? CompletedPart(BoundType part, Koto declaration, uint query)
    {
        if (!part.HasAbstractPart)
        {
            return part;
        }

        if (part.Components.Count == 0)
        {
            return this.CompletedParts(part, declaration, query);
        }

        if (!this.completedParts.TryGet(part, query, out var result))
        {
            result = this.CompletedParts(part, declaration, query);
            this.completedParts.Set(part, query, result);
        }

        return result;
    }

    // A part with an abstract part, its components completed first (CompleteProjections), and a closure's held instance (DECISIONS BU-60)
    // with them, given back through its producer (TyCtxt.HeldContext).
    private BoundType? CompletedParts(BoundType part, Koto declaration, uint query)
    {
        var components = this.RentTypes(part.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < part.Components.Count; i++)
            {
                if (this.CompletedPart(part.Components[i], declaration, query) is not { } component)
                {
                    return null;
                }

                components[i] = component;
                changed |= !ReferenceEquals(component, part.Components[i]);
            }

            var parts = components.AsSpan(0, part.Components.Count);
            var completed = changed ? this.types.Rebuild(part, parts, part.Origin, (BoundOrigin[])part.OriginArguments) : part;
            if (TyCtxt.HeldType(part) is { } held)
            {
                if (this.CompletedPart(held, declaration, query) is not { } image)
                {
                    return null;
                }

                completed = ReferenceEquals(image, held) ? completed : this.WithContext(completed, this.types.HeldContext(part.Symbol!, image));
            }

            return completed.Kind == BoundTypeKind.AssociatedProjection ? this.CompletedProjection(completed, declaration) : completed;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private BoundType? CompletedProjection(BoundType projection, Koto declaration)
    {
        var outcome = this.ProjectionSpecification(projection, this.scopes[declaration]);
        if (outcome.Type is not { } specified)
        {
            return outcome.Pending ? projection : null;
        }

        if (outcome.Subject is not { } subject)
        {
            return specified;
        }

        return this.KeptType(specified, subject);
    }

    // SPEC 5.7 Fits, Calls (DECISIONS BU-46): a declared Type with each raw layer it writes without `during` stating Unstated, the Origin
    // such a layer counts as stating, so that a kept part tells a layer a declaration writes from one a Type argument supplies at any depth.
    // Provenance follows the binding: substitution copies a layer with its mark (a Type slot's argument, a component), and a layer formed
    // from a pair binder's binding reads it through TyCtxt.PairBinding, which keeps the mark (`s/T`, and `s/U` in ApplySemantics). A layer
    // stating an Origin, an abstract part but a pair binder's application `s/U` (whose `U` the declaration writes; KeptType has completed
    // the completable projections first, DECISIONS BU-50) and a callable are kept as written; an Unbound layer, a reported error's
    // dependent, is not marked. A declared part is never a Semantics family (a family
    // comes only from an expression's adaptation): one is a broken contract. A part's result depends on the part alone, so each distinct
    // part is marked once per call (DECISIONS BU-19, BU-58).
    private BoundType WithUnstatedOrigins(BoundType declared) => this.MarkedParts(declared, this.markedParts.Begin(this.storageVersion));

    // A component's result (WithUnstatedOrigins), each distinct one with components marked once per query; a leaf is cheaper to mark
    // again than to record.
    private BoundType MarkedPart(BoundType part, uint query)
    {
        if (part.Components.Count == 0)
        {
            return this.MarkedParts(part, query);
        }

        if (!this.markedParts.TryGet(part, query, out var result))
        {
            result = this.MarkedParts(part, query);
            this.markedParts.Set(part, query, result);
        }

        return result;
    }

    // A declared part, its components marked first (WithUnstatedOrigins).
    private BoundType MarkedParts(BoundType declared, uint query)
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
                components[i] = this.MarkedPart(declared.Components[i], query);
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

    // WithUnstatedOrigins' inverse (DECISIONS BU-50): a kept part with each Unstated layer stating no Origin, the Type a body reads, so no
    // MIR local states Unstated (MirValidator's Local row). Only a part carrying a raw Origin is walked.
    private BoundType? WithoutUnstated(BoundType? kept)
    {
        if (kept is null || !kept.CarriesRawOrigin)
        {
            return kept;
        }

        var components = this.RentTypes(kept.Components.Count);
        try
        {
            for (var i = 0; i < kept.Components.Count; i++)
            {
                components[i] = this.WithoutUnstated(kept.Components[i])!;
            }

            var parts = components.AsSpan(0, kept.Components.Count);
            return TyCtxt.RawStated(kept) ? this.types.Pointer(parts[0]) : this.types.Rebuild(kept, parts, kept.Origin, (BoundOrigin[])kept.OriginArguments);
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
                    Koto? failedPayload = null;
                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        if (this.BindType(payload.Operands[j], scope) is null)
                        {
                            failedPayload ??= payload.Operands[j];
                        }
                    }

                    if (failedPayload is null)
                    {
                        this.CompleteOriginDeclaration(originDeclaration);
                    }
                    else
                    {
                        RestPendingOn(originDeclaration, failedPayload);
                    }

                    for (var j = 0; j < payload.Operands.Length; j++)
                    {
                        var syntax = payload.Operands[j];
                        shape.Types.Add(syntax);
                        if (syntax.BoundType is { } type && !this.TypeAccessCovers(type, container.BoundSymbol!, container.BoundSymbol!))
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

    // Substitutes a stored Origin's declared slots and the binder's per-call atoms (PerCallImages). A pair's outer Origin `o`, a negative
    // slot of its binder, is bound through the Type arguments when they are given (SPEC 8.1.1, 8.1.2) and is otherwise kept.
    private BoundOrigin SubstituteStoredOrigin(
        BoundOrigin origin, Koto binder, ReadOnlySpan<BoundOrigin> arguments, PerCallImages images = default, ReadOnlySpan<BoundType?> types = default)
    {
        if (images.Argument(origin) is { } valueArgument)
        {
            return valueArgument;
        }

        if (origin.Kind == OriginKind.Projection && ReferenceEquals(origin.Binder, binder) && images.Reserved(origin.Slot) is { } reserved)
        {
            return reserved;
        }

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

        if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && (uint)origin.Slot < (uint)images.Inputs.Length &&
            images.Inputs[origin.Slot] is { } input)
        {
            return input;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.SubstituteStoredOrigin(origin.Operands[0], binder, arguments, images, types);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.SubstituteStoredOrigin(origin.Operands[i], binder, arguments, images, types));
            }

            return result;
        }

        return origin;
    }

    private BoundType SubstituteStoredOrigins(
        BoundType type, Koto binder, ReadOnlySpan<BoundOrigin> arguments, PerCallImages images = default, Koto? correspondingBinder = null)
    {
        if (arguments.IsEmpty && images.IsEmpty && correspondingBinder is null)
        {
            return type;
        }

        // Without a corresponding binder this rewrites Origins only, so an Origin-free
        // subtree is already its own result; skip its scratch frames and recursion.
        if (!type.CarriesAnyOrigin && correspondingBinder is null)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.SubstituteStoredOrigin(outer, binder, arguments, images) : null;
        if (type.RawOrigin is { } raw)
        {
            origin = this.SubstituteStoredOrigin(raw, binder, arguments, images);
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
                components[i] = this.SubstituteStoredOrigins(type.Components[i], binder, arguments, images, correspondingBinder);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.SubstituteStoredOrigin(type.OriginArguments[i], binder, arguments, images);
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

    // A call's images of its callee binder's per-call atoms (SPEC 15.6.4, 15.8.2; DECISIONS R4-M152, R4-M159): its inputs, in parameter-slot
    // order, and at a value call of a Closure its two reserved atoms', the call receiver ρ (the receiver acquisition's Origin) and the
    // call result (the call's Unbound atom, R4-M118), each built only where the result names its atom; a missing image keeps its atom.
    // A Closure Type's Origin arguments are the images of its definition's (DECISIONS R4-M159): `definition[i]` becomes `value[i]`.
    private readonly ref struct PerCallImages(
        ReadOnlySpan<BoundOrigin> inputs,
        BoundOrigin? receiver,
        BoundOrigin? result,
        ReadOnlySpan<BoundOrigin> definition = default,
        ReadOnlySpan<BoundOrigin> value = default)
    {
        private readonly ReadOnlySpan<BoundOrigin> definition = definition;
        private readonly ReadOnlySpan<BoundOrigin> value = value;

        internal ReadOnlySpan<BoundOrigin> Inputs { get; } = inputs;

        internal bool IsEmpty => this.Inputs.IsEmpty && receiver is null && result is null && this.definition.IsEmpty;

        public static implicit operator PerCallImages(ReadOnlySpan<BoundOrigin> inputs) => new(inputs, null, null);

        public static implicit operator PerCallImages(Span<BoundOrigin> inputs) => new(inputs, null, null);

        // The image of a definition's Origin argument, null for any other atom.
        internal BoundOrigin? Argument(BoundOrigin origin)
        {
            for (var i = 0; i < this.definition.Length; i++)
            {
                if (ReferenceEquals(this.definition[i], origin))
                {
                    return this.value[i];
                }
            }

            return null;
        }

        // The image of the binder's reserved atom at a Projection slot, null for any other slot.
        internal BoundOrigin? Reserved(int slot) => slot switch
        {
            ClosureValueSlot => receiver,
            CallResultSlot => result,
            _ => null,
        };
    }
}
