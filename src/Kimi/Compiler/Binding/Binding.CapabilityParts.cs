// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The parts a capability judgment decomposes a Type into (SPEC 15.2.3, 7.7.4, 6.1.3; DECISIONS BU-73).
// - Responsibility: JudgedByDeclaration, whether a judgment reads a Type's declaration or its parts; CapabilityParts, the one
//   enumeration of a Type's parts where no declaration judges it, with each part's member: its components, a requirement Item's
//   Contract reference (RequirementReference) replaced by that reference's Type arguments, the inherited parameters of the Item of a
//   function nested in a body, then its Origin parts and the inherited Origin slots; EnvironmentTuple, the one form of an environment's
//   parts as a Type, which CapabilityParts reads back.
// - Inputs: complete Types: a judgment's subject, an erasure's callable; a nested function's enclosing declarations.
// - Outputs: the parts in one numbering with their members; interned environment Tuples.
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

    // The parts a capability judgment decomposes a Type into when no declaration judges it (SPEC 15.2.3): its components, a requirement
    // Item's Contract reference replaced by that reference's Type arguments (Argument), and, for an Item of a function nested in a body, its
    // inherited parameters' Types (SPEC 6.1.3, "even an unused one"; the owner's O-249 Q4 (a)); then its Origin parts: that reference's
    // Origin arguments (a Contract's inherited Origins), or an environment Tuple's (EnvironmentTuple), and the nested function's inherited
    // Origin slots. An environment's parts have this one numbering for the erasure and the Owned proof alike (DECISIONS BU-73: a Closure's
    // capture index). StructuralCapability, ErasureSubject and the component summaries' walk read this one enumeration.
    private ref struct CapabilityParts(BoundType type)
    {
        private readonly BoundType type = type;
        private readonly BoundType? reference = RequirementReference(type);
        private readonly Koto? body = type is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto function } ? EnclosingBody(function) : null;
        private int index = -1;
        private int argument = -1;
        private int origin = -1;
        private int phase; // 0 components, 1 inherited Types, 2 Origin parts, 3 inherited Origin slots
        private Koto? scope;
        private int slot = -1;

        // A Type part, or an Origin part (Part null); Argument for a requirement reference's Type argument.
        public (BoundType? Part, BoundOrigin? Origin, bool Argument) Current { readonly get; private set; }

        // The current part's member: its component's index; a requirement reference's Type and Origin arguments are member 1; an
        // environment Tuple's Origin part follows its components; an inherited parameter is the Item's member Components.Count, beside
        // what its signature names (CallSignature).
        public readonly int Member => this.phase is 1 or 3 ? this.type.Components.Count
            : this.argument >= 0 || (this.phase == 2 && this.reference is not null) ? 1
            : this.phase == 2 ? this.type.Components.Count + this.origin : this.index;

        // The Origin parts: a requirement reference's Origin arguments, or an environment Tuple's, read by EnvironmentTuple's invariant
        // that only an environment Tuple has Origin arguments.
        private readonly IReadOnlyList<BoundOrigin> Origins => this.reference?.OriginArguments ?? (this.type.Kind == BoundTypeKind.Tuple ? this.type.OriginArguments : []);

        // The part at an ordinal of this enumeration, an environment Tuple's member (ErasureSubject), with its member here.
        public static (BoundType? Part, BoundOrigin? Origin, int Member) At(BoundType type, int ordinal)
        {
            var parts = new CapabilityParts(type);
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

                (this.phase, this.scope) = (1, this.body);
            }

            if (this.phase == 1)
            {
                if (this.NextInheritedType())
                {
                    return true;
                }

                this.phase = 2;
            }

            if (this.phase == 2)
            {
                if (++this.origin < this.Origins.Count)
                {
                    this.Current = (null, this.Origins[this.origin], false);
                    return true;
                }

                (this.phase, this.scope, this.slot) = (3, this.body, -1);
            }

            return this.NextInheritedOrigin();
        }

        // SPEC 6.1.3: an inherited parameter's Type: each generic slot of every function enclosing the body, upward, then of the nearest
        // enclosing Container, whose Schema holds its outer Containers' slots; a length slot adds no Owned part (SPEC 15.2.3).
        private bool NextInheritedType()
        {
            for (; this.scope is not null; this.scope = this.scope is DeclarationContainerKoto ? null : this.scope.Parent, this.slot = -1)
            {
                var slots = this.scope.BoundSymbol?.Schema?.GenericSlots ?? [];
                while (++this.slot < slots.Count)
                {
                    if (slots[this.slot] is { Kind: not GenericSlotKind.Length, Symbol.WholeType: { } whole })
                    {
                        this.Current = (whole, null, false);
                        return true;
                    }
                }
            }

            return false;
        }

        // SPEC 6.1.3, 15.3: an inherited Origin slot, the nearest enclosing Container's (with its outer Containers'); a function has no
        // Origin parameter list, so an enclosing function's Origin is a name its signature may use (InheritedEnvironment).
        private bool NextInheritedOrigin()
        {
            for (; this.scope is not null; this.scope = this.scope.Parent, this.slot = -1)
            {
                if (this.scope is DeclarationContainerKoto container)
                {
                    var slots = container.BoundSymbol?.Schema?.Origins ?? [];
                    if (++this.slot < slots.Count)
                    {
                        this.Current = (null, slots[this.slot].Origin, false);
                        return true;
                    }

                    this.scope = null;
                    return false;
                }
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
