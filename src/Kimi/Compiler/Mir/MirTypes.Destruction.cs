// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The walk's visitor belongs to the Type facts.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What destroying a value may observe of the Origins and Loans it holds (<see cref="MirTypes.Observation"/>), from its
/// destruction walk: one class per Type and definition.</summary>
internal enum MirObservation : byte
{
    /// <summary>Nothing: no part it destroys observes.</summary>
    None,

    /// <summary>Only an abstract part not proven Copy may observe, whose instance decides (DECISIONS R4-M147).</summary>
    Unproven,

    /// <summary>A part known to observe: a struct that declares a drop, a Function value, or an owning handle whose payload is not proven
    /// Sealed or observes.</summary>
    Observes,

    /// <summary>A part past the walk's depth bound, which this implementation does not follow (SPEC 8.15): the caller's located
    /// Unsupported_Kd.</summary>
    Deep,
}

/// <summary>What the destruction walk of a Type reports (<see cref="MirTypes.Destruction"/>).</summary>
internal interface IMirDestruction
{
    /// <summary>Whether the query meets a Type for the first time: the walk enters each Type once per query, by interned identity, so a
    /// repeating recursive Type ends where it repeats and a Type several parts share is walked once (DECISIONS R4-M58).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the walk enters it.</returns>
    bool First(BoundType type);

    /// <summary>A part of the destroyed value whose destruction runs or releases something.</summary>
    /// <param name="type">The part's Type.</param>
    /// <param name="depth">The part's depth in the walk, from which a walk of what the part owns goes on (a Sealed handle's payload).</param>
    /// <returns>Whether the walk stops.</returns>
    bool Visit(BoundType type, int depth);

    /// <summary>A part past the walk's depth bound, whose destruction the walk does not follow.</summary>
    /// <returns>Whether the walk stops.</returns>
    bool Deep();
}

// The destruction walk and the facts over it: whether a value may need a drop, and whether its destruction may observe what it holds.
internal static partial class MirTypes
{
    /// <summary>Gets whether a value of a Type may own what its destruction releases (SPEC 16.3), the one rule by which the builder drops
    /// an old value: whether its <see cref="Destruction"/> reports a part.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="seen">Scratch for the Types walked, cleared first.</param>
    /// <returns>Whether drop elaboration has to consider the value.</returns>
    internal static bool MayNeedDrop(BoundType? type, HashSet<BoundType> seen)
    {
        seen.Clear();
        var any = new AnyPart(seen);
        return Destruction(type, ref any);
    }

    /// <summary>Gets what destroying a value of a Type may observe of the Origins and Loans it holds (SPEC 15.6.6, 16.3.3): its
    /// <see cref="Destruction"/> reaches a struct that declares a drop, which observes all it reaches, a Function value, whose destruction
    /// is unknown, or an owning handle whose payload is not proven Sealed, or is and observes, since any rc or arc release may be final
    /// (Observes); else only an abstract part not proven Copy, whose instance decides (Unproven); and a part past the walk's bound is Deep
    /// whatever else it reaches. The walk goes on past an abstract part, so a Type holding one and a part past the bound is Deep (DECISIONS
    /// R4-M147). A payload is walked below its handle: a recursive Type ends, exactly where it repeats and at the walk's bound where it
    /// grows.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose Copy and Sealed proofs Binding publishes.</param>
    /// <param name="definition">The definition whose premises prove Copy and Sealed.</param>
    /// <param name="seen">Scratch for the Types walked, cleared first.</param>
    /// <returns>What the destruction may observe.</returns>
    internal static MirObservation Observation(BoundType? type, TyCtxt types, BindingSymbol? definition, HashSet<BoundType> seen)
    {
        seen.Clear();
        var observer = new Observer(types, definition, seen);
        var known = Destruction(type, ref observer);
        return observer.Beyond ? MirObservation.Deep : known ? MirObservation.Observes : observer.Unproven ? MirObservation.Unproven : MirObservation.None;
    }

