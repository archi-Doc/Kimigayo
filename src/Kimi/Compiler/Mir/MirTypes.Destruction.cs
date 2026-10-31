// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The walk's visitor belongs to the Type facts.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What destroying a part the destruction walk reports runs (SPEC 16.3.1-16.3.3; <see cref="MirTypes.Destroys"/>), the one
/// classification its readers share (DECISIONS R4-M150).</summary>
internal enum MirDestroyedPart : byte
{
    Releases, // A string's, an Array's or a Dictionary's storage is released; its elements are parts of their own.
    Drop, // A struct's declared drop runs (R4-M6b: its body alone destroys the parts).
    Erased, // An erased destruction (SPEC 16.3.3): a Function value's environment, or an owning handle whose payload is not proven Sealed.
    Payload, // An owning handle whose payload is proven Sealed: its payload is destroyed, which the walk goes on into below the handle.
    Abstract, // An abstract part: its instance decides; each reader applies the Copy proof (SPEC 16.3.3 "unless the Type is Copy").
}

/// <summary>What the destruction walk of a Type reports (<see cref="MirTypes.Destruction"/>).</summary>
internal interface IMirDestruction
{
    /// <summary>Whether the walk enters a Type it meets within its depth bound: by interned identity, so a repeating recursive Type ends
    /// where it repeats and a Type several parts share is walked once per query (DECISIONS R4-M58), or once per shallower meeting
    /// (<see cref="MirDepths"/>).</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The depth the walk meets it at.</param>
    /// <returns>Whether the walk enters it.</returns>
    bool First(BoundType type, int depth);

    /// <summary>A part of the destroyed value whose destruction runs or releases something.</summary>
    /// <param name="type">The part's Type.</param>
    /// <param name="depth">The part's depth in the walk, from which a walk of what the part owns goes on (a Sealed handle's payload).</param>
    /// <returns>Whether the walk stops.</returns>
    bool Visit(BoundType type, int depth);

    /// <summary>A part past the walk's depth bound, whose destruction the walk does not follow: the walk's own outcome, which a check
    /// resting on it records as OwnershipDepthLimit_Kd (DECISIONS R4-M153).</summary>
    /// <param name="type">The part's Type.</param>
    /// <returns>Whether the walk stops.</returns>
    bool Deep(BoundType type);
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

    /// <summary>Gets what destroying a value of a Type may observe of the Origins and Loans it holds (SPEC 15.6.6, 16.3.3), as the walks'
    /// answer (<see cref="MirOutcome"/>): its <see cref="Destruction"/> reaches a struct that declares a drop, which observes all it reaches,
    /// a Function value, whose destruction is unknown, or an owning handle whose payload is not proven Sealed, or is and observes, since
    /// any rc or arc release may be final (decided, where the walk stops); an abstract part not proven Copy, whose instance decides
    /// (Limit, DECISIONS R4-M147); and a part past the walk's bound (Deep, DECISIONS R4-M153). The answer is the Join of its parts' (the
    /// walk goes on past Limit and Deep), so it is the same in every order of the parts: decided whatever an unknown part is, and Limit
    /// beside a part past the bound. A payload is walked below its handle: a recursive Type ends, exactly where it repeats and at the walk's
    /// bound where it grows.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose Copy and Sealed proofs Binding publishes.</param>
    /// <param name="definition">The definition whose premises prove Copy and Sealed.</param>
    /// <param name="entered">Scratch for the depth each Type was entered at (<see cref="MirDepths"/>), cleared first.</param>
    /// <returns>What the destruction may observe.</returns>
    internal static MirOutcome Observation(BoundType? type, TyCtxt types, BindingSymbol? definition, Dictionary<BoundType, byte> entered)
    {
        entered.Clear();
        var observer = new Observer(types, definition, entered);
        Destruction(type, ref observer);
        return observer.Answer;
    }

