// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The place writer of FunctionCodegen (R5 plan C2a, C2b, package C 3.2 I4, I5, I7 and I9; DECISIONS R5-M83, R5-M87), the one owner of a
// Place's address. A Place is its root's home, a local's (a zero-sized local's home is the module's zero-sized symbol, which ModuleCodegen
// defines on request, IMPL 21.1.3) or a static's slot (FunctionCodegen.Statics' StaticAddress), then each step of its
// interned projection chain, root first, by one row per MirProjectionKind, each reading the layout its step needs (LayoutNeeds through
// Needed: a Field's and a Downcast's prefix's record, an Index's and a ConstIndex's element's stride; DECISIONS R5-M149) in the item's
// instance (IMPL 21.1): a Field at its Field offset; a Base at the derived struct's own address, its complete base being a prefix (X1); a ConstIndex at
// its position times the element's stride, which the validator proved below the length; a Downcast at the enum's payload offset; a Deref of
// a reference, raw pointer or object borrow the address it stores (IMPL 21.1.3; an object borrow's is its payload's, DECISIONS R5-M110),
// of an owning handle its object's payload; a PairLayer the
// Place itself in the owner case, else the referent of its case Type by the Deref row (MirCases.Layer under the item's view, SPEC 13.5.5);
// an Index its isize position checked against the length, a fixed array's closed length (its record unread) or an Array's or Slice's
// length word, unsigned, so
// a negative position fails, Aborting with KIMI_E_INDEX_BOUNDS (SPEC 4.7.4, 22.5.2) at its position local's location, which the builder
// locates at the selection (DECISIONS R4-M56, SPEC 22.5.1), then the element at the position times the stride from the fixed array's or the
// buffer word's address. Offsets are written without inbounds (I9). The loads, stores and copies through it are
// FunctionCodegen.Stores.cs.
internal sealed partial class FunctionCodegen
{
    // A Place's address (the rows above): false after a broken contract's record (Referent: a Deref or PairLayer step to no pointer, which
    // validated MIR never has; DECISIONS R5-M146), true without an address after another record.
    private bool Address(MirPlace place, out Value address)
    {
        address = default;
        if (place.Root < 0)
        {
            address = this.StaticAddress(~place.Root);
            return true;
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
                address = this.Part(place, start, row.Arg);
                return true;
            case MirProjectionKind.Base:
                return true;
            case MirProjectionKind.ConstIndex:
                if (this.Needed(place) is { } element)
                {
                    address = this.Offset(start, (long)row.Arg * element.Layout.Stride);
                }

                return true;
            case MirProjectionKind.Downcast:
                address = this.Part(place, start, 1); // The payload, after the i32 tag.
                return true;
            case MirProjectionKind.Index:
                return this.Element(place, prefix, start, out address);
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

    // A step's part at its prefix's record's offset of the index (its Parts need, Needed), a Field's or an enum's payload; the prefix's
    // address after a record.
    private Value Part(MirPlace step, Value start, int index)
        => this.Needed(step) is { } whole ? this.Offset(start, whole.Layout.FieldOffsets.Span[index]) : start;

    // A Deref by its Type (IMPL 21.1.3; DECISIONS R5-M110): one stored pointer, a reference's, a raw pointer's or an object borrow's the
    // address of its referent, an object borrow's its object's payload; an owning handle's its object's header, the payload
    // ObjectRuntime.PayloadOffset bytes after it. Any other Type is a broken contract (MirBody.ProjectedType admits a Deref only below a
    // reference, handle or raw pointer, and TypeOf keeps the layer), recorded at the body's subject; false after it.
    private bool Referent(BoundType pointer, Value start, out Value address)
    {
        address = start;
        if (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner })
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return false;
        }

        address = this.LoadAt(start, PointerLayout);
        if (pointer.HandleMode is not null)
        {
            address = this.Offset(address, ObjectRuntime.PayloadOffset);
        }

        return true;
    }

    // An Index: the bounds check at the position local's location, then the element's address at its stride (its Element need, Needed); a
    // handle's record gives its length word, a fixed array's length is its Type's, so its record is not read.
    private bool Element(MirPlace step, MirPlace prefix, Value start, out Value address)
    {
        address = start;
        var position = this.body.Projections[step.Projection].Arg;
        var type = this.InstanceType(prefix)!;
        var sequence = type.Kind == BoundTypeKind.FixedArray ? null : this.LoweringOf(prefix);
        if (!this.Location(this.body.Locals[position].Source, out var site) || this.Needed(step) is not { } element ||
            (sequence is null && type.Kind != BoundTypeKind.FixedArray))
        {
            return true; // Recorded.
        }

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
        LlvmScalarText.WriteFailure(output, "%invalid", AbortReasons.IndexBounds, this.Failure(id, site, stackalloc char[3 * SpellingLength]));
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

    // A sequence's length (SPEC 4.7.3) from its address: a fixed array's closed length, its record unread (null), else the length word of its
    // handle record, which its handle row names (Layout.LengthWord: an Array's or Slice's Field 1, a Dictionary's live count; IMPL 21.1.3).
    private Value Length(BoundType sequence, ValueLowering? record, Value start)
        => sequence.Kind == BoundTypeKind.FixedArray ? new('\0', sequence.Length)
            : this.LoadAt(this.Offset(start, record!.Layout.FieldOffsets.Span[Layout.LengthWord(sequence)]), Layout.Row(BoundType.ISize).Layout);

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
}
