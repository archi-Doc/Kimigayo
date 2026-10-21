// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The aggregates of FunctionCodegen (R5 package C 3.2, plans C2a and C2b; DECISIONS R5-M83, R5-M87, R4-M81): a Tuple, struct, enum Case or
// fixed array put by its components, and a Fill's one value at each element by one counted loop.
internal sealed partial class FunctionCodegen
{
    // An Aggregate (SPEC 4.3.1-4.3.4, 6.5.1-6.5.2; IMPL 21.1.2-21.1.3): its operands' values in order, its Place located (SPEC 13.7.1),
    // then an enum Case's ordinal as its tag (Layout.Tag) and each value put at its component (Component), or a Fill's one value at each
    // element by one counted loop (DECISIONS R4-M81); noinit, from no operand, and a zero-sized Place store none. A Closure environment is
    // C5a's row, an Array or Dictionary from none C8d's and C8e's.
    private void Aggregate(in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var type = this.InstanceType(statement.Place)!;
        var kind = (MirAggregateKind)rvalue.Op;
        switch (kind)
        {
            case MirAggregateKind.Tuple or MirAggregateKind.Adt:
            case MirAggregateKind.FixedArray or MirAggregateKind.Fill when type.Kind == BoundTypeKind.FixedArray:
                break;
            case MirAggregateKind.Closure or MirAggregateKind.FixedArray:
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // C5a's environment; C8d's and C8e's collections from none.
                return;
            default:
                throw new InvalidOperationException($"The MIR aggregate kind {kind} has no code generation row.");
        }

        var operands = this.body.Operands.Slice(rvalue.Start, rvalue.Count);
        this.arguments = this.arguments.Length < operands.Length ? new Value[operands.Length] : this.arguments;
        for (var i = 0; i < operands.Length; i++)
        {
            if (!this.Operand(operands[i], out this.arguments[i]))
            {
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
                return;
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

        if (kind == MirAggregateKind.Adt && AdtDef.IsEnum(type))
        {
            this.StoreAt(address, Layout.Tag.Layout, new('\0', rvalue.Arg)); // The Case ordinal, at the record's offset 0.
        }

        for (var i = 0; i < operands.Length; i++)
        {
            var (offset, component) = this.Component(kind, type, record, rvalue.Arg, i);
            if (this.Laid(component) is { Layout.Size: > 0 } lowering)
            {
                this.Put(this.Offset(address, offset), component, lowering, this.arguments[i]);
            }
        }
    }

    // The component of an aggregate its index-th operand initializes (IMPL 21.1.2; the validator's Component), in the aggregate's record:
    // its offset and its Type in the item's instance. A Tuple's element and a struct's own Field are at the record's Field offsets, a
    // struct's base first and at 0 (its complete base is a prefix, X1); an enum Case's payload element is at the payload's offset, the
    // record's Field 1, plus its offset in the Case's payload Tuple; a fixed array's element is at its position times the element's stride.
    private (long Offset, BoundType Type) Component(MirAggregateKind kind, BoundType type, ValueLowering record, int ordinal, int index)
    {
        var offsets = record.Layout.FieldOffsets.Span;
        switch (kind)
        {
            case MirAggregateKind.FixedArray:
                return (index * (long)(this.Laid(type.Components[0])?.Layout.Stride ?? 0), type.Components[0]);
            case MirAggregateKind.Tuple:
                return (offsets[index], type.Components[index]);
            case MirAggregateKind.Adt when AdtDef.IsEnum(type):
                var payload = AdtDef.CaseTypes(type)![ordinal];
                return (offsets[1] + (this.Laid(payload)?.Layout.FieldOffsets.Span[index] ?? 0), payload.Components[index]);
            case MirAggregateKind.Adt when AdtDef.Base(type) is { } parent:
                return index == 0 ? (0, parent) : (offsets[index - 1], AdtDef.FieldType(type, index - 1)!);
            case MirAggregateKind.Adt:
                return (offsets[index], AdtDef.FieldType(type, index)!);
            default:
                throw new InvalidOperationException($"The MIR aggregate kind {kind} has no parts.");
        }
    }

    // SPEC 4.3.3 (DECISIONS R4-M81): a Fill's value put at each element of the instance's closed length by one counted i64 loop, so the
    // text does not grow with the length (its Place is not zero-sized, so the length is not 0). The loop is entered through a label of its
    // own, which the counter's phi names.
    private void Fill(Value address, BoundType type, Value value)
    {
        if (this.Laid(type.Components[0]) is not { } element)
        {
            return;
        }

        var id = this.next;
        var (index, next, offset, done) = (new Value('t', id + 1), new Value('t', id + 2), new Value('t', id + 3), new Value('t', id + 4));
        this.next += 5;
        var output = this.output;
        Name(output, "  br label %fills", id);
        Name(output, "\nfills", id);
        Name(output, ":\n  br label %fill", id);
        Name(output, "\nfill", id);
        output.Write(":\n  ");
        this.WriteValue(index);
        Name(output, " = phi i64 [ 0, %fills", id);
        output.Write(" ], [ ");
        this.WriteValue(next);
        Name(output, ", %fill", id);
        output.Write(" ]\n  ");
        this.WriteValue(offset);
        output.Write(" = mul i64 ");
        this.WriteValue(index);
        output.Write(", ");
        WriteNumber(output, element.Layout.Stride);
        output.Write('\n');
        this.Put(this.Offset(address, offset), type.Components[0], element, value);
        output.Write("  ");
        this.WriteValue(next);
        output.Write(" = add i64 ");
        this.WriteValue(index);
        output.Write(", 1\n  ");
        this.WriteValue(done);
        output.Write(" = icmp eq i64 ");
        this.WriteValue(next);
        output.Write(", ");
        WriteNumber(output, type.Length);
        output.Write("\n  br i1 ");
        this.WriteValue(done);
        Name(output, ", label %filled", id);
        Name(output, ", label %fill", id);
        Name(output, "\nfilled", id);
        output.Write(":\n");
    }
}