    /// <summary>Classifies a part <see cref="Destruction"/> reports (SPEC 16.3.1-16.3.3): an abstract Type; a Function value, erased; an
    /// owning handle, whose payload is destroyed where it is proven Sealed and any more-derived destructor may run otherwise (erased); a
    /// struct's declared drop; else a storage release.</summary>
    /// <param name="type">The reported part's Type.</param>
    /// <param name="types">The Type context, whose Sealed proofs Binding publishes.</param>
    /// <param name="definition">The definition whose premises prove Sealed.</param>
    /// <returns>What destroying the part runs.</returns>
    internal static MirDestroyedPart Destroys(BoundType type, TyCtxt types, BindingSymbol? definition) => type switch
    {
        { IsAbstract: true } => MirDestroyedPart.Abstract,
        { Kind: BoundTypeKind.Function } => MirDestroyedPart.Erased,
        { Kind: BoundTypeKind.Semantics } => types.Sealed(definition, type.Components[0]) ? MirDestroyedPart.Payload : MirDestroyedPart.Erased,
        _ => AdtDef.Destructor(type) is not null ? MirDestroyedPart.Drop : MirDestroyedPart.Releases,
    };

    /// <summary>Walks what destroying a value of a Type does (SPEC 16.3), the one walk of its inline storage that drop elaboration and the
    /// effect summaries share. It reports each part that runs or releases something: an abstract Type (its instance decides), an owning
    /// handle, a Function value (its environment), a string, an Array or a Dictionary, a struct that declares a drop (whose destructor body
    /// alone destroys its parts, R4-M6b), and as Deep a part past depth 32 (Bound, the holding walks' too); it walks on into the components
    /// of a Tuple, fixed array, Closure, Array or Dictionary, a struct's Fields and base and an enum's Case payloads; a Field or Case Binding
    /// did not substitute (null; <see cref="AdtDef.CaseTypes"/>) destroys nothing, a dependent of Binding's rejection. References, raw
    /// pointers, Slices, scalars and an InlineStorage, whose owner destroys its contents (<see cref="AdtDef.IsInlineStorage"/>), release
    /// nothing. Within the bound the visitor decides whether it enters a Type (<see cref="IMirDestruction.First"/>): once per query, or
    /// once per shallower meeting (<see cref="MirDepths"/>, at most once per depth), so the walk is linear in the distinct Types it meets,
    /// and a growing Type, new at each level, still ends at the bound.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The walk's depth.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Destruction<TVisitor>(BoundType? type, ref TVisitor visitor, int depth = 0)
        where TVisitor : struct, IMirDestruction
    {
        if (type is null)
        {
            return false;
        }

        if (depth > Bound)
        {
            return visitor.Deep(type);
        }

        if (!visitor.First(type, depth))
        {
            return false;
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

    // MayNeedDrop's visitor: any reported part, each Type once per query, past the bound too (a Type met before reports nothing).
    private readonly struct AnyPart(HashSet<BoundType> seen) : IMirDestruction
    {
        public bool First(BoundType type, int depth) => seen.Add(type);

        public bool Visit(BoundType type, int depth) => true;

        public bool Deep(BoundType type) => seen.Add(type);
    }

    // Observation's visitor: the Join of the reported parts' outcomes, a part that may observe decided (the walk stops), an abstract part
    // not proven Copy Limit, after which the walk goes on, since another part may decide, and Deep where a part past the walk's depth
    // bound has a Type the walk never enters within it (MirDepths). A string's or a sequence's own release observes nothing; its elements
    // are parts of their own.
    private struct Observer(TyCtxt types, BindingSymbol? definition, Dictionary<BoundType, byte> entered) : IMirDestruction
    {
        internal readonly MirOutcome Answer => this.Outcome.Join(entered.ContainsValue(MirDepths.Past) ? MirOutcome.Deep : MirOutcome.None);

        private MirOutcome Outcome { get; set; }

        public readonly bool First(BoundType type, int depth)
            => MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(entered, type, out _), depth);

        public bool Visit(BoundType type, int depth)
        {
            switch (Destroys(type, types, definition))
            {
                case MirDestroyedPart.Abstract:
                    this.Outcome = this.Outcome.Join(types.Copy(definition, type) ? MirOutcome.None : MirOutcome.Limit);
                    return false;
                case MirDestroyedPart.Drop or MirDestroyedPart.Erased:
                    return this.Decide();
                case MirDestroyedPart.Payload:
                    return Destruction(type.Components[0], ref this, depth + 1);
                default:
                    return false;
            }
        }

        public readonly bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(entered, type, out _));
            return false;
        }

        // A part known to observe: the decided answer, the Join's top, where the walk stops.
        private bool Decide()
        {
            this.Outcome = this.Outcome.Join(MirOutcome.Decided);
            return true;
        }
    }
}
