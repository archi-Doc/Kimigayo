// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The place writer of FunctionCodegen (R5 plan C2a, C2b, package C 3.2 I4, I5, I7 and I9; DECISIONS R5-M83, R5-M87), the one owner of a
// Place's address, and the loads, stores and copies through it. A Place is its root's home, a local's (a static's slot is C7b's row; a
// zero-sized local's home is the module's zero-sized symbol, which ModuleCodegen defines on request, IMPL 21.1.3), then each step of its
// interned projection chain, root first, by one row per MirProjectionKind, in the Layout of the prefix's Type in the item's instance (IMPL
// 21.1): a Field at its Field offset; a Base at the derived struct's own address, its complete base being a prefix (X1); a ConstIndex at
// its position times the element's stride, which the validator proved below the length; a Downcast at the enum's payload offset; a Deref of
// a reference or raw pointer the address it stores (IMPL 21.1.3), of an object handle or borrow the payload, C6a's row; a PairLayer the
// Place itself in the owner case, else the referent of its case Type by the Deref row (MirCases.Layer under the item's view, SPEC 13.5.5);
// an Index its isize position checked against the length, a fixed array's closed length or an Array's or Slice's length word, unsigned, so
// a negative position fails, Aborting with KIMI_E_INDEX_BOUNDS (SPEC 4.7.4, 22.5.2) at its position local's location, which the builder
// locates at the selection (DECISIONS R4-M56, SPEC 22.5.1), then the element at the position times the stride from the fixed array's or the
// buffer word's address. Offsets are written without inbounds (I9). A value is read and put in its representation (InMemory): a value in
// memory is its address, its bytes copied by memcpy when it is put.
internal sealed partial class FunctionCodegen
{
    // IMPL 21.1.3, 21.3.1 (DECISIONS R5-M87): whether a value of a Type is in memory, so it is read as its address and copied by memcpy: a
    // slot Type of nonzero size (Abi.HasResultSlot) whose representation is not one first-class LLVM value. A scalar, a pointer and the
    // string's record (RuntimeAbi.String, its three Fields: the one slot Type with a first-class representation) are loaded and stored.
    private static bool InMemory(BoundType type, ValueLowering lowering) => Abi.HasResultSlot(type, lowering) && !ReferenceEquals(lowering, RuntimeAbi.String);

    // A Place's address (the rows above): false for a row not written yet, which the caller records at its site; true without an address
    // after a record.
    private bool Address(MirPlace place, out Value address)
    {
        address = default;
        if (place.Root < 0)
        {
            return false; // A static's slot (C7b).
        }

        if (place.Projection == 0)
        {
            address = new('h', place.Root); // A zero-sized local's is the zero-sized symbol (WriteHome).
            return true;
        }

        var prefix = place with { Projection = this.body.Projections[place.Projection].Prefix };
        return this.Address(prefix, out var start) && this.Step(place, prefix, start, out address);
    }

    // One step of a Place from its prefix's address.
    private bool Step(MirPlace place, MirPlace prefix, Value start, out Value address)
    {
        var row = this.body.Projections[place.Projection];
        address = start;
        switch (row.Kind)
        {
            case MirProjectionKind.Field:
                address = this.Part(prefix, start, row.Arg);
                return true;
            case MirProjectionKind.Base:
                return true;
            case MirProjectionKind.ConstIndex:
                if (this.Representation(row.Type!) is { } element)
                {
                    address = this.Offset(start, (long)row.Arg * element.Layout.Stride);
                }

                return true;
            case MirProjectionKind.Downcast:
                address = this.Part(prefix, start, 1); // The payload, after the i32 tag.
                return true;
            case MirProjectionKind.Index:
                return this.Element(row, prefix, start, out address);
            case MirProjectionKind.Deref when this.InstanceType(prefix) is { } pointer:
                return this.Referent(pointer, start, out address);
            case MirProjectionKind.Deref:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector made every Type of an admitted item.
                return true;
            case MirProjectionKind.PairLayer:
                return this.cases.Layer(place) is not { } layer || this.Referent(layer, start, out address);
            default:
                throw new InvalidOperationException($"The MIR projection kind {row.Kind} has no code generation row.");
        }
    }

    // A part of a Place at its record's offset of the index, a Field's or an enum's payload; the Place's address after a record.
    private Value Part(MirPlace place, Value start, int index)
        => this.LoweringOf(place) is { } whole ? this.Offset(start, whole.Layout.FieldOffsets.Span[index]) : start;

    // A Deref by its Type: a reference or raw pointer stores its referent's address; an object handle's or borrow's payload is C6a's row.
    private bool Referent(BoundType pointer, Value start, out Value address)
    {
        address = start;
        if (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.Raw })
        {
            return false;
        }

