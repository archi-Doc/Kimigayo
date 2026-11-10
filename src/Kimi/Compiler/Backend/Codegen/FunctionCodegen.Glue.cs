// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The destruction of a value one level per Type in FunctionCodegen (R5 package C 3.6, plans C4a, C4c, C5a and C5b; DECISIONS R5-M78,
// R5-M95, R5-M100, R5-M102, R5-M104): a DropGlue item's function, void (ptr value, ptr location, i64 length), which ModuleCodegen names and
// signs (Glue), is one level of its Type (Level) at its value parameter, reporting failures at its own location parameters, reading its
// item's targets; a Drop destroys its value by its group (FunctionCodegen.Drops), at its address, reporting the Drop's location, reading
// the Drop's targets. Both write the one walk the collector targets by (Destroys: DropGlue.Destroyed, PLAN G641; a glue function's
// level is its walk without a row), step by step (Step): a string's level calls the runtime release (PD18's string row, C0c). A
// struct's, Tuple's, enum's, fixed array's or Closure environment's level destroys its parts in destruction order (a Closure's captures
// last to first, SPEC 16.4, each addressed as a Tuple's element): a struct that declares a drop by its destructor instance alone (PD18,
// R4-M6b), the value its receiver (never under an Open row: A2's MovedPlace_Kd); otherwise each part whose instance Type may need a drop
// (MirTypes.MayNeedDrop, the rule the collector targets by) by its Keep or Flag row (an Elide row, destroying nothing, has no step), or its
// parent's own bit without one, through its glue, the destruction's next target, at the part's address (Component, the aggregate writer's
// rule; a base at the value's own address, X1), an Open part by its own level; an enum's parts under one switch on its tag over the Cases
// holding such a part (an Open enum's tag is defined: Case payload paths exist only in an owned match Subject, initialized before arm
// selection, SPEC 15.1.6); a fixed array's elements from the last, each run of elements without a row in one counted loop (Loop, Fill's
// loop), so the text does not grow with the length.
// A common Function value's calls its table's drop entry, when present, on a copy of its word in an entry slot (FunctionCodegen.Erasures),
// and an owning handle's releases its object (FunctionCodegen.Objects' Release), each only in its glue function, since no move path opens
// either. A collection's row (also only in its glue function), an Array's or a Dictionary's, calls its one target, the library body its
// destruction runs, with the glue's parameters (CollectionGlue; DECISIONS R8-M5); a Type of a kind no glue has, or a target missing
// or left over, is a broken contract, InternalInvariant_Kd there.
internal sealed partial class FunctionCodegen
{
    private static readonly Site Received = new(new('p', 1), new('p', 2)); // A glue function's location, its own parameters location and length.
    private readonly List<DropPart> parts = []; // DropGlue.Destroyed's scratch of DropGlue.Parts.
    private readonly List<DropStep> steps = []; // DropGlue.Destroyed's steps.
    private int read; // The next of the item's targets the destruction being written reads: a glue function's, or a Drop's at its site.
    private int end; // The end of them.
    private SourceInfo destruction; // Where it reports a missing target: a Drop's source; none for a glue function.

