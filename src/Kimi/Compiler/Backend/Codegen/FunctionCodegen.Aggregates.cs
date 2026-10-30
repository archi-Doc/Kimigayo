// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The aggregates of FunctionCodegen (R5 package C 3.2, plans C2a, C2b and C5a; DECISIONS R5-M83, R5-M87, R4-M81, R5-M102): a Tuple, struct,
// enum Case, Closure environment or fixed array put by its components (a string constant written whole, DECISIONS R5-M118), an Array's
// elements put the same way in the buffer its storage row allocates (FunctionCodegen.Arrays' ArrayBuffer; DECISIONS R5-M129), and a Fill's
// one value at each element by one counted loop, the loop writer (Loop) the glue shares; a Dictionary from none is
// FunctionCodegen.Dictionaries' (DECISIONS R5-M124).
internal sealed partial class FunctionCodegen
{
    // One pass of a counted loop (Loop): its labels' stem and its work from the pass's element offset.
    private interface IElementPass
    {
        string Stem { get; }

        void Write(FunctionCodegen codegen, Value offset);
    }

    // An Aggregate (SPEC 4.3.1-4.3.4, 6.5.1-6.5.2, 7.7.1-7.7.2; IMPL 21.1.2-21.1.3): its operands' values in order, its Place located (SPEC
    // 13.7.1), then an enum Case's ordinal as its tag (Layout.Tag) and each value put at its component (Component), or a Fill's one value at
    // each element by one counted loop (DECISIONS R4-M81); noinit, from no operand, and a zero-sized Place store none. A string constant has
    // nothing to evaluate: it is written whole at its component once the Place is located (Evaluate and Put, the Use row's rule). A Closure's
    // environment is the Closure value, its captures placed as a Tuple's elements in the Place's own storage at every size, so forming it
    // allocates nothing (DECISIONS R5-M102). An Array's elements are put in the buffer ArrayBuffer makes for them, by the fixed-array rule
    // (DECISIONS R5-M129); a Dictionary from none is its initialize call (EmptyDictionary).
    private void Aggregate(in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var type = this.InstanceType(statement.Place)!;
        var kind = (MirAggregateKind)rvalue.Op;
        switch (kind)
        {
            case MirAggregateKind.Tuple or MirAggregateKind.Adt or MirAggregateKind.Closure or MirAggregateKind.Array:
            case MirAggregateKind.FixedArray or MirAggregateKind.Fill when type.Kind == BoundTypeKind.FixedArray:
                break;
            case MirAggregateKind.FixedArray when type.Kind == BoundTypeKind.Dictionary:
                this.EmptyDictionary(statement); // A Dictionary from none (FunctionCodegen.Dictionaries).
                return;
            default:
                throw new InvalidOperationException($"The MIR aggregate kind {kind} has no code generation row.");
        }

        var operands = this.body.Operands.Slice(rvalue.Start, rvalue.Count);
        this.arguments = this.arguments.Length < operands.Length ? new Value[operands.Length] : this.arguments;
        for (var i = 0; i < operands.Length; i++)
        {
            if (!this.Evaluate(operands[i], out this.arguments[i]))
            {
                return; // A broken contract, recorded.
            }
        }

        if (!this.Destination(statement, out var address) || this.LoweringOf(statement.Place) is not { Layout.Size: > 0 } record)
        {
            return;
        }

        if (kind == MirAggregateKind.Fill)
        {
            this.Fill(address, type, this.arguments[0]);
            return;
        }

        if (kind == MirAggregateKind.Array)
        {
            if (this.ArrayBuffer(statement.Source, address, type, record, operands.Length) is not { } buffer)
            {
                return; // Recorded.
            }

            address = buffer; // Its elements are put in its buffer by the fixed-array rule (Component).
        }

        if (kind == MirAggregateKind.Adt && AdtDef.IsEnum(type))
        {
            this.StoreAt(address, Layout.Tag.Layout, new('\0', rvalue.Arg)); // The Case ordinal, at the record's offset 0.
        }

        for (var i = 0; i < operands.Length; i++)
        {
            var (offset, component) = this.Component(kind, type, record, rvalue.Arg, i);
            if (this.Laid(component) is not { Layout.Size: > 0 } lowering)
            {
                continue;
            }

            this.Put(this.Offset(address, offset), component, lowering, operands[i], this.arguments[i]);
        }
    }

