// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The walk's visitor belongs to the Type facts.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

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

    /// <summary>A struct or enum instance the walk entered below an indirection (<see cref="MirTypes.Below"/>), whose Fields, Cases and
    /// base the walk never reads there: only an indirection lets a declaration's instances grow (inline storage is finite), so a reader of
    /// declarations' destruction summaries composes the instance through its arguments, a fixed point per declaration that needs no depth
    /// (SPEC 16.3.3, 15.3.5, 15.6.4); any other visitor walks its parts from the inline position (<see cref="MirTypes.Entered"/>).</summary>
    /// <param name="instance">The instance, which <see cref="First"/> entered.</param>
    /// <param name="depth">Its depth in the walk.</param>
    /// <returns>Whether the walk stops.</returns>
    bool Declared(BoundType instance, int depth);
}

// The destruction walk, its two positions (inline, and below an indirection, entered only by Below, where a struct or enum instance is
// Declared to the visitor and never walked by its storage; Entered, the dispatch after First, walks one from the inline position), and the
// fact over it whether a value may need a drop. What a destruction may observe is A5's (MirBorrows.Accesses), which reads A4's summaries.
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

    /// <summary>Walks what destroying a value of a Type does (SPEC 16.3), the one walk of its inline storage that drop elaboration and the
    /// effect summaries share. It reports each part that runs or releases something: an abstract Type (its instance decides), an owning
    /// handle, a Function value (its environment), a string, an Array or a Dictionary, a struct that declares a drop (whose destructor body
    /// alone destroys its parts, R4-M6b), and as Deep a part past depth 32 (Bound, the holding walks' too); it walks on into the components
    /// of a Tuple, fixed array, Closure, Array or Dictionary, a struct's Fields and base and an enum's Case payloads; a Field or Case Binding
    /// did not substitute (null; <see cref="AdtDef.CaseTypes"/>) destroys nothing, a dependent of Binding's rejection. References, raw
    /// pointers, Slices, scalars and an InlineStorage, whose owner destroys its contents (<see cref="AdtDef.IsInlineStorage"/>), release
    /// nothing. Within the bound the visitor decides whether it enters a Type (<see cref="IMirDestruction.First"/>): once per query, or
    /// once per shallower meeting (<see cref="MirDepths"/>, at most once per depth), so the walk is linear in the distinct Types it meets,
    /// and a growing Type, new at each level, still ends at the bound. A value's parts lie in its position, one level deeper; a sequence's
    /// elements and a Sealed payload lie below an indirection (<see cref="Below"/>).</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The walk's depth.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Destruction<TVisitor>(BoundType? type, ref TVisitor visitor, int depth = 0)
        where TVisitor : struct, IMirDestruction
        => Walk(type, ref visitor, depth, indirect: false);

    /// <summary>Walks a value of a Type that lies below an indirection, at a depth (SPEC 16.3.3, 6.x): a Sealed handle's payload, which
    /// each visitor's <see cref="IMirDestruction.Visit"/> passes here one level below its handle, an Array's or a Dictionary's element one
    /// level below its sequence (<see cref="Parts"/>), and a composed summary's atom at its instance's depth. The one entry to the walk's
    /// indirect position, which the walk's own recursion carries: there each struct or enum instance <see cref="IMirDestruction.First"/>
    /// enters goes to <see cref="IMirDestruction.Declared"/>, and the walk reads none of its Fields, Cases or base.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type; null destroys nothing.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The value's depth in the walk.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Below<TVisitor>(BoundType? type, ref TVisitor visitor, int depth)
        where TVisitor : struct, IMirDestruction
        => Walk(type, ref visitor, depth, indirect: true);

    /// <summary>Walks a Type the walk has entered (<see cref="IMirDestruction.First"/>) from the inline position: <see cref="Destruction"/>'s
    /// dispatch after <c>First</c>, extracted once (PLAN G349 U2): <see cref="IMirDestruction.Visit"/> for an abstract part, a Function
    /// value, an owning handle, a string, a sequence or a struct with a drop, else its <see cref="Parts"/>. Its callers are
    /// <see cref="Destruction"/> and a visitor's <see cref="IMirDestruction.Declared"/> that walks an instance per instance (A5's Composer
    /// until PLAN G349 U3, and test visitors); a reader of declarations' summaries composes instead and never calls it (its premise: no
    /// atom's instance is walked by its storage).</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The entered Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The Type's depth in the walk.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Entered<TVisitor>(BoundType type, ref TVisitor visitor, int depth)
        where TVisitor : struct, IMirDestruction
        => Dispatch(type, ref visitor, depth, indirect: false);

    /// <summary>Gets the payload an owning handle's destruction walks on (SPEC 16.3.3): its View Target where Binding proves it Sealed,
    /// whose own destruction runs; otherwise null, an erased destruction, whatever the View Target is: an abstract one too, proven Copy or
    /// not, since a more-derived Type opts into Copy on its own (DECISIONS R4-M170). The one classification of a handle's payload that A4's
    /// effect summaries and A5's destructions read.</summary>
    /// <param name="handle">The owning handle's Type.</param>
    /// <param name="types">The Type context, whose Sealed proofs Binding publishes.</param>
    /// <param name="definition">The definition whose premises prove Sealed.</param>
    /// <returns>The payload the walk goes on into, or null.</returns>
    internal static BoundType? SealedPayload(BoundType handle, TyCtxt types, BindingSymbol? definition)
        => types.Sealed(definition, handle.Components[0]) ? handle.Components[0] : null;

    /// <summary>Walks the parts a value of a Type destroys after its own release (SPEC 16.3.2): a Tuple's, fixed array's, Closure's, Array's
    /// or Dictionary's components, a struct's Fields and base and an enum's Case payloads, each by <see cref="Destruction"/> one level
    /// deeper. <see cref="Destruction"/> walks them past every part but a struct that declares a drop, whose destructor body destroys them
    /// (R4-M6b); a visitor that composes the drops a destruction runs walks them there itself.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The Type's depth in the walk.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Parts<TVisitor>(BoundType type, ref TVisitor visitor, int depth)
        where TVisitor : struct, IMirDestruction
        => Descend(type, ref visitor, depth, indirect: false);

    // The one walk: null destroys nothing, past the bound Deep, then First; in the indirect position a struct or enum instance is Declared,
    // else the dispatch.
    private static bool Walk<TVisitor>(BoundType? type, ref TVisitor visitor, int depth, bool indirect)
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

        return indirect && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ? visitor.Declared(type, depth) : Dispatch(type, ref visitor, depth, indirect);
    }

    // An entered Type: the part Visit reports, then its parts unless a declared drop destroys them (R4-M6b) or it is an InlineStorage.
    private static bool Dispatch<TVisitor>(BoundType type, ref TVisitor visitor, int depth, bool indirect)
        where TVisitor : struct, IMirDestruction
    {
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

        return !destructor && !AdtDef.IsInlineStorage(type) && Descend(type, ref visitor, depth, indirect);
    }

    // A value's parts one level deeper, in its position; a sequence's elements below an indirection.
    private static bool Descend<TVisitor>(BoundType type, ref TVisitor visitor, int depth, bool indirect)
        where TVisitor : struct, IMirDestruction
    {
        var sequence = type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary; // Its elements lie below an indirection.
        var parts = sequence || type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure
            ? (IReadOnlyList<BoundType?>)type.Components : AdtDef.CaseTypes(type) ?? [];
        for (var i = 0; i < parts.Count; i++)
        {
            if (Walk(parts[i], ref visitor, depth + 1, sequence || indirect))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Walk(AdtDef.FieldType(type, i), ref visitor, depth + 1, indirect))
            {
                return true;
            }
        }

        return AdtDef.Base(type) is { } parent && Walk(parent, ref visitor, depth + 1, indirect);
    }

    // MayNeedDrop's visitor: any reported part, each Type once per query, past the bound too (a Type met before reports nothing). It
    // stops at an indirection's handle or sequence, a reported part, before any instance below it.
    private readonly struct AnyPart(HashSet<BoundType> seen) : IMirDestruction
    {
        public bool First(BoundType type, int depth) => seen.Add(type);

        public bool Visit(BoundType type, int depth) => true;

        public bool Deep(BoundType type) => seen.Add(type);

        public bool Declared(BoundType instance, int depth)
            => throw new InvalidOperationException("MayNeedDrop's walk stops at an indirection's handle or sequence.");
    }
}