    /// <summary>Writes the function of a DropGlue item, one level of its Type (<see cref="DropGlue.Row"/>), or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The DropGlue item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteGlue(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next) = (collector, item, output, 0);
        (this.read, this.end, this.destruction) = (0, collector.Set.Targets(item).Length, default);
        this.abi = this.module.Glue(item);
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        this.Level(collector.Set.Items[item].RepresentativeType!, new('p', 0), Received);
        output.Write("  ret void\n}\n");
    }

    // A glue function's level of its item's closed Type at its address by the Type's row, its releases reporting its own location.
    private void Level(BoundType type, Value address, Site site)
    {
        switch (DropGlue.Row(type))
        {
            case DropGlueRow.Release:
                this.Destroy(DropGlue.Release, address, site);
                return;
            case DropGlueRow.Parts:
                this.Destroys(-1, type, address, site);
                if (this.read != this.end)
                {
                    this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets exactly the parts that may need a drop.
                }

                return;
            case DropGlueRow.Function:
                this.DestroyErased(type, address, site);
                return;
            case DropGlueRow.Handle:
                this.Release(this.item, type, address, site);
                return;
            case DropGlueRow.Collection:
                this.CollectionGlue(this.item, address, site);
                return;
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A glue item's Type is of a kind MirTypes.MayNeedDrop admits.
                return;
        }
    }

    // SPEC 4.9.7, 16.3.2: a collection's glue level at its address, its one target, the library body its destruction runs (destroyArray,
    // destroyDictionary or releaseDictionary), with the glue's own parameters.
    private void CollectionGlue(int glue, Value address, Site site)
    {
        if (this.collector.Set.Targets(glue) is not [var target])
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets the body a collection's destruction runs.
        }
        else if (this.module.Signature(target.Item) is { } destroy)
        {
            this.Destroy(destroy, address, site);
        }
    }

    // What destroying a value of a closed Type at an address by a row of the body's drop table destroys (-1: a glue function's level, which
    // no row governs), by the walk the collector targets by (DropGlue.Destroyed), each step in order (Step).
    private void Destroys(int row, BoundType type, Value address, Site site)
    {
        var (steps, start) = (this.steps, this.steps.Count);
        DropGlue.Destroyed(this.typeContext, row < 0 ? default : this.program.Drops.RowsOf(this.body.Ordinal), row, type, this.parts, steps);
        _ = this.Step(start, null, null, address, site);
        steps.RemoveRange(start, steps.Count - start);
    }

    // SPEC 16.3.2: a step of the walk in its level (owner: none at the top) at the level's address; the index after it (after its End for
    // an Open step). A destructor alone on the value (its receiver); a part that may need a drop (MirTypes.MayNeedDrop, the rule the
    // collector targets by) at its address (Component, the aggregate writer's rule; a base at the value's own address, X1): Whole by its
    // Keep or Flag row's flag, or its parent's own bit without a row (its glue, the destruction's next target, under the flag), Open by its
    // level (Opened); a Run of fixed array elements under its parent's own bit in the one counted loop
    // (Loop, Fill's loop), whose k-th pass destroys the element at the run's last element's offset (ElementOffset) less the pass's, so the
    // text does not grow with the length.
    private int Step(int index, BoundType? owner, ValueLowering? record, Value address, Site site)
    {
        var step = this.steps[index];
        var type = step.Part.Type;
        if (step.Part.Kind == DropPartKind.Destructor)
        {
            if (this.NextTarget() is { } target && this.module.Signature(target) is { } destructor)
            {
                this.Destroy(destructor, address, site); // Its receiver, the one parameter other than a location; none for a zero-sized Self.
            }

            return index + 1;
        }

        if (!MirTypes.MayNeedDrop(type, this.seen))
        {
            return this.After(index);
        }

        if (step.Kind == DropStepKind.Run)
        {
            if (this.NextTarget() is { } glue && step.First <= step.Last && this.Laid(type) is { } element)
            {
                var guard = this.Test(this.Own(step.Parent));
                var last = new Value('\0', ElementOffset(element, step.Last)); // The run's last element, which the first pass destroys.
                this.Loop(new Value('\0', step.Last - step.First + 1), element, new Destroyed(glue, address, last, site));
                this.Join(guard);
            }

            return index + 1;
        }

        if (this.OffsetOf(owner, record, step) is not { } offset)
        {
            return this.After(index); // A broken contract, recorded.
        }

        if (step.Kind == DropStepKind.Open)
        {
            return this.Opened(index, this.Offset(address, offset), site);
        }

        if (this.NextTarget() is { } part)
        {
            var guard = this.Test(step.Row >= 0 ? this.DropRow(step.Row).Flag : this.Own(step.Parent));
            this.Destroy(this.module.Glue(part), this.Offset(address, offset), site);
            this.Join(guard);
        }

        return index + 1;
    }

    // An Open step's level at its address, its Type's parts (a Function value, a handle or a collection has none a move path opens) up to
    // its End: an enum's under one switch on its tag (Switch), each Case holding a part that may need a drop a block (an Open enum's tag
    // is defined: Case payload paths exist only in an owned match Subject, initialized before arm selection, SPEC 15.1.6). The index after
    // its End.
    private int Opened(int index, Value address, Site site)
    {
        var type = this.steps[index].Part.Type;
        if (DropGlue.Row(type) != DropGlueRow.Parts)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return this.After(index);
        }

        if (this.Laid(type) is not { } record)
        {
            return this.After(index); // A broken contract, recorded.
        }

        var (id, open, i) = (AdtDef.IsEnum(type) ? this.Switch(index, address) : -1, -1, index + 1);
        while (this.steps[i].Kind != DropStepKind.End)
        {
            var part = this.steps[i].Part;
            if (id >= 0 && part.Ordinal != open && MirTypes.MayNeedDrop(part.Type, this.seen))
            {
                this.Leave(open, id);
                open = part.Ordinal;
                this.CaseLabel("case", id, open);
                this.output.Write(":\n");
            }

            i = this.Step(i, type, record, address, site);
        }

        if (id >= 0)
        {
            this.Leave(open, id);
            Name(this.output, "destroyed", id);
            this.output.Write(":\n");
        }

        return i + 1;
    }

    // SPEC 16.3.2: an enum level's switch on its tag (Layout.Tag, at the record's offset 0) over the Cases its steps destroy a part of
    // that may need a drop; any other tag destroys nothing. Its id.
    private int Switch(int index, Value address)
    {
        var output = this.output;
        var id = this.next++;
        var tag = this.LoadAt(address, Layout.Tag.Layout);
        output.Write("  switch i32 ");
        this.WriteValue(tag);
        Name(output, ", label %destroyed", id);
        output.Write(" [");
        for (int i = index + 1, last = -1; this.steps[i].Kind != DropStepKind.End; i = this.After(i))
        {
            if (this.steps[i].Part is var part && part.Ordinal != last && MirTypes.MayNeedDrop(part.Type, this.seen))
            {
                last = part.Ordinal;
                output.Write(" i32 ");
                WriteNumber(output, last);
                this.CaseLabel(", label %case", id, last);
            }
        }

        output.Write(" ]\n");
        return id;
    }

    // The index after a step, past its level for an Open one.
    private int After(int index)
    {
        for (var depth = 0; ; index++)
        {
            depth += this.steps[index].Kind switch { DropStepKind.Open => 1, DropStepKind.End => -1, _ => 0 };
            if (depth <= 0)
            {
                return index + 1;
            }
        }
    }

    // A step's part's offset in its level's value (none at the top: 0), by the aggregate writer's rule (Component), a fixed array element's
    // by its position; null for a broken contract, recorded.
    private long? OffsetOf(BoundType? owner, ValueLowering? record, in DropStep step)
    {
        if (owner is null)
        {
            return 0;
        }

        var kind = owner.Kind switch
        {
            BoundTypeKind.Tuple => MirAggregateKind.Tuple,
            BoundTypeKind.Closure => MirAggregateKind.Closure,
            BoundTypeKind.FixedArray => MirAggregateKind.FixedArray,
            _ => MirAggregateKind.Adt,
        };
        return this.Component(kind, owner, record!, step.Part.Ordinal, kind == MirAggregateKind.FixedArray ? (int)step.First : step.Part.Index)?.Offset;
    }

    // The end of an open Case's block, a branch to the switch's join.
    private void Leave(int open, int id)
    {
        if (open >= 0)
        {
            Name(this.output, "  br label %destroyed", id);
            this.output.Write('\n');
        }
    }

    // A Case's block label, case<id>.<ordinal>.
    private void CaseLabel(string prefix, int id, int ordinal)
    {
        Name(this.output, prefix, id);
        this.output.Write('.');
        WriteNumber(this.output, ordinal);
    }

    // An i64 difference, last - offset.
    private Value Difference(Value last, Value offset)
    {
        var difference = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(difference);
        this.output.Write(" = sub i64 ");
        this.WriteValue(last);
        this.output.Write(", ");
        this.WriteValue(offset);
        this.output.Write('\n');
        return difference;
    }

    // The one destruction call (a glue's part or destructor, a Drop's glue, a common Function value's drop entry through its pointer): a
    // routine whose one parameter other than the location is the given value, its location parameters a site's (a glue function passes its
    // own on, Received; a Drop its location).
    private void Destroy(FunctionAbi routine, Value value, Site site, Value? entry = null)
    {
        var parameters = routine.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        for (var i = 0; i < parameters.Length; i++)
        {
            this.arguments[i] = parameters[i].Kind switch
            {
                AbiParameterKind.Location => site.Text,
                AbiParameterKind.LocationLength => site.Length,
                _ => value,
            };
        }

        _ = this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length), entry);
    }

    // The destruction's next target (I8: a glue function's in DropGlue.Parts' order, a Drop's in DropGlue.Destroyed's), or null with
    // InternalInvariant_Kd at the destruction where none is left.
    private int? NextTarget()
    {
        if (this.read < this.end)
        {
            return this.collector.Set.Targets(this.item)[this.read++].Item;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, this.destruction);
        return null;
    }

    // A level's pass over a fixed array at an address: the element's glue at the element the pass destroys, from the last given.
    private readonly struct Destroyed(int glue, Value address, Value last, Site site) : IElementPass
    {
        public string Stem => "destroy";

        public void Write(FunctionCodegen codegen, Value offset)
            => codegen.Destroy(codegen.module.Glue(glue), codegen.Offset(address, codegen.Difference(last, offset)), site);
    }
}
