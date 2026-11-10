// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the drop glue of a Type (SPEC 16.3, 16.4; R5 package K 4.5 "DropGlue", package C 3.6, S2-S4 X3, X23, X27; DECISIONS R5-M60, R5-M78,
// R5-M95 as PLAN G702 amends it): the row of a Type's glue function (Row), which FunctionCodegen writes through its call writer (PD3): a
// string's calls the runtime release (PD18's string row, C0c); a struct's, Tuple's, enum's, fixed array's or Closure environment's
// destroys its parts (C4a, C5a); an erased function's calls its drop entry (C5b), an owning handle's releases its object (C6b); a
// collection's, an Array's or a Dictionary's, calls its one destruction body (DECISIONS R8-M5, R8-K1c); the direct parts only the Backend
// knows (Parts); and the glue steps of what destroying a value destroys, Mir's steps (MirDrops.Destroyed.cs, the one statement of a
// level's order, SPEC 16.3.2) each mapped to its glue part: a drop group's walk (Destroyed) and a glue function's level (Level), the
// steps the instance collector targets a Drop's site and a DropGlue item by, and FunctionCodegen writes the Drop and the glue function
// from, so the two cannot disagree. No order of an aggregate's parts is stated here.
// Input: a closed Type of a compilation and its TyCtxt (Sealed); for a group, the drop table (A2's MirDropTable), the body and the
// group's row. Output: a Type's row, the string row's routine and a glue function's parameters; a group's or a level's steps in
// destruction order (Destroyed, Level), each aggregate part with its position in its aggregate (a struct's Fields, then its base; a
// Tuple's elements; a fixed array's elements; an enum's Case payload elements under the Case's ordinal; a Closure environment's
// captures, X23: Mir's order); the Backend's own parts (Parts): a declared destructor alone for a struct that declares one (its body
// destroys the Fields and the base, R4-M6b); an Array's or Dictionary's element Types, which its destruction destroys (through its
// glue's one target, destroyArray's or destroyDictionary's instance, DECISIONS R8-M5) and lays out (X34); an owning handle's payload
// when the payload is proven Sealed (else the object descriptor's finalizer runs the actual Type's glue, K8). Strings, scalars,
// references, raw pointers, Slices, Function values (their environment's destruction is erased) and InlineStorage, whose owner destroys
// its contents, have none.
// Invariants: deterministic; a level's parts are the components MirTypes.Destruction descends into, in destruction order, and a Sealed
// handle's payload, which that walk visits without descending (InstanceCollectorTest.DropGlueParts agree); a position is the component's
// index in the aggregate writer's numbering (FunctionCodegen.Component: a struct's base first, then its Fields; a Tuple's elements; a
// Case payload's elements under the Case's ordinal); the steps' order is Mir's, the order code generation reads a Drop's targets in
// (InstanceCollectorTest.AnOpenDropTargetsWhatItsGroupDestroysInWalkOrder).
// Must not: state the order of an aggregate's parts (Mir's level, MirDropTable.Level); decide whether a part needs a drop
// (MirTypes.MayNeedDrop, the consumer's), a row's verdict (A2's) or what a group's row destroys (MirDropTable.Destroyed: no row is read
// here); read syntax or proofs other than Sealed, name a glue function (ModuleCodegen names its items) or write text.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

/// <summary>What a direct part of a destroyed value is: Part from the mapping of Mir's steps (<see cref="DropGlue.Destroyed"/>,
/// <see cref="DropGlue.Level"/>), the others the Backend's own (<see cref="DropGlue.Parts"/>).</summary>
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
    Collection, // An Array's or a Dictionary's: its glue's one target, the library body its destruction runs, called with the glue's own
    // parameters (destroyArray; destroyDictionary, or releaseDictionary when no entry needs a drop; DECISIONS R8-M5).
}

