// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The interrupt entries of FunctionCodegen (IMPL 21.6; design C-457 §6.4, DECISIONS T-M65): an InterruptEntry item's thunk, the
// x86_intrcc function of its entry, whose one call runs its handler, its one target, by code generation's one target rule (Witness: a
// body, or an import by its C signature, SPEC 22.6) through the one call writer, the frame (and the error code) as the handler's inputs;
// and an entry call's result, its thunk's code address (EntryAddress).
internal sealed partial class FunctionCodegen
{
    /// <summary>Writes an InterruptEntry item's thunk (IMPL 21.6), or records why not: its header (the frame by value at the processor's
    /// push, the error code, the realigned stack), then its handler's call through the one target rule and the one call writer.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The InterruptEntry item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteThunk(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next) = (collector, item, output, 0);
        var thunk = this.module.Thunk(item);
        if (collector.Set.Targets(item) is not [var handler])
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets the entry's handler (InstanceCollector.Expand).
            return;
        }

        if (this.Witness(handler.Item, default) is not { } run)
        {
            return; // Recorded.
        }

        if (run is not { Slot: null, Signature: { Result: "void", NoReturn: false, ResultSlot: false } callee } ||
            callee.Parameters.Length != thunk.Parameters.Length)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // handle's signature is its requirement's (SPEC 8.6.3; probe 1).
            return;
        }

        this.abi = thunk; // 'p' 0 spells %frame, 'p' 1 %code.
        this.WriteThunkHeader(thunk);
        output.Write("entry:\n");
        ReadOnlySpan<Value> inputs = [new('p', 0), new('p', 1)];
        if (this.CallWitness(run, inputs[..thunk.Parameters.Length], null, null, out var result))
        {
            this.End(callee, result);
        }
    }

    // IMPL 21.6 (design C-457 §6.4; probe 3): the thunk's header, `define internal x86_intrcc void @<name>(ptr byval(<frame>) %frame[,
    // i64 %code]) "stackrealign" #0 {`: the frame by value where the processor pushed it, its Type X86.InterruptFrame's storage (the
    // catalog's lang item, laid out by Layout), then the error code; the stack realigned to 16 bytes before any call.
    private void WriteThunkHeader(FunctionAbi thunk)
    {
        var (output, parameters) = (this.output, thunk.Parameters);
        output.Write("define internal x86_intrcc void @");
        output.Write(thunk.Name);
        output.Write("(ptr byval(");
        // X86.InterruptFrame is the catalog's non-generic C-layout struct of five u64 Fields, so its layout is always Ok.
        output.Write(this.module.Layout.Of(this.typeContext.SelfType(KimiDeclarationId.InterruptFrame)).Value!.Layout.StorageType);
        output.Write(") %");
        output.Write(parameters[0].Name);
        for (var i = 1; i < parameters.Length; i++)
        {
            output.Write(", ");
            output.Write(parameters[i].Type);
            output.Write(" %");
            output.Write(parameters[i].Name);
        }

        output.Write(") \"stackrealign\" #0 {\n");
    }

    // IMPL 21.6 (DECISIONS T-M65): X86.entry's and X86.errorCodeEntry's result, the code address of the call's target, its
    // InterruptEntry item, whose thunk the module defines (WriteThunk), as a usize. Any other target is a broken contract, recorded at the
    // call: the collector targets the entry item of the call's Type argument and variant (InstanceCollector.Edges' Entry).
    private void EntryAddress(in MirTerminator call, int target)
    {
        if (target >= 0 && this.collector.Set.Items[target].Key.Kind == MonoKind.InterruptEntry)
        {
            this.Finish(call, this.Convert("ptrtoint ", "ptr", new Value('e', target), "i64"));
            return;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
    }
}
