// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The inline storage kernels of FunctionCodegen (SPEC 22.2; R5 plans P2a and C8b; DECISIONS R5-M45, R5-M116): a compiler function of the
// Body disposition whose legacy row names a kernel's writer, its text the P2a kernel row's (LegacyRuntimeHelpers.StorageRow), the one copy
// both generators write (PD2), over its input spelled from the call. FixedStorage: borrowStorage and borrowStorageUniq over a fixed array
// give the remainder {storage, position, count} = {the first element's address, 0, N} at the result record's Fields 0, 1 and 2 (the
// declaration order of RefRemainder and UniqRemainder), N the array's closed length in the item's instance; the reference operand's value
// is the array's address, so nothing is read or copied. The Dictionary kernels are C8e's rows.
internal sealed partial class FunctionCodegen
{
    private void FixedStorage(in MirTerminator call)
    {
        var source = this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)];
        if (this.Referent(this.InstanceType(source)) is not { } array || this.LoweringOf(call.Place) is not { } remainder)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Operand(source, out var storage) || !this.Address(call.Place, out var record))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        var input = new StorageKernelInput(
            this.Spelled(storage, stackalloc char[SpellingLength]),
            this.Spelled(record, stackalloc char[SpellingLength]),
            array.Length,
            remainder.Layout.FieldOffsets.Span[..3],
            this.next++);
        LegacyRuntimeHelpers.StorageRow(StorageKernelKind.BorrowFixed).Write(this.output, input);
        this.Finish(call, null);
    }
}