    // The component of an aggregate its index-th operand initializes (IMPL 21.1.2; the validator's Component), in the aggregate's record:
    // its offset and its Type in the item's instance. A Tuple's element, a Closure's capture and a struct's own Field are at the record's
    // Field offsets, a struct's base first and at 0 (its complete base is a prefix, X1); an enum Case's payload element is at the payload's
    // offset, the record's Field 1, plus its offset in the Case's payload Tuple; a fixed array's element, and an Array's in its buffer, is at
    // its position times the element's stride.
    private (long Offset, BoundType Type) Component(MirAggregateKind kind, BoundType type, ValueLowering record, int ordinal, int index)
    {
        var offsets = record.Layout.FieldOffsets.Span;
        switch (kind)
        {
            case MirAggregateKind.FixedArray or MirAggregateKind.Array:
                return (index * (long)(this.Laid(type.Components[0])?.Layout.Stride ?? 0), type.Components[0]);
            case MirAggregateKind.Tuple or MirAggregateKind.Closure:
                return (offsets[index], type.Components[index]);
            case MirAggregateKind.Adt when AdtDef.IsEnum(type):
                var payload = AdtDef.CaseTypes(type)![ordinal]!;
                return (offsets[1] + (this.Laid(payload)?.Layout.FieldOffsets.Span[index] ?? 0), payload.Components[index]);
            case MirAggregateKind.Adt when AdtDef.Base(type) is { } parent:
                return index == 0 ? (0, parent) : (offsets[index - 1], AdtDef.FieldType(type, index - 1)!);
            case MirAggregateKind.Adt:
                return (offsets[index], AdtDef.FieldType(type, index)!);
            default:
                throw new InvalidOperationException($"The MIR aggregate kind {kind} has no parts.");
        }
    }

    // SPEC 4.3.3 (DECISIONS R4-M81): a Fill's value put at each element of the instance's closed length by one counted loop (Loop), so the
    // text does not grow with the length (its Place is not zero-sized, so the length is not 0).
    private void Fill(Value address, BoundType type, Value value)
    {
        if (this.Laid(type.Components[0]) is { } element)
        {
            this.Loop(type.Length, element, new Filled(address, type.Components[0], element, value));
        }
    }

    // The counted i64 loop of a number of passes, not 0, over a fixed array's elements (a Fill's closed length, a glue's run; DECISIONS
    // R4-M81): it owns its counter, and the body writes each pass's work from the pass's element offset, the counter times the element's
    // stride. The loop is entered through a label of its own,
    // which the counter's phi names; the pass names the labels, its stem followed by s (the entry), nothing (the loop) and ed (the exit),
    // so a Fill's are fills, fill and filled and a glue's destroys, destroy and destroyed; a struct body, so a warm write allocates nothing.
    private void Loop<TBody>(long count, ValueLowering element, in TBody body)
        where TBody : struct, IElementPass
    {
        var id = this.next;
        var index = new Value('t', id + 1);
        var next = new Value('t', id + 2);
        var offset = new Value('t', id + 3);
        var done = new Value('t', id + 4);
        this.next += 5;
        var output = this.output;
        output.Write("  br label %");
        this.LoopLabel(body.Stem, "s", id);
        output.Write('\n');
        this.LoopLabel(body.Stem, "s", id);
        output.Write(":\n  br label %");
        this.LoopLabel(body.Stem, string.Empty, id);
        output.Write('\n');
        this.LoopLabel(body.Stem, string.Empty, id);
        output.Write(":\n  ");
        this.WriteValue(index);
        output.Write(" = phi i64 [ 0, %");
        this.LoopLabel(body.Stem, "s", id);
        output.Write(" ], [ ");
        this.WriteValue(next);
        output.Write(", %");
        this.LoopLabel(body.Stem, string.Empty, id);
        output.Write(" ]\n  ");
        this.WriteValue(offset);
        output.Write(" = mul i64 ");
        this.WriteValue(index);
        output.Write(", ");
        WriteNumber(output, element.Layout.Stride);
        output.Write('\n');
        body.Write(this, offset);
        output.Write("  ");
        this.WriteValue(next);
        output.Write(" = add i64 ");
        this.WriteValue(index);
        output.Write(", 1\n  ");
        this.WriteValue(done);
        output.Write(" = icmp eq i64 ");
        this.WriteValue(next);
        output.Write(", ");
        WriteNumber(output, count);
        output.Write("\n  br i1 ");
        this.WriteValue(done);
        output.Write(", label %");
        this.LoopLabel(body.Stem, "ed", id);
        output.Write(", label %");
        this.LoopLabel(body.Stem, string.Empty, id);
        output.Write('\n');
        this.LoopLabel(body.Stem, "ed", id);
        output.Write(":\n");
    }

    // A label of the counted loop numbered id: the pass's stem and a suffix.
    private void LoopLabel(string stem, string suffix, int id)
    {
        this.output.Write(stem);
        this.output.Write(suffix);
        WriteNumber(this.output, id);
    }

    // A Fill's pass: its value put at the element.
    private readonly struct Filled(Value address, BoundType type, ValueLowering element, Value value) : IElementPass
    {
        public string Stem => "fill";

        public void Write(FunctionCodegen codegen, Value offset) => codegen.Put(codegen.Offset(address, offset), type, element, value);
    }
}
