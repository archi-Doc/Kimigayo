// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The destruction of a value one level per Type in FunctionCodegen (R5 package C 3.6, plans C4a, C4c, C5a and C5b; DECISIONS R5-M78,
// R5-M95, R5-M100, R5-M102, R5-M104): a DropGlue item's function, void (ptr value, ptr location, i64 length), which ModuleCodegen names and
// signs (Glue), is one level of its Type (Level) at its value parameter, reporting failures at its own location parameters, reading its
// item's targets; an Open row of a Drop is one level of the dropped value's or part's Type under that row (FunctionCodegen.Drops), at its
// address, reporting the Drop's location, reading the Drop's targets, the group's walk (DropGlue.Destroyed; PLAN G641).
// A level is written by its Type's row (DropGlue.Row): a string's calls the runtime release (PD18's string row, C0c). A struct's,
// Tuple's, enum's, fixed array's or Closure environment's destroys its parts in destruction order (DropGlue.Parts, the enumeration the
// collector targets from, X27; a Closure's captures last to first, SPEC 16.4, each addressed as a Tuple's element), each part by its row
// (FunctionCodegen.Drops' DestroyByRow): under an Open row its child row's verdict, or its parent's own bit without one; under no
// row (-1, the representation of a glue function's level, as a flag's -1) every part, unconditionally. A struct that declares a drop calls
// its destructor instance alone (PD18, R4-M6b), the value its receiver (never under an Open row: A2's MovedPlace_Kd); otherwise each part
// whose instance Type may need a drop (MirTypes.MayNeedDrop, the rule the collector targets by) takes the level's next target, the part's
// glue (none for a part under an Open row, whose own level reads its parts'), at the part's address (Component, the aggregate writer's
// rule; a base at the value's own address, X1); an enum's parts under one switch on its tag over the Cases holding such a part (an Open
// enum's tag is defined: Case payload paths exist only in an owned match Subject, initialized before arm selection, SPEC 15.1.6); a fixed
// array's elements from the last, through the element's glue read once, each run of elements without a row in one counted loop (Loop,
// Fill's loop), so the text does not grow with the length.
// A common Function value's calls its table's drop entry, when present, on a copy of its word in an entry slot (FunctionCodegen.Erasures),
// and an owning handle's releases its object (FunctionCodegen.Objects' Release), each only in its glue function, since no move path opens
// either. A collection's row (also only in its glue function), an Array's or a Dictionary's, calls its one target, the library body its
// destruction runs, with the glue's parameters (CollectionGlue; DECISIONS R8-M5); a Type of a kind no glue has, or a target missing
// or left over, is a broken contract, InternalInvariant_Kd there.
internal sealed partial class FunctionCodegen
{
    private static readonly Site Received = new(new('p', 1), new('p', 2)); // A glue function's location, its own parameters location and length.
    private readonly List<DropPart> parts = []; // DropGlue.Parts' scratch.
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
        this.Level(collector.Set.Items[item].RepresentativeType!, -1, new('p', 0), Received);
        output.Write("  ret void\n}\n");
    }

    // One level of a value of a closed Type at its address by the Type's row, under an Open row (-1: none, the glue function's own level of
    // its item, the only one a Function value, a handle or a collection has), its releases reporting the site.
    private void Level(BoundType type, int row, Value address, Site site)
    {
        switch (DropGlue.Row(type))
        {
            case DropGlueRow.Release:
                this.Destroy(DropGlue.Release, address, site);
                return;
            case DropGlueRow.Parts:
                this.DestroyParts(type, row, address, site);
                return;
            case DropGlueRow.Function when row < 0:
                this.DestroyErased(type, address, site);
                return;
            case DropGlueRow.Handle when row < 0:
                this.Release(this.item, type, address, site);
                return;
            case DropGlueRow.Function or DropGlueRow.Handle:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A Function value or a handle has no part a move path opens.
                return;
            case DropGlueRow.Collection when row < 0:
                this.CollectionGlue(this.item, address, site);
                return;
            case DropGlueRow.Collection:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A collection has no part a move path opens.
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

    // A struct's, Tuple's, enum's or fixed array's parts in destruction order under a row, each of the level's targets read once; a glue
    // function's level reads all of its item's (a Drop's are checked at its end, FunctionCodegen.Drops' DestroyPlace).
    private void DestroyParts(BoundType type, int row, Value address, Site site)
    {
        var (parts, start) = (this.parts, this.parts.Count);
        DropGlue.Parts(this.typeContext, type, parts);
        if (this.Laid(type) is { } record)
        {
            if (AdtDef.IsEnum(type))
            {
                this.Cases(type, record, start, row, address, site);
            }
            else
            {
                for (var i = start; i < parts.Count; i++)
                {
                    this.Part(type, record, parts[i], row, address, site);
                }
            }

            if (row < 0 && this.read != this.end)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets exactly the parts that may need a drop.
            }
        }

        parts.RemoveRange(start, parts.Count - start);
    }

    // SPEC 16.3.2: an enum's active Case payload, under one switch on its tag (Layout.Tag, at the record's offset 0) over the Cases holding
    // a part that may need a drop; any other tag destroys nothing.
    private void Cases(BoundType type, ValueLowering record, int start, int row, Value address, Site site)
    {
        var parts = this.parts;
        var output = this.output;
        var id = this.next++;
        var tag = this.LoadAt(address, Layout.Tag.Layout);
        output.Write("  switch i32 ");
        this.WriteValue(tag);
        Name(output, ", label %destroyed", id);
        output.Write(" [");
        for (int i = start, last = -1; i < parts.Count; i++)
        {
            if (parts[i].Ordinal != last && MirTypes.MayNeedDrop(parts[i].Type, this.seen))
            {
                last = parts[i].Ordinal;
                output.Write(" i32 ");
                WriteNumber(output, last);
                this.CaseLabel(", label %case", id, last);
            }
        }

        output.Write(" ]\n");
        var open = -1;
        for (var i = start; i < parts.Count; i++)
        {
            if (parts[i].Ordinal != open && MirTypes.MayNeedDrop(parts[i].Type, this.seen))
            {
                this.Leave(open, id);
                open = parts[i].Ordinal;
                this.CaseLabel("case", id, open);
                output.Write(":\n");
            }

            this.Part(type, record, parts[i], row, address, site);
        }

        this.Leave(open, id);
        Name(output, "destroyed", id);
        output.Write(":\n");
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

    // A direct part under a row: the destructor instance, or the part's glue at its address or each element's by its governing row
    // (DropGlue.Governing, DestroyByRow); nothing for a part needing no drop.
    private void Part(BoundType type, ValueLowering record, in DropPart part, int row, Value address, Site site)
    {
        if (part.Kind != DropPartKind.Destructor && !MirTypes.MayNeedDrop(part.Type, this.seen))
        {
            return;
        }

        if (part.Kind == DropPartKind.Destructor)
        {
            if (this.NextTarget() is { } target && this.module.Signature(target) is { } destructor)
            {
                this.Destroy(destructor, address, site); // Its receiver, the one parameter other than a location; none for a zero-sized Self.
            }
        }
        else if (type.Kind == BoundTypeKind.FixedArray)
        {
            this.Elements(type, this.NextTarget() ?? -1, row, address, site);
        }
        else
        {
            var kind = type.Kind switch
            {
                BoundTypeKind.Tuple => MirAggregateKind.Tuple,
                BoundTypeKind.Closure => MirAggregateKind.Closure,
                _ => MirAggregateKind.Adt,
            };
            if (this.Component(kind, type, record, part.Ordinal, part.Index) is not { } component)
            {
                return; // A broken contract, recorded.
            }

            var (parent, child) = DropGlue.Governing(this.DropRows(row), type, part, row);
            this.DestroyByRow(parent, child, part.Type, this.GlueOf(child), address, component.Offset, site);
        }
    }

    // SPEC 16.3.2: a fixed array's elements by decreasing index under a row, through the element's glue, the level's target read once: an
    // element with a row (its ConstIndex child) by it (DestroyByRow), each run of elements without one under the row's own bit in the one
    // counted loop (Loop), whose k-th pass destroys the run's element at its last element's offset (ElementOffset) less the pass's; under
    // no row, one run of every element. Nothing for no element.
    private void Elements(BoundType type, int target, int row, Value address, Site site)
    {
        if (target < 0 || type.Length == 0 || this.Laid(type.Components[0]) is not { } element)
        {
            return; // No element, or a missing target, recorded (NextTarget).
        }

        for (long last = type.Length - 1, end; last >= 0; last = end - 1)
        {
            var child = DropGlue.Highest(this.DropRows(row), row, last);
            end = child < 0 ? -1 : this.DropRow(child).Arg;
            if (end < last)
            {
                var guard = this.Test(this.Own(row));
                this.Loop(new Value('\0', last - end), element, new Destroyed(target, address, new('\0', ElementOffset(element, last)), site));
                this.Join(guard);
            }

            if (child >= 0)
            {
                this.DestroyByRow(row, child, type.Components[0], target, address, ElementOffset(element, end), site);
            }
        }
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
