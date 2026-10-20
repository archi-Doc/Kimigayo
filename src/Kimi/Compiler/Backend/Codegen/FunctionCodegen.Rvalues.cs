// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen (R5 package C 3.2, plan C2b; C0c brings Ref forward, DECISIONS R5-M78, and C2a the fixed-array
// aggregate, R5-M83), each into its Place through the place writer.
internal sealed partial class FunctionCodegen
{
    // A Use: its operand in the Place's storage representation, nothing for a zero-sized Place; a string constant written whole; a copy of
    // a slot Type is C2b's (memcpy).
    private void Use(in MirStatement statement)
    {
        var operand = this.body.Operands[statement.Rvalue.Start];
        var type = this.InstanceType(statement.Place)!;
        if (operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } &&
            ReferenceEquals(type, BoundType.String))
        {
            this.Text(statement, text);
            return;
        }

        if (this.LoweringOf(statement.Place) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        if (Abi.HasResultSlot(type, lowering) || !this.Operand(operand, out var value))
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

    // An Aggregate (SPEC 4.3.1-4.3.4): a fixed array from its elements in order, each operand's value stored at its position times the
    // element's stride once the Place is located; noinit, from none, stores none; a Fill its one value at each element (Fill). A Tuple,
    // struct, enum Case or Closure environment and an element of a slot Type are C2b's rows, an Array or Dictionary from none C8d's and C8e's.
    private void Aggregate(in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var fill = (MirAggregateKind)rvalue.Op == MirAggregateKind.Fill;
        if ((!fill && (MirAggregateKind)rvalue.Op != MirAggregateKind.FixedArray) || this.InstanceType(statement.Place) is not { Kind: BoundTypeKind.FixedArray } type)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.module.Layout.Of(type.Components[0]).Value is not { } element)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // The collector laid out the Place's Type.
            return;
        }

        if (Abi.HasResultSlot(type.Components[0], element))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // An element of a slot Type is copied by C2b's row.
            return;
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

        if (!this.Destination(statement, out var address) || element.Layout.Size == 0)
        {
            return;
        }

        if (fill)
        {
            this.Fill(address, element.Layout, this.arguments[0], type.Length);
            return;
        }

        for (var i = 0; i < operands.Length; i++)
        {
            this.StoreAt(this.Offset(address, (long)i * element.Layout.Stride), element.Layout, this.arguments[i]);
        }
    }

    // SPEC 4.3.3 (DECISIONS R4-M81): a Fill's value stored at each element of the instance's closed length by one counted i64 loop, so the
    // text does not grow with the length; none at length 0. The loop is entered through a label of its own, which the counter's phi names.
    private void Fill(Value address, TypeLayout element, Value value, long length)
    {
        if (length == 0)
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
        WriteNumber(output, element.Stride);
        output.Write('\n');
        this.StoreAt(this.Offset(address, offset), element, value);
        output.Write("  ");
        this.WriteValue(next);
        output.Write(" = add i64 ");
        this.WriteValue(index);
        output.Write(", 1\n  ");
        this.WriteValue(done);
        output.Write(" = icmp eq i64 ");
        this.WriteValue(next);
        output.Write(", ");
        WriteNumber(output, length);
        output.Write("\n  br i1 ");
        this.WriteValue(done);
        Name(output, ", label %filled", id);
        Name(output, ", label %fill", id);
        Name(output, "\nfilled", id);
        output.Write(":\n");
    }
}
