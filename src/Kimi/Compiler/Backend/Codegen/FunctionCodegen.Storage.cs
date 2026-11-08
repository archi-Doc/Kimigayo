// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The inline storage kernels of FunctionCodegen (SPEC 22.2; R5 plans P2a, C8b and C8e; DECISIONS R5-M45, R5-M116, R5-M124): a compiler function
// of the Body disposition whose legacy row names a kernel's writer, its text the P2a kernel row's (LegacyRuntimeHelpers.StorageRow), the one
// copy (PD2), over its input spelled from the call. DictionaryKernel: dictionaryStorage's {address, stride} Tuple over its call's entry
// layout, of the K and V its substitution binds in the item's instance (its row's TypeArguments need, Needed; DECISIONS R5-M149), the one
// writer LegacyRow.DictionaryKernel names. The remainders (R8-K1a), keyAt and valueAt (R8-K1b) are
// bodies.
internal sealed partial class FunctionCodegen
{
    // SPEC 22.2 (DECISIONS R5-M124): dictionaryStorage's kernel (LegacyRow.DictionaryKernel) over its call's entry stride
    // (DictionaryEntryLayout.Of of the K and V the call's substitution binds, in the item's instance), its source operand 0's value (a
    // reference's Dictionary address), its result the call Place's record, whose Fields the kernel stores.
    private void DictionaryKernel(in MirTerminator call, Subst subst)
    {
        if (this.Needed(subst, 0, out _) is not { } key || this.Needed(subst, 1, out _) is not { } value)
        {
            return; // A broken contract, recorded.
        }

        var source = this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)];
        if (!this.Operand(source, out var storage) || !this.Address(call.Place, out var record))
        {
            return; // A broken contract, recorded.
        }

        var input = new StorageKernelInput(
            this.Spelled(storage, stackalloc char[SpellingLength]),
            this.Spelled(record, stackalloc char[SpellingLength]),
            DictionaryEntryLayout.Of(key.Layout, value.Layout).Stride,
            this.next++);
        LegacyRuntimeHelpers.StorageRow(StorageKernelKind.DictionaryLayout).Write(this.output, input);
        this.Finish(call, null);
    }
}
