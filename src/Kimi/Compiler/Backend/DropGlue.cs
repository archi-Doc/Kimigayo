// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the drop glue of a Type (SPEC 16.3, 16.4; R5 package K 4.5 "DropGlue", package C 3.6, S2-S4 X3, X23, X27; DECISIONS R5-M60, R5-M78,
// R5-M95): what destroying a value of a closed Type destroys directly, in destruction order (Parts); the row of a Type's glue function
// (Row), which FunctionCodegen writes through its call writer (PD3): a string's calls the runtime release (PD18's string row, C0c); a
// struct's, Tuple's, enum's, fixed array's or Closure environment's destroys its parts (C4a, C5a); an erased function's calls its drop
// entry (C5b), an owning handle's releases its object (C6b); a collection's, an Array's or a Dictionary's, calls its one destruction body
// (DECISIONS R8-M5, R8-K1c); and the one walk of what destroying a value by a drop group's row, or a glue function's level, destroys
// (Destroyed, PLAN G641, run by its Walker), by the row governing each part: the steps the instance collector targets a Drop's site
// and a DropGlue item by, and FunctionCodegen writes the Drop and the glue function from, so the two cannot disagree.
// Input: a closed Type of a compilation and its TyCtxt (Sealed); for a walk, a body's drop table rows (A2's MirDropTable) and a group's
// row. Output: a Type's row, the string row's routine and a glue function's parameters; the walk's steps in destruction order
// (Destroyed); the Type's direct parts in destruction order, each with its position in its aggregate: a declared
// destructor alone for a struct that declares one (its body destroys the Fields and the base, R4-M6b); else a struct's Fields in
// reverse logical declaration order, then its base; a Tuple's elements in decreasing index; a fixed array's element (each element,
// from the last); an enum's Case payload elements, each Case's in reverse declaration order; a Closure environment's captures in
// reverse initialization order (X23); an Array's or Dictionary's element Types, which its destruction destroys (through its glue's one
// target, destroyArray's or destroyDictionary's instance, DECISIONS R8-M5) and lays out (X34); an owning handle's payload when the
// payload is proven Sealed (else the object descriptor's finalizer runs the actual Type's glue, K8). Strings, scalars, references, raw
// pointers, Slices, Function values (their environment's destruction is erased) and InlineStorage, whose owner destroys its contents,
// have none.
// Invariants: deterministic; the parts are the components MirTypes.Destruction descends into, in destruction order, and a Sealed handle's
// payload, which that walk visits without descending (InstanceCollectorTest.DropGlueParts agree); a position is the component's index in
// the aggregate writer's numbering (FunctionCodegen.Component: a struct's base first, then its Fields; a Tuple's elements; a Case
// payload's elements under the Case's ordinal); a walk's order is the order code generation reads a Drop's targets in
// (InstanceCollectorTest.AnOpenDropTargetsWhatItsGroupDestroysInWalkOrder).
// Must not: decide whether a part needs a drop (MirTypes.MayNeedDrop, the consumer's) or a row's verdict (A2's), read syntax or proofs
// other than Sealed, name a glue function (ModuleCodegen names its items) or write text.

using Kimi.Compiler.Mir;
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

/// <summary>What a step of a drop group's walk is (<see cref="DropGlue.Destroyed"/>).</summary>
internal enum DropStepKind : byte
{
    Whole, // A part destroyed whole through its glue (a destructor alone), by its row's verdict, or by its parent's own bit without a row.
    Run, // A fixed array's elements without a row, from Last down to First, through the element's glue under the parent's own bit.
    Open, // A level destroyed part by part: its steps follow, up to its End.
    End, // The end of a level.
}