    /// <summary>Walks what destroying a value of a Type does (SPEC 16.3), the one walk of its inline storage that drop elaboration and the
    /// effect summaries share. It reports each part that runs or releases something: an abstract Type (its instance decides), an owning
    /// handle, a Function value (its environment), a string, an Array or a Dictionary, a struct that declares a drop (whose destructor body
    /// alone destroys its parts, R4-M6b), and as Deep a part past depth 32; it walks on into the components of a Tuple, fixed array,
    /// Closure, Array or Dictionary, a struct's Fields and base and an enum's Case payloads; a Field or Case Binding did not substitute
    /// (null; <see cref="AdtDef.CaseTypes"/>) destroys nothing, a dependent of Binding's rejection. References, raw pointers, Slices, scalars and
    /// an InlineStorage, whose owner destroys its contents (<see cref="AdtDef.IsInlineStorage"/>), release nothing. It enters each Type
    /// once per query (<see cref="IMirDestruction.First"/>): a Type met again was walked already, so the walk is linear in the distinct
    /// Types it meets, and a growing Type, new at each level, still ends at the bound.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The walk's depth.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Destruction<TVisitor>(BoundType? type, ref TVisitor visitor, int depth = 0)
        where TVisitor : struct, IMirDestruction
    {
        if (type is null || !visitor.First(type))
        {
            return false;
        }

        if (depth > 32)
        {
            return visitor.Deep();
        }

        if (type.IsAbstract || type.Kind == BoundTypeKind.Function)
        {
            return visitor.Visit(type, depth);
        }

        if (type.Kind == BoundTypeKind.Semantics)
        {
            return type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && visitor.Visit(type, depth);
        }

        var sequence = type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary; // A managed sequence releases its storage and its elements.
        var destructor = AdtDef.Destructor(type) is not null;
        if ((sequence || ReferenceEquals(type, BoundType.String) || destructor) && visitor.Visit(type, depth))
        {
            return true;
        }

        if (destructor || AdtDef.IsInlineStorage(type))
        {
            return false;
        }

        var parts = sequence || type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure
            ? (IReadOnlyList<BoundType?>)type.Components : AdtDef.CaseTypes(type) ?? [];
        for (var i = 0; i < parts.Count; i++)
        {
            if (Destruction(parts[i], ref visitor, depth + 1))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Destruction(AdtDef.FieldType(type, i), ref visitor, depth + 1))
            {
                return true;
            }
        }

        return AdtDef.Base(type) is { } parent && Destruction(parent, ref visitor, depth + 1);
    }

    // MayNeedDrop's visitor: any reported part.
    private readonly struct AnyPart(HashSet<BoundType> seen) : IMirDestruction
    {
        public bool First(BoundType type) => seen.Add(type);

        public bool Visit(BoundType type, int depth) => true;

        public bool Deep() => true;
    }

    // Observation's visitor: a reported part that may observe, and past the walk's depth bound one it does not follow; an abstract part not
    // proven Copy is noted and the walk goes on, since another part may be known to observe. A string's or a sequence's own release
    // observes nothing; its elements are parts of their own.
    private struct Observer(TyCtxt types, BindingSymbol? definition, HashSet<BoundType> seen) : IMirDestruction
    {
        internal bool Beyond { get; private set; }

        internal bool Unproven { get; private set; }

        public readonly bool First(BoundType type) => seen.Add(type);

        public bool Visit(BoundType type, int depth)
        {
            if (type.IsAbstract)
            {
                this.Unproven |= !types.Copy(definition, type);
                return false;
            }

            if (type.Kind == BoundTypeKind.Function || AdtDef.Destructor(type) is not null)
            {
                return true;
            }

            var payload = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : null;
            return payload is not null && (!types.Sealed(definition, payload) || Destruction(payload, ref this, depth + 1));
        }

        public bool Deep()
        {
            this.Beyond = true;
            return true;
        }
    }
}
