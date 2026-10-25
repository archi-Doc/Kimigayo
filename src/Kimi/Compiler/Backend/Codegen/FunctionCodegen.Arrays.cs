// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Array rows of FunctionCodegen written in place (SPEC 4.3, 4.9.5, 16.3.2, 22.2; R5 plan C8d; DECISIONS R5-M121), beside the operations
// and storage kinds whose call runs their LegacyRow item's routine (FunctionCodegen.Intrinsics' Routine): an Array from none, the runtime's
// empty handle (__kimi_array_init) at the aggregate's Place with its location; setArrayLength, the length stored in the handle's length word
// (Field 1, the word Length reads), reading or destroying no element; capacity, P2b's kernel over the handle, the legacy representation's
// capacity word of an Array or a Dictionary (PD2); and an Array's glue level, its glue item's Drop helper called with the glue's own
// parameters (the string row's form). A Dictionary's glue (FunctionCodegen.Glue) and a Dictionary from none (FunctionCodegen.Aggregates)
// are C8e's.
internal sealed partial class FunctionCodegen
{
    // SPEC 4.3, 4.9.5: `[]`, an aggregate of no operand whose Type is an Array, allocates nothing: the runtime's empty handle at the Place.
    private void EmptyArray(in MirStatement statement)
    {
        if (this.Location(statement.Source, out var site) && this.Destination(statement, out var address))
        {
            _ = this.WriteCall(RuntimeAbi.ArrayInit, [address, site.Text, site.Length]);
        }
    }

    // SPEC 22.2: setArrayLength(value, length) publishes the length in the handle its reference holds; it reads and destroys no element.
    private void ArrayLength(in MirTerminator call)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        var handle = this.body.Operands[call.Start + ArgumentPosition(key, 0)];
        var length = this.body.Operands[call.Start + ArgumentPosition(key, 1)];
        if (this.Referent(this.InstanceType(handle)) is not { } array || this.Laid(array) is not { } record || this.LoweringOf(length) is not { } isize)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Operand(handle, out var address) || !this.Operand(length, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        this.StoreAt(this.Offset(address, record.Layout.FieldOffsets.Span[1]), isize.Layout, value);
        this.Finish(call, null);
    }

    // SPEC 4.9.5: capacity, from the shared view of the handle its one operand holds, by P2b's kernel (the legacy handle's capacity word).
    private void Capacity(in MirTerminator call)
    {
        if (!this.Operand(this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)], out var handle))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        var capacity = new Value('t', this.next++);
        LegacyRuntimeHelpers.WriteCapacity(
            this.output, this.Spelled(handle, stackalloc char[SpellingLength]), this.Spelled(capacity, stackalloc char[SpellingLength]), this.next++);
        this.Finish(call, capacity);
    }

    // SPEC 4.9.7, 16.3.2: an Array's glue level at its address, its glue item's Drop helper (P1b's row, which destroys the elements from the
    // last by the element's glue, then releases the buffer) with the glue's own parameters.
    private void ArrayGlue(int glue, Value address, Site site)
    {
        if (this.module.ArrayDrop(glue) is { } drop)
        {
            this.Destroy(drop, address, site);
        }
    }
}
