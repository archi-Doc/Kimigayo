// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The Array rows of FunctionCodegen written in place (SPEC 4.3, 4.9.7, 16.3.2; R5 plan C8d; DECISIONS R5-M129): an Array aggregate's storage
// (ArrayBuffer: the empty handle at its Place, for n elements one growth to n, the aggregate's target, and the length n), whose buffer
// the aggregate writer puts the elements in; destroyValues' counted loop over T's glue (PLAN G126); and an Array's glue level, the
// destroyArray instance its glue item targets, called with the glue's own parameters (the string row's form). A Dictionary's glue and a
// Dictionary from none are FunctionCodegen.Dictionaries' (DECISIONS R5-M124).
internal sealed partial class FunctionCodegen
{
    // SPEC 4.3.1, 4.9.5, 12.4.1 (DECISIONS R5-M129): an Array aggregate's storage at its handle: the handle's words zero, then for its n
    // elements one growth to n, the site's target, growArray's instance, and the length n in the handle's length word; its buffer, where the
    // aggregate writer puts each element, the handle for `[]`, which allocates nothing; null after a record. No element is observable before
    // all are put: the puts run no user code and cannot fail.
    private Value? ArrayBuffer(in SourceInfo source, Value handle, BoundType array, ValueLowering record, int count)
    {
        var word = Layout.Row(BoundType.ISize).Layout;
        foreach (var offset in record.Layout.FieldOffsets.Span)
        {
            this.StoreAt(this.Offset(handle, offset), word, new('\0', 0)); // The empty handle: null, 0, 0 (IMPL 21.1.3).
        }

        if (count == 0)
        {
            return handle;
        }

        var target = this.Target(new(this.at, this.position));
        if (!this.Location(source, out var site) || this.Laid(array.Components[0]) is not { } element || target < 0 ||
            this.module.Signature(target) is not { } grow)
        {
            return null; // Recorded.
        }

        _ = this.WriteCall(grow, [handle, new('\0', element.Layout.Stride), new('\0', count), site.Text, site.Length]);
        this.StoreAt(this.Offset(handle, record.Layout.FieldOffsets.Span[Layout.LengthWord(array)]), Layout.Row(BoundType.ISize).Layout, new('\0', count));
        return this.LoadAt(handle, RuntimeAbi.StringReference.Layout); // The buffer word, a pointer (IMPL 21.1.3).
    }

    // SPEC 4.9.7, 16.3.2, 4.9.8 (PLAN G126): destroyValues over its operands, the storage and the count: the values from the last, each by T's
    // glue, the call's target, in the one counted loop (Loop) after a test for no value. A T without a glue has no target and writes nothing,
    // so a cleanup-free destruction costs nothing at any optimization level.
    private void DestroyValues(in MirTerminator call, int target)
    {
        if (target >= 0)
        {
            var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
            if (this.Needed(call.Callee.Subst!, 0, out _) is not { } element ||
                !this.Operand(this.body.Operands[call.Start + ArgumentPosition(key, 0)], out var storage) ||
                !this.Operand(this.body.Operands[call.Start + ArgumentPosition(key, 1)], out var count) ||
                !this.Location(call.Source, out var site))
            {
                return; // A broken contract, recorded.
            }

            var (id, empty, bytes) = (this.next, new Value('t', this.next + 1), new Value('t', this.next + 2));
            this.next += 3;
            var output = this.output;
            output.Write("  ");
            this.WriteValue(empty);
            output.Write(" = icmp sle i64 ");
            this.WriteValue(count);
            output.Write(", 0\n  br i1 ");
            this.WriteValue(empty);
            output.Write(", label %undestroyed");
            WriteNumber(output, id);
            output.Write(", label %destroying");
            WriteNumber(output, id);
            output.Write("\ndestroying");
            WriteNumber(output, id);
            output.Write(":\n  ");
            this.WriteValue(bytes);
            output.Write(" = mul i64 ");
            this.WriteValue(count);
            output.Write(", ");
            WriteNumber(output, element.Layout.Stride);
            output.Write('\n');
            var last = this.Difference(bytes, new('\0', element.Layout.Stride));
            this.Loop(count, element, new Destroyed(target, storage, last, site));
            output.Write("  br label %undestroyed");
            WriteNumber(output, id);
            output.Write("\nundestroyed");
            WriteNumber(output, id);
            output.Write(":\n");
        }

        this.Finish(call, null);
    }

    // SPEC 4.9.7, 16.3.2: an Array's glue level at its address, its one target, the destroyArray instance, with the glue's own parameters.
    private void ArrayGlue(int glue, Value address, Site site)
    {
        if (this.collector.Set.Targets(glue) is not [var target])
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets the body an Array's destruction runs.
        }
        else if (this.module.Signature(target.Item) is { } destroy)
        {
            this.Destroy(destroy, address, site);
        }
    }
}
