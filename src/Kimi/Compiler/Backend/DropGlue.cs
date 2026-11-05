// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the drop glue of a Type (SPEC 16.3, 16.4; R5 package K 4.5 "DropGlue", package C 3.6, S2-S4 X3, X23, X27; DECISIONS R5-M60, R5-M78,
// R5-M95): what destroying a value of a closed Type destroys directly, in destruction order, the one enumeration (Parts) the instance
// collector's DropGlue items target and FunctionCodegen writes their functions from, so the two cannot disagree; and the row of a Type's
// glue function (Row), which FunctionCodegen writes through its call writer (PD3): a string's calls the runtime release (PD18's string
// row, C0c); a struct's, Tuple's, enum's, fixed array's or Closure environment's destroys its parts (C4a, C5a); an erased function's
// calls its drop entry (C5b), an owning handle's releases its object (C6b); an Array's and a Dictionary's are their legacy Drop rows'
// (C8d, C8e), one row each.
// Input: a closed Type of a compilation and its TyCtxt (Sealed). Output: a Type's row, the string row's routine and a glue function's
// parameters; the Type's direct parts in destruction order, each with its position in its aggregate: a declared destructor alone for a
// struct that declares one (its body destroys the Fields and the base, R4-M6b); else a struct's Fields in reverse logical declaration order,
// then its base; a Tuple's elements in decreasing index; a fixed array's element (each element, from the last); an enum's Case payload
// elements, each Case's in reverse declaration order; a Closure environment's captures in reverse initialization order (X23); an Array's
// or Dictionary's element Types, which its legacy Drop row destroys (its ElementGlue need, X4; a Dictionary's through the clearEntries
// instance its Drop row declares, its glue's one target, DECISIONS R5-M124) and lays out (X34); an owning handle's
// payload when the payload is proven Sealed (else the object descriptor's finalizer runs the actual Type's glue, K8). Strings, scalars,
// references, raw pointers, Slices, Function values (their environment's destruction is erased) and InlineStorage, whose owner destroys
// its contents, have none.
// Invariants: deterministic; the parts are the components MirTypes.Destruction descends into, in destruction order, and a Sealed handle's
// payload, which that walk visits without descending (InstanceCollectorTest.DropGlueParts agree); a position is the component's index in
// the aggregate writer's numbering (FunctionCodegen.Component: a struct's base first, then its Fields; a Tuple's elements; a Case
// payload's elements under the Case's ordinal).
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

/// <summary>How the glue function of a closed Type is written (<see cref="DropGlue.Row"/>; package C 3.6).</summary>
internal enum DropGlueRow : byte
{
    Release, // A string: the runtime release, called with the glue's own parameters (IMPL 21.1.3, PD18).
    Parts, // A struct, Tuple, enum, fixed array or Closure environment: its parts' glue, one level, in destruction order (a Closure's captures
    // in reverse initialization order, X23); a declared destructor alone (R4-M6b).
    Function, // An erased function value's environment, through its table's drop entry: C5b.
    Handle, // An obj, rc or arc release through ObjectRuntime, a Sealed payload's glue called directly (DECISIONS R5-M112).
    Array, // An Array's legacy Drop row, which destroys its elements by their glue (DECISIONS R5-M121).
    Dictionary, // A Dictionary's legacy Drop row, which calls the clearEntries instance its glue targets (DECISIONS R5-M124).
}

/// <summary>A direct part of a destroyed value.</summary>
/// <param name="Kind">What the part is.</param>
/// <param name="Type">The part's Type, or the struct whose destructor runs.</param>
/// <param name="Ordinal">A Case payload element's Case ordinal; 0 for any other part.</param>
/// <param name="Index">The component's index in its aggregate (the aggregate writer's numbering: a struct's base 0 and its Fields after it,
/// a Tuple's or a Case payload's elements, a capture); -1 for a fixed array's element, which stands for every element, and for a part
/// without a position (a destructor, a collection's element Type, a Sealed payload).</param>
internal readonly record struct DropPart(DropPartKind Kind, BoundType Type, int Ordinal = 0, int Index = -1);

/// <summary>The drop glue of the Backend (SPEC 16.3.2).</summary>
internal static class DropGlue
{
    /// <summary>The parameters of a glue function (package C 3.6, as the old generator's glue), named as the old generator's glue names
    /// them, <c>slot</c>, <c>location</c> and <c>length</c>, which ObjectRuntime's release text reads (DECISIONS R5-M112): the value's
    /// address, then the location a failure inside a release reports (SPEC 22.5.1).</summary>
    internal static readonly AbiParameter[] Parameters =
    [
        new("ptr", "slot", AbiParameterKind.OwnedSlot, 0), new("ptr", "location", AbiParameterKind.Location),
        new("i64", "length", AbiParameterKind.LocationLength),
    ];

    /// <summary>Gets the runtime routine of the string row (<see cref="DropGlueRow.Release"/>), which its glue calls with its own parameters
    /// (the string representation and its release kind, IMPL 21.1.3; PD18).</summary>
    internal static FunctionAbi Release => RuntimeAbi.DestroyString;

    /// <summary>Gets the row of a closed Type's glue function, by the kinds <see cref="Mir.MirTypes.MayNeedDrop"/> admits.</summary>
    /// <param name="type">The glue item's closed Type.</param>
    /// <returns>The row; null for a kind no glue item has, a broken contract of the glue item.</returns>
    internal static DropGlueRow? Row(BoundType type)
    {
        if (ReferenceEquals(type, BoundType.String))
        {
            return DropGlueRow.Release;
        }

        return type.Kind switch
        {
            BoundTypeKind.Nominal or BoundTypeKind.Constructed => DropGlueRow.Parts, // A struct or an enum.
            BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure => DropGlueRow.Parts, // A structural aggregate.
            BoundTypeKind.Function => DropGlueRow.Function,
            BoundTypeKind.Semantics => DropGlueRow.Handle,
            BoundTypeKind.Array => DropGlueRow.Array,
            BoundTypeKind.Dictionary => DropGlueRow.Dictionary,
            _ => null,
        };
    }

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
            case BoundTypeKind.Tuple or BoundTypeKind.Closure:
                Reversed(type.Components, DropPartKind.Part, 0, into);
                return;
            case BoundTypeKind.FixedArray:
                into.Add(new(DropPartKind.Part, type.Components[0])); // Every element, from the last.
                return;
            case BoundTypeKind.Array or BoundTypeKind.Dictionary:
                Reversed(type.Components, DropPartKind.Element, 0, into);
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

        var cases = AdtDef.CaseTypes(type) ?? [];
        for (var ordinal = 0; ordinal < cases.Length; ordinal++)
        {
            if (cases[ordinal] is { } payload)
            {
                Reversed(payload.Components, DropPartKind.Part, ordinal, into);
            }
        }

        var parent = AdtDef.Base(type);
        for (var i = AdtDef.IsStruct(type) ? AdtDef.Count(type) - 1 : -1; i >= 0; i--)
        {
            if (AdtDef.FieldType(type, i) is { } field)
            {
                into.Add(new(DropPartKind.Part, field, 0, parent is null ? i : i + 1));
            }
        }

        if (parent is not null)
        {
            into.Add(new(DropPartKind.Part, parent, 0, 0));
        }
    }

    // A Tuple's, Closure's, Case payload's or collection's components, the last first, each at its index (an Element at none).
    private static void Reversed(IReadOnlyList<BoundType> parts, DropPartKind kind, int ordinal, List<DropPart> into)
    {
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            into.Add(new(kind, parts[i], ordinal, kind == DropPartKind.Part ? i : -1));
        }
    }
}
