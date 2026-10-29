// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The loads, stores and copies of FunctionCodegen through its place writer (R5 plan C2a, C2b, package C 3.2 I2, I4, I7 and I10; DECISIONS
// R5-M83, R5-M87): an Assign's destination and the value put into it, an operand put at an address (a string constant, the one operand
// without a value, written whole as its record), a Place's value read in its representation and the value an address holds. A value is
// read and put in its representation (InMemory): a value in memory is its address, its bytes copied by memcpy when it is put.
internal sealed partial class FunctionCodegen
{
    // IMPL 21.1.3, 21.3.1 (DECISIONS R5-M87): whether a value of a Type is in memory, so it is read as its address and copied by memcpy: a
    // slot Type of nonzero size (Abi.HasResultSlot) whose representation is not one first-class LLVM value. A scalar, a pointer and the
    // string's record (RuntimeAbi.String, its three Fields: the one slot Type with a first-class representation) are loaded and stored.
    private static bool InMemory(BoundType type, ValueLowering lowering) => Abi.HasResultSlot(type, lowering) && !ReferenceEquals(lowering, RuntimeAbi.String);

    // The address of an Assign's Place, located after its rvalue (SPEC 13.7.1), or false: a local without storage, whose value is none
    // (I2), or after a broken contract's record (Address).
    private bool Destination(in MirStatement statement, out Value address)
    {
        address = default;
        return !this.Unstored(statement.Place) && this.Address(statement.Place, out address);
    }

    // An Assign's value put into its Place once the Place is located; nothing for a zero-sized Place.
    private void Store(in MirStatement statement, Value value)
    {
        if (this.Destination(statement, out var address) && this.LoweringOf(statement.Place) is { Layout.Size: > 0 } lowering)
        {
            this.Put(address, this.InstanceType(statement.Place)!, lowering, value);
        }
    }

    // A value of nonzero size put at an address in its Type's representation (its Type in the item's instance): a value in memory copied
    // from the address Read gave, by memcpy with the Type's alignment on both pointers (package C 4; ModuleCodegen declares it on request),
    // any other stored in its storage representation. The builder never copies between overlapping Places (DECISIONS R5-M87).
    private void Put(Value address, BoundType type, ValueLowering lowering, Value value)
    {
        if (!InMemory(type, lowering))
        {
            this.StoreAt(address, lowering.Layout, value);
            return;
        }

        this.module.Request(ModuleCodegen.Needs.Memory);
        var (output, layout) = (this.output, lowering.Layout);
        output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr align ");
        WriteNumber(output, layout.Alignment);
        output.Write(' ');
        this.WriteValue(address);
        output.Write(", ptr align ");
        WriteNumber(output, layout.Alignment);
        output.Write(' ');
        this.WriteValue(value);
        output.Write(", i64 ");
        WriteNumber(output, layout.Size);
        output.Write(", i1 false)\n");
    }

    // An operand's value, read before its Place is located (SPEC 13.7.1; Operand): none for a string constant, which has nothing to
    // evaluate (Put writes it whole). False after a broken contract's record (Operand).
    private bool Evaluate(in MirOperand operand, out Value value)
    {
        value = default;
        return this.TextOf(operand) is not null || this.Operand(operand, out value);
    }

    // An evaluated operand of nonzero size put at an address in its Type's representation: a string constant written whole as its record
    // (Text), any other its value (Put).
    private void Put(Value address, BoundType type, ValueLowering lowering, in MirOperand operand, Value value)
    {
        if (this.TextOf(operand) is { } text)
        {
            this.Text(address, text);
        }
        else
        {
            this.Put(address, type, lowering, value);
        }
    }

    // An operand of nonzero size evaluated (Evaluate), then put at an address in its Type's representation (above). False after a broken
    // contract's record (Evaluate).
    private bool Put(Value address, BoundType type, ValueLowering lowering, in MirOperand operand)
    {
        if (!this.Evaluate(operand, out var value))
        {
            return false;
        }

        this.Put(address, type, lowering, operand, value);
        return true;
    }