/// <summary>A step of a drop group's walk (<see cref="DropGlue.Destroyed"/>).</summary>
/// <param name="Kind">What the step is.</param>
/// <param name="Part">The part (the destroyed value's own Type at the top), its position in its aggregate the part's.</param>
/// <param name="Parent">The row whose own bit a part without a row, or a Run, follows; -1 for none (set on every path).</param>
/// <param name="Row">The part's governing row, or -1.</param>
/// <param name="First">A fixed array element's position, or a Run's first; -1 for any other part.</param>
/// <param name="Last">A Run's last position.</param>
internal readonly record struct DropStep(DropStepKind Kind, DropPart Part, int Parent, int Row, long First, long Last = -1);

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

    /// <summary>Appends the walk of what destroying a closed Type's value by a row of a drop group destroys, in destruction order (SPEC
    /// 16.3.2, 15.1.3; R4-M6a), each part a step: a Keep, a Flag or an Elide row the value whole (an Elide row as the whole glue's admission
    /// reaches it, until R3-U2b); an Open row (never a struct's that declares a drop: A2's MovedPlace_Kd), or no row (-1: a glue function's
    /// level, which no row governs), an Open step, then each direct part (<see cref="Parts"/>) by its governing row, by that row's walk, so
    /// a part without a row whole under its parent's own bit, then the End step; a fixed array's elements from the last, each run of
    /// elements without a row one Run step (without a row, one run of every element, none left out for no element), each element with a
    /// row by its row's walk (<see cref="Walker"/>).</summary>
    /// <param name="types">The Type context, whose Sealed proofs decide an owning handle's payload.</param>
    /// <param name="rows">The body's drop table rows (none for no row).</param>
    /// <param name="row">The row, or -1.</param>
    /// <param name="type">The row's closed Type.</param>
    /// <param name="parts">The scratch list of <see cref="Parts"/>, left as it was.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal static void Destroyed(TyCtxt types, ReadOnlySpan<MirDropRow> rows, int row, BoundType type, List<DropPart> parts, List<DropStep> into)
    {
        var walk = new Walker(types, rows, parts, into);
        DropPart value = new(DropPartKind.Part, type);
        if (row < 0)
        {
            walk.Level(value, -1, row, -1); // A glue function's level.
        }
        else
        {
            walk.Step(value, -1, row, -1);
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

    /// <summary>The walk of one drop group (<see cref="Destroyed"/>): the Type context, the body's drop table rows, the scratch list of
    /// <see cref="Parts"/> and the steps it appends, which every step of the walk shares; each step takes only its part, its parent row, its
    /// governing row and its element position.</summary>
    private readonly ref struct Walker
    {
        private readonly TyCtxt types;
        private readonly ReadOnlySpan<MirDropRow> rows;
        private readonly List<DropPart> parts;
        private readonly List<DropStep> into;

        /// <summary>Initializes a new instance of the <see cref="Walker"/> struct.</summary>
        /// <param name="types">The Type context, whose Sealed proofs decide an owning handle's payload.</param>
        /// <param name="rows">The body's drop table rows (none for no row).</param>
        /// <param name="parts">The scratch list of <see cref="Parts"/>, left as it was.</param>
        /// <param name="into">The list the steps are appended to.</param>
        internal Walker(TyCtxt types, ReadOnlySpan<MirDropRow> rows, List<DropPart> parts, List<DropStep> into)
        {
            this.types = types;
            this.rows = rows;
            this.parts = parts;
            this.into = into;
        }

        /// <summary>Appends a part's walk by its governing row under its parent: an Open row's level, else the part whole.</summary>
        /// <param name="part">The part.</param>
        /// <param name="parent">The row whose own bit the part follows without a row; -1 for none.</param>
        /// <param name="row">The part's governing row, or -1.</param>
        /// <param name="position">The part's element position, or -1.</param>
        internal void Step(in DropPart part, int parent, int row, long position)
        {
            if (row >= 0 && this.rows[row].Verdict == MirDropVerdict.Open)
            {
                this.Level(part, parent, row, position);
            }
            else
            {
                this.into.Add(new(DropStepKind.Whole, part, parent, row, position));
            }
        }

        /// <summary>Appends a level's steps under its row (-1: a glue function's), from its Open step to its End.</summary>
        /// <param name="part">The part whose level it is.</param>
        /// <param name="parent">The row whose own bit the part follows without a row; -1 for none.</param>
        /// <param name="row">The level's row, or -1.</param>
        /// <param name="position">The part's element position, or -1.</param>
        internal void Level(in DropPart part, int parent, int row, long position)
        {
            var (type, parts, start) = (part.Type, this.parts, this.parts.Count);
            this.into.Add(new(DropStepKind.Open, part, parent, row, position));
            Parts(this.types, type, parts);
            var end = parts.Count; // The level's parts, read in order, then removed: the walks of nested levels follow them.
            for (var i = start; i < end; i++)
            {
                if (type.Kind != BoundTypeKind.FixedArray)
                {
                    var (governor, child) = this.Governing(type, parts[i], row);
                    this.Step(parts[i], governor, child, -1);
                    continue;
                }

                if (row < 0)
                {
                    this.into.Add(new(DropStepKind.Run, parts[i], row, -1, 0, type.Length - 1)); // Every element (none for no element).
                    continue;
                }

                for (long last = type.Length - 1, first; last >= 0; last = first - 1)
                {
                    var child = this.Highest(row, last);
                    first = child < 0 ? 0 : this.rows[child].Arg + 1;
                    if (first <= last)
                    {
                        this.into.Add(new(DropStepKind.Run, parts[i], row, -1, first, last));
                    }

                    if (child >= 0)
                    {
                        this.Step(parts[i], row, child, --first);
                    }
                }
            }

            parts.RemoveRange(start, end - start);
            this.into.Add(new(DropStepKind.End, part, parent, row, position));
        }

        // The rows governing a direct part under an Open row (SPEC 16.3.2; R4-M6a): its child row, named by the part's move-path step
        // (MirMoveData; the bridge from the aggregate writer's numbering, which DropPart carries: a struct's own Field by its index, its
        // position less one after a base, the base by one Base step, a Tuple's element by its index); an enum Case payload element through
        // its Case's row, by that row's child at the payload's Field when the Case's row is Open, else by the Case's row. The parent row
        // whose own bit a part without a row follows, and the child row, or -1 for none (-1 under no row, a glue function's level).
        private (int Parent, int Child) Governing(BoundType type, in DropPart part, int row)
        {
            if (AdtDef.IsEnum(type))
            {
                var payload = this.Child(row, MirProjectionKind.Downcast, part.Ordinal);
                return payload >= 0 && this.rows[payload].Verdict == MirDropVerdict.Open
                    ? (payload, this.Child(payload, MirProjectionKind.Field, part.Index)) : (row, payload);
            }

            var based = AdtDef.Base(type) is not null; // The aggregate writer's numbering: a struct's base first, then its Fields.
            if (based && part.Index == 0)
            {
                return (row, this.Child(row, MirProjectionKind.Base, 1));
            }

            return (row, this.Child(row, MirProjectionKind.Field, based ? part.Index - 1 : part.Index));
        }

        // The child row of an Open fixed array's row at the highest element position not above last (each a ConstIndex step), or -1.
        private int Highest(int row, long last)
        {
            var found = -1;
            for (var child = row + 1; child < this.rows.Length && this.rows[child].Depth > this.rows[row].Depth; child++)
            {
                var candidate = this.rows[child];
                if (candidate.Depth == this.rows[row].Depth + 1 && candidate.Arg <= last && (found < 0 || candidate.Arg > this.rows[found].Arg))
                {
                    found = child;
                }
            }

            return found;
        }

        // The child row of a row by its step, among the rows of its depth below it in the group's pre-order; -1 for none or no row.
        private int Child(int row, MirProjectionKind step, int arg)
        {
            if (row < 0)
            {
                return -1;
            }

            for (var child = row + 1; child < this.rows.Length && this.rows[child].Depth > this.rows[row].Depth; child++)
            {
                if (this.rows[child].Depth == this.rows[row].Depth + 1 && this.rows[child].Step == step && this.rows[child].Arg == arg)
                {
                    return child;
                }
            }

            return -1;
        }
    }
}
