// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The DropGlue items' functions of FunctionCodegen (R5 package C 3.6, plan C4a; DECISIONS R5-M78, R5-M95): one function per erased Type,
// void (ptr value, ptr location, i64 length), which ModuleCodegen names and signs (Glue), written by its Type's row (DropGlue.Row). A
// string's calls the runtime release with the function's own parameters (PD18's string row, C0c). A struct's, Tuple's, enum's or fixed
// array's destroys one level, its parts in destruction order (DropGlue.Parts, the enumeration the collector targets from, X27): a struct
// that declares a drop calls its destructor instance alone (PD18, R4-M6b), the value its receiver; otherwise each part whose instance Type
// may need a drop (MirTypes.MayNeedDrop, the rule the collector targets by) calls the item's next target, the part's glue, at the part's
// address (Component, the aggregate writer's rule; a base at the value's own address, X1), passing the function's location on; an enum's
// parts under one switch on its tag over the Cases holding such a part, a fixed array's element in one counted loop from the last
// element (Loop, Fill's loop). The other rows are their units' (C5a, C5b, C6b, C8d, C8e), one located Unsupported_Kd at the item's site; a
// Type of a kind no glue has, or a target missing or left over, is a broken contract, InternalInvariant_Kd there.
internal sealed partial class FunctionCodegen
{
    private readonly List<DropPart> parts = []; // DropGlue.Parts' scratch.
    private int glued; // The glue item's targets read.

    /// <summary>Writes the function of a DropGlue item by its Type's row (<see cref="DropGlue.Row"/>), or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The DropGlue item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteGlue(InstanceCollector collector, int item, TextWriter output)
    {
        var type = collector.Set.Items[item].RepresentativeType!;
        (this.collector, this.item, this.output, this.next, this.glued) = (collector, item, output, 0, 0);
        if (DropGlue.Row(type) is not { } row)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A glue item's Type is of a kind MirTypes.MayNeedDrop admits.
            return;
        }

        if (row is not (DropGlueRow.Release or DropGlueRow.Parts))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, default); // C5a, C5b, C6b, C8d and C8e write these rows.
            return;
        }

        this.abi = this.module.Glue(item);
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        if (row == DropGlueRow.Release)
        {
            this.Destroy(DropGlue.Release, new('p', 0));
        }
        else
        {
            this.Level(type);
        }

        output.Write("  ret void\n}\n");
    }

    // One level of a struct, Tuple, enum or fixed array: its parts in destruction order, each of the item's targets read once.
    private void Level(BoundType type)
    {
        var (parts, start) = (this.parts, this.parts.Count);
        DropGlue.Parts(this.typeContext, type, parts);
        if (this.Laid(type) is { } record)
        {
            if (AdtDef.IsEnum(type))
            {
                this.Cases(type, record, start);
            }
            else
            {
                for (var i = start; i < parts.Count; i++)
                {
                    this.Part(type, record, parts[i]);
                }
            }

            if (this.glued != this.collector.Set.Targets(this.item).Length)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets exactly the parts that may need a drop.
            }
        }

        parts.RemoveRange(start, parts.Count - start);
    }

    // SPEC 16.3.2: an enum's active Case payload, under one switch on its tag (Layout.Tag, at the record's offset 0) over the Cases holding
    // a part that may need a drop; any other tag destroys nothing.
    private void Cases(BoundType type, ValueLowering record, int start)
    {
        var (parts, output, id) = (this.parts, this.output, this.next++);
        var tag = this.LoadAt(new('p', 0), Layout.Tag.Layout);
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

            this.Part(type, record, parts[i]);
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

    // A direct part: the destructor instance, or the part's glue at its address or each element's; nothing for a part needing no drop.
    private void Part(BoundType type, ValueLowering record, in DropPart part)
    {
        if (part.Kind != DropPartKind.Destructor && !MirTypes.MayNeedDrop(part.Type, this.seen))
        {
            return;
        }

        if (this.NextTarget() is not { } target)
        {
            return;
        }

        if (part.Kind == DropPartKind.Destructor)
        {
            if (this.module.Signature(target) is { } destructor)
            {
                this.Destroy(destructor, new('p', 0)); // Its receiver, the one parameter other than a location; none for a zero-sized Self.
            }
        }
        else if (type.Kind == BoundTypeKind.FixedArray)
        {
            this.Elements(type, target);
        }
        else
        {
            var kind = type.Kind == BoundTypeKind.Tuple ? MirAggregateKind.Tuple : MirAggregateKind.Adt;
            var (offset, _) = this.Component(kind, type, record, part.Ordinal, part.Index);
            this.Destroy(this.module.Glue(target), this.Offset(new('p', 0), offset));
        }
    }

    // SPEC 16.3.2: a fixed array's elements by decreasing index, the element's glue in the one counted loop (Loop): the k-th pass destroys
    // element n - 1 - k, at the last element's offset less the pass's; nothing for no element.
    private void Elements(BoundType type, int target)
    {
        if (type.Length > 0 && this.Laid(type.Components[0]) is { } element)
        {
            this.Loop(type, element, new Destroyed(target, (type.Length - 1) * element.Layout.Stride));
        }
    }

    // An i64 difference, last - offset.
    private Value Difference(long last, Value offset)
    {
        var difference = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(difference);
        this.output.Write(" = sub i64 ");
        WriteNumber(this.output, last);
        this.output.Write(", ");
        this.WriteValue(offset);
        this.output.Write('\n');
        return difference;
    }

    // A call of a routine whose one parameter other than the location is the given value, the location parameters the function's own.
    private void Destroy(FunctionAbi routine, Value value)
    {
        var parameters = routine.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        for (var i = 0; i < parameters.Length; i++)
        {
            this.arguments[i] = parameters[i].Kind switch
            {
                AbiParameterKind.Location => new('p', 1),
                AbiParameterKind.LocationLength => new('p', 2),
                _ => value,
            };
        }

        _ = this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length));
    }

    // The glue item's next target (I8: in DropGlue.Parts' order), or null with InternalInvariant_Kd where none is left.
    private int? NextTarget()
    {
        var targets = this.collector.Set.Targets(this.item);
        if (this.glued < targets.Length)
        {
            return targets[this.glued++].Item;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default);
        return null;
    }

    // A glue's pass over a fixed array: the element's glue at the element the pass destroys, the last first.
    private readonly struct Destroyed(int glue, long last) : IElementPass
    {
        public string Stem => "destroy";

        public void Write(FunctionCodegen codegen, Value offset)
            => codegen.Destroy(codegen.module.Glue(glue), codegen.Offset(new('p', 0), codegen.Difference(last, offset)));
    }
}
