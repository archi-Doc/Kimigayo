// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Array rows of FunctionCodegen written in place (SPEC 4.3, 4.9.5, 16.3.2, 22.2; R5 plan C8d; DECISIONS R5-M121, R5-M129), beside the
// operations and storage kinds whose call runs their LegacyRow item's routine (FunctionCodegen.Intrinsics' Routine): an Array aggregate's
// storage (ArrayBuffer: the runtime's empty handle at its Place with its location, for n elements one growth to n and the length n), whose
// buffer the aggregate writer puts the elements in; capacity, P2b's kernel over the handle, the legacy representation's
// capacity word of an Array or a Dictionary (PD2); and an Array's glue level, its glue item's Drop helper called with the glue's own
// parameters (the string row's form). A Dictionary's glue and a Dictionary from none are FunctionCodegen.Dictionaries' (DECISIONS R5-M124).
internal sealed partial class FunctionCodegen
{
    // SPEC 4.3.1, 4.9.5, 12.4.1 (DECISIONS R5-M129): an Array aggregate's storage at its handle: the runtime's empty handle, then for its n
    // elements one growth to n (the Array runtime's grow, as the old generator called it) and the length n in the handle's length word; its
    // buffer, where the aggregate writer puts each element, the handle for `[]`, which allocates nothing; null after a record. No element is
    // observable before all are put: the puts run no user code and cannot fail.
    private Value? ArrayBuffer(in SourceInfo source, Value handle, BoundType array, ValueLowering record, int count)
    {
        if (!this.Location(source, out var site))
        {
            return null; // Recorded.
        }

        _ = this.WriteCall(RuntimeAbi.ArrayInit, [handle, site.Text, site.Length]);
        if (count == 0)
        {
            return handle;
        }

        if (this.Laid(array.Components[0]) is not { } element)
        {
            return null; // Recorded.
        }

        this.module.Request(HelperNeeds.ArrayRuntime);
        _ = this.WriteCall(RuntimeAbi.ArrayGrow, [handle, new('\0', element.Layout.Stride), new('\0', count), site.Text, site.Length]);
        this.StoreAt(this.Offset(handle, record.Layout.FieldOffsets.Span[Layout.LengthWord(array)]), Layout.Row(BoundType.ISize).Layout, new('\0', count));
        return this.LoadAt(handle, RuntimeAbi.StringReference.Layout); // The buffer word, a pointer (IMPL 21.1.3).
    }

    // SPEC 4.9.5: capacity, from the shared view of the handle its one operand holds, by P2b's kernel (the legacy handle's capacity word).
    private void Capacity(in MirTerminator call)
    {
        if (!this.Operand(this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)], out var handle))
        {
            return; // A broken contract, recorded.
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
