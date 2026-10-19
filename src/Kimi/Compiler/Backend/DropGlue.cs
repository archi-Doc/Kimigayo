// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the drop glue of a Type (SPEC 16.3, 16.4; R5 package K 4.5 "DropGlue", S2-S4 X3, X23, X27; DECISIONS R5-M60): what destroying a
// value of a closed Type destroys directly, in destruction order, the one enumeration (Parts) the instance collector's DropGlue items target
// and C4a's glue bodies are written from, so the two cannot disagree; and the rows of glue functions by Type (package C 3.6; DECISIONS
// R5-M78), which FunctionCodegen writes through its call writer (PD3): a string's calls the runtime release (PD18's string row, brought
// forward by C0c), every other Type's row is C4a's, written from Parts.
// Input: a closed Type of a compilation and its TyCtxt (Sealed). Output: a glue function's parameters and its Type's routine, or none for a
// row not written yet; the Type's direct parts in destruction order: a declared destructor
// alone for a struct that declares one (its body destroys the Fields and the base, R4-M6b); else a struct's Fields in reverse logical
// declaration order, then its base; a Tuple's elements in decreasing index; a fixed array's element (each element, from the last); an enum's
// Case payload elements, each Case's in reverse declaration order; a Closure environment's captures in reverse initialization order (X23);
// an Array's or Dictionary's element Types, which its legacy Drop row destroys (its ElementGlue need, X4) and lays out (X34); an owning
// handle's payload when the payload is proven Sealed (else the object descriptor's finalizer runs the actual Type's glue, K8). Strings,
// scalars, references, raw pointers, Slices, Function values (their environment's destruction is erased) and InlineStorage, whose owner
// destroys its contents, have none.
// Invariants: deterministic; the parts are the components MirTypes.Destruction descends into, in destruction order, and a Sealed handle's
// payload, which that walk visits without descending.
// Must not: decide whether a part needs a drop (MirTypes.MayNeedDrop, the consumer's), read syntax or proofs other than Sealed, name a glue
// function (ModuleCodegen names its items) or write text.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

/// <summary>What a direct part of a destroyed value is (<see cref="DropGlue.Parts"/>).</summary>
internal enum DropPartKind : byte
{
    Destructor, // The struct's declared destructor, which destroys all the rest (R4-M6b).
    Part, // A component destroyed in place: a Field, the base, an element, a Case payload element or a capture.
    Element, // An Array's or Dictionary's element Type, which its glue destroys and lays out.
    Payload, // A Sealed owning handle's payload, destroyed when the last reference goes.
}

/// <summary>A direct part of a destroyed value.</summary>
/// <param name="Kind">What the part is.</param>
/// <param name="Type">The part's Type, or the struct whose destructor runs.</param>
internal readonly record struct DropPart(DropPartKind Kind, BoundType Type);

/// <summary>The drop glue of the Backend (SPEC 16.3.2).</summary>
internal static class DropGlue
{
    /// <summary>The parameters of a glue function (package C 3.6, as the old generator's glue), named as a function's arguments
    /// (<c>%a</c> and the position): the value's address, then the location a failure inside a release reports (SPEC 22.5.1).</summary>
    internal static readonly AbiParameter[] Parameters =
        [new("ptr", "a0", AbiParameterKind.OwnedSlot, 0), new("ptr", "a1", AbiParameterKind.Location), new("i64", "a2", AbiParameterKind.LocationLength)];

    /// <summary>Gets the runtime routine a glue function calls with its own parameters, by its Type's row: a string's is the runtime release
    /// (the string representation and its release kind, IMPL 21.1.3; PD18).</summary>
    /// <param name="type">The glue item's closed Type.</param>
    /// <returns>The routine, or null for a Type whose row is not written yet (C4a writes them from <see cref="Parts"/>).</returns>
    internal static FunctionAbi? Release(BoundType type) => ReferenceEquals(type, BoundType.String) ? RuntimeAbi.DestroyString : null;

    /// <summary>Appends the direct parts destroying a value of a closed Type destroys, in destruction order (SPEC 16.3.2, 16.4).</summary>
    /// <param name="types">The Type context, whose Sealed proofs decide an owning handle's payload.</param>
    /// <param name="type">The closed Type.</param>
    /// <param name="into">The list the parts are appended to.</param>
    internal static void Parts(TyCtxt types, BoundType type, List<DropPart> into)
    {
        if (AdtDef.Destructor(type) is not null)
        {
            into.Add(new(DropPartKind.Destructor, type));
            return;
        }

        switch (type.Kind)
        {
            case BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure:
                Reversed(type.Components, DropPartKind.Part, into);
                return;
            case BoundTypeKind.Array or BoundTypeKind.Dictionary:
                Reversed(type.Components, DropPartKind.Element, into);
                return;
            case BoundTypeKind.Semantics when type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc:
                if (types.Sealed(null, type.Components[0]))
                {
                    into.Add(new(DropPartKind.Payload, type.Components[0]));
                }

                return;
        }

        if (AdtDef.IsInlineStorage(type))
        {
            return;
        }

        foreach (var payload in AdtDef.CaseTypes(type) ?? [])
        {
            Reversed(payload.Components, DropPartKind.Part, into);
        }

        for (var i = AdtDef.IsStruct(type) ? AdtDef.Count(type) - 1 : -1; i >= 0; i--)
        {
            if (AdtDef.FieldType(type, i) is { } field)
            {
                into.Add(new(DropPartKind.Part, field));
            }
        }

        if (AdtDef.Base(type) is { } parent)
        {
            into.Add(new(DropPartKind.Part, parent));
        }
    }

    private static void Reversed(IReadOnlyList<BoundType> parts, DropPartKind kind, List<DropPart> into)
    {
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            into.Add(new(kind, parts[i]));
        }
    }
}