    // The text of a string constant operand of the string Type, which has no value of its own: it is written whole where it is put (Text).
    private string? TextOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } constant &&
            ReferenceEquals(constant.Type, BoundType.String) ? text : null;

    // A string constant stored at an address (the string representation, implementation-defined by IMPL 21.1.3: RuntimeAbi.String): its
    // bytes (TextBytes), the Static release kind, so never released.
    private void Text(Value address, string text)
    {
        var output = this.output;
        var lowering = RuntimeAbi.String;
        var bytes = this.TextBytes(text);
        output.Write("  store ");
        output.Write(lowering.Layout.StorageType);
        output.Write(" { ptr ");
        this.WriteValue(bytes.Data);
        output.Write(", i64 ");
        this.WriteValue(bytes.Length);
        output.Write(", i8 ");
        WriteNumber(output, RuntimeAbi.StaticReleaseKind);
        output.Write(" }");
        this.WriteAccess(address, lowering.Layout);
    }

    // A string constant's bytes: its UTF-8 bytes a pooled constant and their count; the empty string has none (null, length 0).
    private StringBytes TextBytes(string text)
    {
        if (text.Length == 0)
        {
            return new(new('n', 0), new('\0', 0));
        }

        var constant = this.module.Constants.Intern(text, LlvmConstantKind.Text);
        return new(new('@', constant), new('\0', this.module.Constants[constant].ByteLength));
    }

    // A Place's value in its representation (I4): loaded in its storage representation, or its address when the value is in memory; none
    // for a zero-sized one; false after a broken contract's record (Address).
    private bool Read(MirPlace place, out Value value)
    {
        value = default;
        if (this.Unstored(place))
        {
            return true;
        }

        if (!this.Address(place, out var address))
        {
            return false;
        }

        if (this.LoweringOf(place) is { Layout.Size: > 0 } lowering)
        {
            value = this.ValueAt(address, this.InstanceType(place)!, lowering);
        }

        return true;
    }

    // The value of nonzero size an address holds, in its Type's representation (I4): the address itself for a value in memory, else loaded.
    private Value ValueAt(Value address, BoundType type, ValueLowering lowering) => InMemory(type, lowering) ? address : this.LoadAt(address, lowering.Layout);

    // Whether a Place is a local without storage (zero-sized, I2).
    private bool Unstored(MirPlace place) => place is { Root: >= 0, Projection: 0 } && this.slots[place.Root].Home == Home.None;

    // The Type of a Place in the item's instance: a local's, or its projection's.
    private BoundType? InstanceType(MirPlace place)
        => place is { Root: >= 0, Projection: 0 } ? this.types[place.Root] : this.collector.TypeOf(this.item, this.body.TypeOf(place)!);

    // The Type of an operand in the item's instance: a constant's, or its Place's.
    private BoundType? InstanceType(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.collector.TypeOf(this.item, this.body.Consts[operand.Arg].Type) : this.InstanceType(operand.Place);

    // The representation of a Place's Type in the item's instance (I7): a local's slot's, or its Type's (null after a record).
    private ValueLowering? LoweringOf(MirPlace place)
        => place is { Root: >= 0, Projection: 0 } ? this.slots[place.Root].Value : this.Representation(this.body.TypeOf(place)!);

    // A local's value, loaded from its home in its storage representation (I4).
    private Value Load(int local) => this.LoadAt(new('h', local), this.slots[local].Value.Layout);

    private Value LoadAt(Value address, TypeLayout layout)
    {
        var value = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(value);
        this.output.Write(" = load ");
        this.output.Write(layout.StorageType);
        this.WriteAccess(address, layout);
        return value;
    }

    private void StoreAt(Value address, TypeLayout layout, Value value)
    {
        this.output.Write("  store ");
        this.output.Write(layout.StorageType);
        this.output.Write(' ');
        this.WriteValue(value);
        this.WriteAccess(address, layout);
    }

    // The end of a load or store: the address and its Layout's alignment.
    private void WriteAccess(Value address, TypeLayout layout)
    {
        this.output.Write(", ptr ");
        this.WriteValue(address);
        this.output.Write(", align ");
        WriteNumber(this.output, layout.Alignment);
        this.output.Write('\n');
    }

    // A string's bytes, the string record's first two Fields (IMPL 21.1.3): their address and their count.
    private readonly record struct StringBytes(Value Data, Value Length);
}