        address = this.LoadAt(start, Layout.Row(pointer).Layout);
        return true;
    }

    // An Index: the bounds check at the position local's location, then the element's address.
    private bool Element(in MirProjection row, MirPlace prefix, Value start, out Value address)
    {
        address = start;
        var position = row.Arg;
        var location = this.Location(this.body.Locals[position].Source);
        if (location < 0 || this.Representation(row.Type!) is not { } element || this.LoweringOf(prefix) is not { } sequence)
        {
            return true; // Recorded.
        }

        var type = this.InstanceType(prefix)!;
        var isize = this.slots[position].Value;
        var index = this.Load(position);
        // An Array's or Slice's buffer word, a pointer (IMPL 21.1.3).
        var buffer = type.Kind == BoundTypeKind.FixedArray ? start : this.LoadAt(start, RuntimeAbi.StringReference.Layout);
        var length = this.Length(type, sequence, start);

        var id = this.next++;
        var output = this.output;
        output.Write("  %invalid");
        WriteNumber(output, id);
        output.Write(" = icmp uge ");
        output.Write(isize.ComputationType);
        output.Write(' ');
        this.WriteValue(index);
        output.Write(", ");
        this.WriteValue(length);
        output.Write('\n');
        LlvmScalarText.WriteFailure(output, "%invalid", AbortReasons.IndexBounds, this.Failure(id, location, stackalloc char[3 * SpellingLength]));
        if (element.Layout.Stride == 0)
        {
            address = buffer; // A zero-sized element needs no address of its own; its check remains.
            return true;
        }

        var bytes = new Value('t', this.next++);
        output.Write("  ");
        this.WriteValue(bytes);
        output.Write(" = mul ");
        output.Write(isize.ComputationType);
        output.Write(' ');
        this.WriteValue(index);
        output.Write(", ");
        WriteNumber(output, element.Layout.Stride);
        output.Write('\n');
        address = this.Offset(buffer, bytes);
        return true;
    }

    // A sequence's length (SPEC 4.7.3) from its address: a fixed array's closed length, else the length word of an Array's or Slice's
    // handle record, its Field 1 (IMPL 21.1.3).
    private Value Length(BoundType sequence, ValueLowering record, Value start)
        => sequence.Kind == BoundTypeKind.FixedArray ? new('\0', sequence.Length)
            : this.LoadAt(this.Offset(start, record.Layout.FieldOffsets.Span[1]), Layout.Row(BoundType.ISize).Layout);

    // An address a constant number of bytes past another: the same address for none.
    private Value Offset(Value start, long bytes) => bytes == 0 ? start : this.Offset(start, new Value('\0', bytes));

    private Value Offset(Value start, Value bytes)
    {
        var address = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(address);
        this.output.Write(" = getelementptr i8, ptr ");
        this.WriteValue(start);
        this.output.Write(", i64 ");
        this.WriteValue(bytes);
        this.output.Write('\n');
        return address;
    }

    // The address of an Assign's Place, located after its rvalue (SPEC 13.7.1), or false: a local without storage, whose value is none
    // (I2), or a Place whose row is not written yet, recorded at the statement.
    private bool Destination(in MirStatement statement, out Value address)
    {
        address = default;
        if (this.Unstored(statement.Place))
        {
            return false;
        }

        if (this.Address(statement.Place, out address))
        {
            return true;
        }

        this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
        return false;
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

    // A Place's value in its representation (I4): loaded in its storage representation, or its address when the value is in memory; none
    // for a zero-sized one; false for a Place whose row is not written yet.
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
            value = InMemory(this.InstanceType(place)!, lowering) ? address : this.LoadAt(address, lowering.Layout);
        }

        return true;
    }

    // Whether a Place is a local without storage (zero-sized, I2).
    private bool Unstored(MirPlace place) => place is { Root: >= 0, Projection: 0 } && this.slots[place.Root].Home == Home.None;

    // The Type of a Place in the item's instance: a local's, or its projection's.
    private BoundType? InstanceType(MirPlace place)
        => place is { Root: >= 0, Projection: 0 } ? this.types[place.Root] : this.collector.TypeOf(this.item, this.body.TypeOf(place)!);

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

    private void WriteHome(int local)
    {
        var slot = this.slots[local];
        switch (slot.Home)
        {
            case Home.Slot:
                this.output.Write("%l");
                WriteNumber(this.output, local);
                return;
            case Home.Argument:
                this.output.Write("%a");
                WriteNumber(this.output, slot.Index);
                return;
            case Home.Result:
                this.output.Write("%ret");
                return;
            case Home.None:
                this.module.Request(ModuleCodegen.Needs.ZeroSized);
                this.output.Write('@');
                this.output.Write(ModuleCodegen.ZeroSized);
                return;
            default:
                throw new InvalidOperationException($"The home {slot.Home} has no spelling.");
        }
    }
}