/// <summary>A direct part of a destroyed value.</summary>
/// <param name="Kind">What the part is.</param>
/// <param name="Type">The part's Type, or the struct whose destructor runs.</param>
/// <param name="Ordinal">A Case payload element's Case ordinal; 0 for any other part.</param>
/// <param name="Index">The component's index in its aggregate (the aggregate writer's numbering: a struct's base 0 and its Fields after it,
/// a Tuple's or a Case payload's elements, a capture); -1 for a fixed array's element, which stands for every element, and for a part
/// without a position (a destructor, a collection's element Type, a Sealed payload).</param>
internal readonly record struct DropPart(DropPartKind Kind, BoundType Type, int Ordinal = 0, int Index = -1);

/// <summary>A glue step of a drop group's walk or of a glue function's level (<see cref="DropGlue.Destroyed"/>,
/// <see cref="DropGlue.Level"/>).</summary>
/// <param name="Kind">What the step is.</param>
/// <param name="Part">The part (the destroyed value's own Type at the top), its position in its aggregate the part's.</param>
/// <param name="Parent">The row whose own bit a part without a row, or a Run, follows; -1 for none (set on every path).</param>
/// <param name="Row">The part's governing row, or -1.</param>
/// <param name="First">A fixed array element's position, or a Run's first; -1 for any other part.</param>
/// <param name="Last">A Run's last position.</param>
internal readonly record struct DropStep(MirDropStepKind Kind, DropPart Part, int Parent, int Row, long First, long Last = -1);

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
            BoundTypeKind.Array or BoundTypeKind.Dictionary => DropGlueRow.Collection,
            _ => null,
        };
    }

    /// <summary>Appends the direct parts of a destroyed value only the Backend knows, which no move path names (SPEC 16.3.2, 16.4): a
    /// declared destructor alone, a collection's element Types, a Sealed owning handle's payload. An aggregate's parts are Mir's
    /// (<see cref="Level"/>).</summary>
    /// <param name="types">The Type context, whose Sealed proofs decide an owning handle's payload.</param>
    /// <param name="type">The closed Type.</param>
    /// <param name="into">The list the parts are appended to.</param>
    internal static void Parts(TyCtxt types, BoundType type, List<DropPart> into)
    {
        if (AdtDef.Destructor(type) is not null)
        {
            into.Add(new(DropPartKind.Destructor, type));
        }
        else if (type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            for (var i = type.Components.Count - 1; i >= 0; i--)
            {
                into.Add(new(DropPartKind.Element, type.Components[i]));
            }
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc } &&
            types.Sealed(null, type.Components[0]))
        {
            into.Add(new(DropPartKind.Payload, type.Components[0]));
        }
    }

    /// <summary>Appends a glue function's level of a closed Type, which no row governs (SPEC 16.3.2): for an aggregate whose parts move
    /// paths name (a struct without a declared destructor, a Tuple, an enum, a fixed array, a Closure environment), Mir's level of the
    /// Type (<see cref="MirDropTable.Level"/>, the level a drop group's walk composes with its rows), each step mapped to its glue part
    /// as a group's are (<see cref="Destroyed"/>); else an Open step, the Backend's own parts (<see cref="Parts"/>) Whole and the End
    /// step.</summary>
    /// <param name="types">The Type context, whose Sealed proofs decide an owning handle's payload.</param>
    /// <param name="type">The glue item's closed Type.</param>
    /// <param name="parts">The scratch list of <see cref="Parts"/>, left as it was.</param>
    /// <param name="walk">The scratch list of Mir's steps, left as it was.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal static void Level(TyCtxt types, BoundType type, List<DropPart> parts, List<MirDropStep> walk, List<DropStep> into)
    {
        if (Row(type) == DropGlueRow.Parts && AdtDef.Destructor(type) is null && !AdtDef.IsInlineStorage(type))
        {
            var first = walk.Count;
            MirDropTable.Level(type, walk);
            _ = Glue(walk, first, null, 0, into);
            walk.RemoveRange(first, walk.Count - first);
            return;
        }

        var start = parts.Count;
        DropPart value = new(DropPartKind.Part, type);
        into.Add(new(MirDropStepKind.Open, value, -1, -1, -1));
        Parts(types, type, parts);
        for (var i = start; i < parts.Count; i++)
        {
            into.Add(new(MirDropStepKind.Whole, parts[i], -1, -1, -1));
        }

        parts.RemoveRange(start, parts.Count - start);
        into.Add(new(MirDropStepKind.End, value, -1, -1, -1));
    }

    /// <summary>Appends what destroying a closed Type's value by a row of a drop group destroys, in destruction order (SPEC 16.3.2,
    /// 15.1.3; R4-M6a): Mir's walk of the group (<see cref="MirDropTable.Destroyed"/>, the one statement of what a group destroys and
    /// by which row), each step mapped to the glue's part: its position in its aggregate (the aggregate writer's numbering: a struct's
    /// base 0 and its Fields after it), a fixed array's element by its position, and an enum's Case payload, which has no glue of its
    /// own, as its parts under the payload's rows: an Open payload's steps, a payload destroyed whole the parts of Mir's level of the
    /// payload Tuple (<see cref="MirDropTable.Level"/>).</summary>
    /// <param name="drops">The drop table.</param>
    /// <param name="body">The ordinal of the body whose row it is.</param>
    /// <param name="row">The row.</param>
    /// <param name="type">The row's closed Type.</param>
    /// <param name="walk">The scratch list of Mir's steps, left as it was.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal static void Destroyed(MirDropTable drops, int body, int row, BoundType type, List<MirDropStep> walk, List<DropStep> into)
    {
        var start = walk.Count;
        drops.Destroyed(body, row, type, walk);
        for (var i = start; i < walk.Count;)
        {
            i = Glue(walk, i, null, 0, into);
        }

        walk.RemoveRange(start, walk.Count - start);
    }

    // Maps Mir's step at an index, a part of its level's Type (null at the top) under a Case ordinal (a payload's element's, else 0), and
    // an Open step's level with it; the index after it.
    private static int Glue(List<MirDropStep> walk, int index, BoundType? owner, int ordinal, List<DropStep> into)
    {
        var step = walk[index];
        var (type, payload) = (step.Type, owner is not null && step.Step == MirProjectionKind.Downcast);
        if (payload && step.Kind == MirDropStepKind.Whole)
        {
            // A payload destroyed whole has no glue of its own: the parts of Mir's level of the payload Tuple, which no row governs,
            // each mapped under the Case's ordinal, then governed as the payload is (its row, or its parent's own bit).
            var (level, mapped) = (walk.Count, into.Count);
            MirDropTable.Level(type, walk);
            for (var i = level + 1; walk[i].Kind != MirDropStepKind.End;)
            {
                i = Glue(walk, i, type, (int)step.Arg, into);
            }

            walk.RemoveRange(level, walk.Count - level);
            for (; mapped < into.Count; mapped++)
            {
                into[mapped] = into[mapped] with { Parent = step.Parent, Row = step.Row };
            }

            return index + 1;
        }

        // The aggregate writer's numbering: a struct's base first, then its Fields; a fixed array's element at its position.
        var array = owner?.Kind == BoundTypeKind.FixedArray;
        var based = owner is not null && AdtDef.Base(owner) is not null ? 1 : 0;
        var position = step.Step == MirProjectionKind.Base ? 0 : (int)step.Arg + based;
        if (owner is null || array)
        {
            position = -1;
        }

        DropPart part = new(DropPartKind.Part, type, ordinal, position);
        var (first, last) = (array ? step.Arg : -1, step.Last);
        if (!payload)
        {
            into.Add(new(step.Kind, part, step.Parent, step.Row, first, last));
        }

        if (step.Kind != MirDropStepKind.Open)
        {
            return index + 1;
        }

        for (index++; walk[index].Kind != MirDropStepKind.End;)
        {
            index = Glue(walk, index, type, payload ? (int)step.Arg : 0, into);
        }

        if (!payload)
        {
            into.Add(new(MirDropStepKind.End, part, step.Parent, step.Row, first));
        }

        return index + 1;
    }
}
