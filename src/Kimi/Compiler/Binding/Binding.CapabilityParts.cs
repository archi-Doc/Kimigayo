// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The parts a capability judgment decomposes a Type into (SPEC 15.2.3, 7.7.4, 6.1.3, 3.2.1; DECISIONS BU-73).
// - Responsibility: JudgedByDeclaration, whether a judgment reads a Type's declaration or its parts; CapabilityParts, the one
//   enumeration of a Type's parts where no declaration judges it, with each part's member: its components, a requirement Item's
//   Contract reference (RequirementReference) replaced by that reference's Type arguments, a nested definition's inherited Type
//   arguments, then its Origin parts and the inherited Origin arguments; InheritedArguments, a nested definition's inherited arguments
//   read through its held instance; EnvironmentTuple, the one form of an environment's parts as a Type, which CapabilityParts reads back.
// - Inputs: complete Types: a judgment's subject, an erasure's callable; a nested definition's chain of enclosing declarations
//   (TyCtxt.EnclosingDeclaration) and its held instance as its readers see it (TyCtxt.NestedInstance).
// - Outputs: the parts in one numbering with their members; interned environment Tuples; InheritedArguments' null where the instance
//   is withheld (PLAN G300, the owner's O-262 (a)) or an image does not complete, which each reader answers with BU-22's located limit:
//   at the withheld definition (StructuralCapability; ErasureSubject through it), at the stored Type (the component summaries' walk).
// - Invariants: the erasure and the Owned proof read one numbering (DECISIONS BU-73: a Closure's capture index); an environment Tuple
//   is the only Tuple with Origin arguments, a capability subject and never a value's Type.
// - Prohibitions: no judgment here (StructuralCapability, ErasureSubject and the component summaries' walk judge the parts); no second
//   enumeration of a Type's parts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Whether a capability judgment reads a Type's declaration (a struct, enum or other container) rather than its parts (SPEC 4.5: a
    // collection by its element Types, whatever its Symbol).
    private static bool JudgedByDeclaration(BoundType type)
        => type.Kind is not (BoundTypeKind.Array or BoundTypeKind.Dictionary) && type.Symbol?.Declaration is DeclarationContainerKoto;

    // A requirement Item's Contract reference: metadata, of which only the bound Type arguments are retained bindings.
    private static BoundType? RequirementReference(BoundType type)
        => type.Kind == BoundTypeKind.FunctionItem && type.Symbol?.Declaration is FunctionKoto { IsRequirement: true } && type.Components.Count > 1
            ? type.Components[1] : null;

    // An environment's parts as one Type (SPEC 7.7.4, 15.2.3): a Tuple of its Type parts whose Origin arguments are its Origin parts, which
    // CapabilityParts reads back in this order; Unit when it has none. Invariant: it is the only Tuple whose Origin arguments are Origin
    // parts, and the only producer of a Tuple with Origin arguments (TyCtxt.Tuple gives none); it is a capability subject (the erasure's,
    // an Item's signature part), never a value's Type. A new producer of a Tuple with Origin arguments must not reach CapabilityParts.
    private BoundType EnvironmentTuple(ReadOnlySpan<BoundType> types, ReadOnlySpan<BoundOrigin> origins)
        => types.IsEmpty && origins.IsEmpty ? BoundType.Unit : this.types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, types, originArguments: origins);

    // SPEC 6.1.3, 15.2.3, 3.2.1 (PLAN G846, G856; the owner's O-249 Q4 (a) and O-258 (a)): a nested definition's inherited arguments, the
    // arguments that the instance it was formed in binds to the parameters of the declarations enclosing it, used or not, as one
    // environment Tuple (EnvironmentTuple). For a Closure Type or the Item of a function declared in a body: each generic slot (a length
    // slot adds no Owned part) of every declaration on the chain TyCtxt.EnclosingDeclaration publishes (a named function, an accessor's
    // function; never a call's callee or an anonymous function), up to the nearest Container, whose identity (SelfType) holds its outer
    // Containers' slots and its Origin slots (a function has no Origin parameter list, SPEC 15.3), each read through the Type's held
    // instance as its readers see it (TyCtxt.NestedInstance): the slot itself in that declaration's identity (no context), else its image.
    // Unit for any other Type, whose bound arguments are its whole dependence; null where the instance is withheld (an Origin position
    // of the held instance or of the holding Container's identity, PLAN G300) or an image does not complete, which the reader answers
    // with BU-22's limit.
    private BoundType? InheritedArguments(BoundType type)
    {
        if (type.Kind is not (BoundTypeKind.Closure or BoundTypeKind.FunctionItem) || type.Symbol is not { } definition ||
            this.types.EnclosingDeclaration(definition) is not { } enclosing)
        {
            return BoundType.Unit;
        }

        // Withheld (DECISIONS BU-60; PLAN G300): a held instance with an Origin position (NestedInstance), and every read of a
        // definition a Container's instance holds when that Container has an Origin slot, so that its identity has an Origin position:
        // a held instance is formed modulo Origins, so no context tells that Container's instances apart, `self`'s from another's
        // (the owner's O-262 (a)). A definition a function's instance holds is read in that function's frame and is exact.
        var instance = TyCtxt.NestedInstance(type);
        if ((instance is null && type.InstanceContext is not null) ||
            enclosing is { Declaration: DeclarationContainerKoto, Schema.Origins.Count: > 0 })
        {
            return null;
        }

        var typeCount = 0;
        var originCount = 0;
        for (var declaration = enclosing; declaration is not null; declaration = this.NextEnclosing(declaration))
        {
            typeCount += declaration.Schema?.GenericSlots.Count ?? 0;
            originCount += declaration.Declaration is DeclarationContainerKoto ? declaration.Schema?.Origins.Count ?? 0 : 0;
        }

        if (typeCount + originCount == 0)
        {
            return BoundType.Unit;
        }

        var types = this.RentTypes(typeCount);
        var origins = this.originScratch.Rent(originCount);
        try
        {
            var t = 0;
            var o = 0;
            for (var declaration = enclosing; declaration is not null; declaration = this.NextEnclosing(declaration))
            {
                if (declaration.Schema is not { } schema || schema.GenericSlots.Count + schema.Origins.Count == 0)
                {
                    continue;
                }

                if (declaration.Declaration is DeclarationContainerKoto)
                {
                    // The Container's identity under the instance: its arguments are its slots' images.
                    if ((instance is null ? this.SelfType(declaration) : this.types.Apply(this.SelfType(declaration), instance)) is not { } self)
                    {
                        return null;
                    }

                    for (var i = 0; i < self.Components.Count; i++)
                    {
                        types[t++] = self.Components[i];
                    }

                    for (var i = 0; i < self.OriginArguments.Count; i++)
                    {
                        origins[o++] = self.OriginArguments[i];
                    }

                    continue;
                }

                for (var i = 0; i < schema.GenericSlots.Count; i++)
                {
                    if (schema.GenericSlots[i] is { Kind: not GenericSlotKind.Length, Symbol.WholeType: { } whole })
                    {
                        if ((instance is null ? whole : this.types.Apply(whole, instance)) is not { } image)
                        {
                            return null;
                        }

                        types[t++] = image;
                    }
                }
            }

            return this.EnvironmentTuple(types.AsSpan(0, t), origins.AsSpan(0, o));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // The next declaration on a nested definition's chain of enclosing declarations: none above a Container, whose identity holds its outer
    // Containers' slots.
    private BindingSymbol? NextEnclosing(BindingSymbol declaration)
        => declaration.Declaration is DeclarationContainerKoto ? null : this.types.EnclosingDeclaration(declaration.Scope);

    // The parts a capability judgment decomposes a Type into when no declaration judges it (SPEC 15.2.3): its components, a requirement
    // Item's Contract reference replaced by that reference's Type arguments (Argument), then a nested definition's inherited Type
    // arguments (InheritedArguments' Tuple, which the caller passes: Unit for a judgment that reads none); then its Origin parts: that
    // reference's Origin arguments (a Contract's inherited Origins), or an environment Tuple's (EnvironmentTuple), then the inherited
    // Origin arguments. An environment's parts have this one numbering for the erasure and the Owned proof alike (DECISIONS BU-73: a
    // Closure's capture index). StructuralCapability, ErasureSubject and the component summaries' walk read this one enumeration.
    private ref struct CapabilityParts(BoundType type, BoundType inherited)
    {
        private readonly BoundType type = type;
        private readonly BoundType inherited = inherited;
        private readonly BoundType? reference = RequirementReference(type);
        private int index = -1;
        private int argument = -1;
        private int origin = -1;
        private int slot = -1;
        private int phase; // 0 components, 1 inherited Type arguments, 2 Origin parts, 3 inherited Origin arguments

        // A Type part, or an Origin part (Part null); Argument for a requirement reference's Type argument.
        public (BoundType? Part, BoundOrigin? Origin, bool Argument) Current { readonly get; private set; }

        // The current part's member: its component's index; a requirement reference's Type and Origin arguments are member 1; an
        // environment Tuple's Origin part follows its components; an inherited argument is member Components.Count, beside the call
        // signature (CallSignature).
        public readonly int Member => this.phase is 1 or 3 ? this.type.Components.Count
            : this.argument >= 0 || (this.phase == 2 && this.reference is not null) ? 1
            : this.phase == 2 ? this.type.Components.Count + this.origin : this.index;

        // The Origin parts: a requirement reference's Origin arguments, or an environment Tuple's, read by EnvironmentTuple's invariant
        // that only an environment Tuple has Origin arguments.
        private readonly IReadOnlyList<BoundOrigin> Origins => this.reference?.OriginArguments ?? (this.type.Kind == BoundTypeKind.Tuple ? this.type.OriginArguments : []);

        // The part at an ordinal of this enumeration, an environment Tuple's member (ErasureSubject), with its member here.
        public static (BoundType? Part, BoundOrigin? Origin, int Member) At(BoundType type, BoundType inherited, int ordinal)
        {
            var parts = new CapabilityParts(type, inherited);
            for (var i = 0; parts.MoveNext(); i++)
            {
                if (i == ordinal)
                {
                    return (parts.Current.Part, parts.Current.Origin, parts.Member);
                }
            }

            throw new InvalidOperationException($"`{type}` has no part {ordinal} (InternalInvariant; BU-73).");
        }

        public readonly CapabilityParts GetEnumerator() => this;

        public bool MoveNext()
        {
            if (this.phase == 0)
            {
                if (this.NextComponent())
                {
                    return true;
                }

                this.phase = 1;
            }

            if (this.phase == 1)
            {
                if (++this.slot < this.inherited.Components.Count)
                {
                    this.Current = (this.inherited.Components[this.slot], null, false);
                    return true;
                }

                (this.phase, this.slot) = (2, -1);
            }

            if (this.phase == 2)
            {
                if (++this.origin < this.Origins.Count)
                {
                    this.Current = (null, this.Origins[this.origin], false);
                    return true;
                }

                this.phase = 3;
            }

            if (++this.slot < this.inherited.OriginArguments.Count)
            {
                this.Current = (null, this.inherited.OriginArguments[this.slot], false);
                return true;
            }

            return false;
        }

        private bool NextComponent()
        {
            if (this.argument >= 0 && ++this.argument < this.reference!.Components.Count)
            {
                this.Current = (this.reference.Components[this.argument], null, true);
                return true;
            }

            this.argument = -1;
            while (++this.index < this.type.Components.Count)
            {
                if (this.index != 1 || this.reference is null)
                {
                    this.Current = (this.type.Components[this.index], null, false);
                    return true;
                }

                if (this.reference.Components.Count != 0)
                {
                    this.argument = 0;
                    this.Current = (this.reference.Components[0], null, true);
                    return true;
                }
            }

            return false;
        }
    }
}
