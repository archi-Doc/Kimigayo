// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The values of FunctionCodegen (R5 package C 3.2 I4, I5 and I7): an operand's value and a constant's, a Type's representation in the
// item's instance, bool's widening and narrowing, a site's location (SPEC 22.5.1), and the spelling of the text's values and labels.
internal sealed partial class FunctionCodegen
{
    // An integer's spelling at its width: the signed value of its low bits (two's complement, IMPL 21.1.3), so an unsigned value above the
    // signed range is the negative number with the same bits.
    private static Int128 Signed(Int128 bits, int width) => width >= 128 ? bits : (bits << (128 - width)) >> (128 - width);

    // An operand's value in its Type's representation (Read; a value in memory is its address); false for a form not covered yet. A
    // zero-sized operand has no value. A PairAcquire Copies its Place or Reborrows it, as the item's view decides (SPEC 8.14), and both
    // write the Place's bits in its case Type: a Reborrow of an exclusive reference is the pointer the Place holds (IMPL 21.1.3), so it
    // needs no case decision here (DECISIONS R5-M87).
    private bool Operand(in MirOperand operand, out Value value)
    {
        value = default;
        switch (operand.Kind)
        {
            case MirOperandKind.Copy or MirOperandKind.Move or MirOperandKind.PairAcquire:
                return this.Read(operand.Place, out value);
            case MirOperandKind.Const:
                return this.Constant(this.body.Consts[operand.Arg], out value);
            default:
                throw new InvalidOperationException($"The MIR operand kind {operand.Kind} has no code generation row.");
        }
    }

    // A constant of a covered kind: Bool (stored 0 or 1), Integer of an integer or char Type or a raw pointer's null (SPEC 5.1), Float (its
    // IEEE bits), Unit (zero-sized).
    private bool Constant(in MirConst constant, out Value value)
    {
        value = default;
        if (constant.Kind is not (MirConstKind.Bool or MirConstKind.Integer or MirConstKind.Float or MirConstKind.Unit))
        {
            return false; // String other than into a string local (C8c), FnItem (C5a), Location (C10a), TypeToken (C6a, C9a).
        }

        if (this.Representation(constant.Type) is not { } lowering)
        {
            return true; // A broken contract, recorded.
        }

        if (constant.Kind == MirConstKind.Float)
        {
            value = new('x', Hexadecimal(constant.Bits, lowering.Layout.Size));
            return true;
        }

        var type = this.collector.TypeOf(this.item, constant.Type)!;
        if (constant.Kind == MirConstKind.Integer && type.Semantics == SemanticsKind.Raw)
        {
            value = new('n', 0); // SPEC 5.1: null, the one raw pointer constant.
            return true;
        }

        if (constant.Kind == MirConstKind.Integer && !type.HasIntegerArithmetic && !ReferenceEquals(type, BoundType.Char))
        {
            return false; // A later row's Type.
        }

        value = lowering.Layout.Size == 0 ? default : new('\0', Signed(constant.Bits, 8 * lowering.Layout.Size));
        return true;
    }

    // The representation of a Type of the body in the item's instance (I7): Laid of its instance Type.
    private ValueLowering? Representation(BoundType type) => this.Laid(this.collector.TypeOf(this.item, type));

    // The representation of a Type in the item's instance (I7); null, with InternalInvariant_Kd at the body, where TypeOf made none or Layout
    // has none (the collector laid out every Type of an admitted item).
    private ValueLowering? Laid(BoundType? closed)
    {
        if (closed is not null && this.module.Layout.Of(closed).Value is { } lowering)
        {
            return lowering;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default);
        return null;
    }

    // The representation of a covered operand's Type: its Place's, or a constant's in the item's instance (null after a record).
    private ValueLowering? LoweringOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.Representation(this.body.Consts[operand.Arg].Type) : this.LoweringOf(operand.Place);

    // SPEC 22.5.1 (I5; DECISIONS R5-M93): the location a callee, a runtime routine or an Abort at a site of the body reports, IMPL 21.3.1's
    // pair of a text and its byte length: in a caller-location body the location it received, its last two parameters (Abi.Of), which it
    // forwards, so a failure inside it reports the user's call; elsewhere the site's text as a pooled Location constant. False after a
    // record at a site without its source document, a broken contract.
    private bool Location(in SourceInfo source, out Site site)
    {
        var count = this.abi.Parameters.Length;
        if (this.abi.CallerLocation)
        {
            site = new(new('p', count - 2), new('p', count - 1));
            return true;
        }

        site = default;
        if (this.module.Locations.Text(this.body.Subject(source)) is not { } text)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // A site of a body has its source document.
            return false;
        }

        var constant = this.module.Constants.Intern(text, LlvmConstantKind.Location);
        site = new(new('@', constant), new('\0', this.module.Constants[constant].ByteLength));
        return true;
    }

    // IMPL 21.1.3: bool, the one Type whose computation (i1) and storage (i8) representations differ, widens to be stored and narrows to
    // be computed; every other value is the same in both.
    private Value Widen(ValueLowering lowering, Value value) => this.Convert("zext ", lowering.ComputationType, value, lowering.Layout.StorageType);

    private Value Narrow(ValueLowering lowering, Value value) => this.Convert("trunc ", lowering.Layout.StorageType, value, lowering.ComputationType);

    private Value Convert(string operation, string from, Value value, string to)
    {
        if (from == to)
        {
            return value;
        }

        var converted = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(converted);
        this.output.Write(" = ");
        this.output.Write(operation);
        this.output.Write(from);
        this.output.Write(' ');
        this.WriteValue(value);
        this.output.Write(" to ");
        this.output.Write(to);
        this.output.Write('\n');
        return converted;
    }

    // A branch target: the label of a block, b<index> (I1).
    private void WriteLabel(int block)
    {
        this.output.Write("label %b");
        WriteNumber(this.output, block);
    }

    private void WriteValue(Value value)
    {
        if (value.Prefix == 'h')
        {
            this.WriteHome((int)value.Number);
            return;
        }

        this.output.Write(this.Spelled(value, stackalloc char[SpellingLength]));
    }

    // A value's spelling into text (SpellingLength characters): a pooled constant's @<name>, a parameter's %<name>, its signature's, or
    // Spell's.
    private ReadOnlySpan<char> Spelled(Value value, Span<char> text)
    {
        if (value.Prefix is not ('@' or 'p'))
        {
            return Spell(value, text);
        }

        var name = value.Prefix == '@' ? this.module.Constants[(int)value.Number].Name : this.abi.Parameters[(int)value.Number].Name;
        text[0] = value.Prefix == '@' ? '@' : '%';
        name.CopyTo(text[1..]);
        return text[..(name.Length + 1)];
    }

    // A value of the text: %t<n> (Prefix t), the parameter Number of the function's signature, %<its name> (Prefix p; Abi.Of names them),
    // the copy slot %s<n> (Prefix s; never c, a check's success label), the pooled constant @<name> of index Number (Prefix @), the home of
    // local Number, its address (Prefix h), the floating constant whose double bits are Number (Prefix x, LLVM's hexadecimal spelling), the
    // null pointer (Prefix n) or the literal integer Number (Prefix NUL).
    private readonly record struct Value(char Prefix, Int128 Number);

    // A location as IMPL 21.3.1 passes it (Location): its text's address and its byte length.
    private readonly record struct Site(Value Text, Value Length);
}
