// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen (R5 package C 3.2, plan C2b; DECISIONS R5-M78, R5-M83, R5-M87, R4-M81), each computed from its operands,
// then put into its Place through the place writer (SPEC 13.7.1): a Use, a Ref, an aggregate, a Discriminant, a Len and an Adapt.
internal sealed partial class FunctionCodegen
{
    // A Use: its operand put into the Place in its representation (a value in memory copied; nothing for a zero-sized Place); a string
    // constant written whole.
    private void Use(in MirStatement statement)
    {
        var operand = this.body.Operands[statement.Rvalue.Start];
        if (operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } &&
            ReferenceEquals(this.InstanceType(statement.Place), BoundType.String))
        {
            this.Text(statement, text);
            return;
        }

        if (!this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        this.Store(statement, value);
    }

    // A string constant stored into a string Place (the string representation, implementation-defined by IMPL 21.1.3: RuntimeAbi.String):
    // its UTF-8 bytes a pooled constant, the Static release kind, so never released; the empty string has no bytes (null, length 0).
    private void Text(in MirStatement statement, string text)
    {
        if (!this.Destination(statement, out var address) || this.LoweringOf(statement.Place) is not { } lowering)
        {
            return;
        }

        var output = this.output;
        output.Write("  store ");
        output.Write(lowering.Layout.StorageType);
        output.Write(" { ptr ");
        if (text.Length == 0)
        {
            output.Write("null, i64 0");
        }
        else
        {
            var constant = this.module.Constants[this.module.Constants.Intern(text, LlvmConstantKind.Text)];
            output.Write('@');
            output.Write(constant.Name);
            output.Write(", i64 ");
            WriteNumber(output, constant.ByteLength);
        }

        output.Write(", i8 ");
        WriteNumber(output, RuntimeAbi.StaticReleaseKind);
        output.Write(" }");
        this.WriteAccess(address, lowering.Layout);
    }

    // Ref (IMPL 21.1.3: a shared, exclusive, two-phase or raw borrow is one nonnull pointer to its Place's storage): the address the place
    // writer gives, stored into the statement's Place.
    private void Ref(in MirStatement statement)
    {
        if (!this.Address(statement.Rvalue.Place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        this.Store(statement, address);
    }

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

    // A Discriminant (SPEC 6.5.1, 14.8): the Case ordinal its enum Place's tag holds (Layout.Tag, at the record's offset 0), widened to the
    // Place's isize.
    private void Discriminant(in MirStatement statement)
    {
        if (!this.Address(statement.Rvalue.Place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.LoweringOf(statement.Place) is { } isize)
        {
            var tag = Layout.Tag;
            this.Store(statement, this.Convert("zext ", tag.ComputationType, this.LoadAt(address, tag.Layout), isize.ComputationType));
        }
    }

    // A Len (SPEC 4.7.3): the length of a fixed array, Array or Slice Place (Length), its Place located first, so an Index on its path
    // checks its bounds. A Dictionary's count (its DictionaryStorage length word) is C8e's row; a string's has no Len row yet (C8c).
    private void Len(in MirStatement statement)
    {
        var place = statement.Rvalue.Place;
        var type = this.InstanceType(place)!;
        if (type.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Slice) || !this.Address(place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.LoweringOf(place) is { } record)
        {
            this.Store(statement, this.Length(type, record, address));
        }
    }

    // An Adapt (SPEC 8.14, 13.5.3): the ordinary rvalue of the operation the item's view selects (MirCases.Adapt), written by that rvalue's
    // row: an Identity its operand, a pointer conversion or a non-exclusive upcast a Proven Cast, a borrow or an address a Ref. An object
    // creation allocates, C6a's row; a view that selects none is a broken contract (Binding admitted each case).
    private void Adapt(in MirStatement statement)
    {
        var rvalue = this.cases.Adapt(statement.Rvalue, out var operation);
        switch (operation)
        {
            case ConversionBinding.None:
                this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source);
                return;
            case ConversionBinding.ObjectCreation:
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // C6a creates the object.
                return;
            default:
                this.Assign(statement with { Rvalue = rvalue });
                return;
        }
    }
}
