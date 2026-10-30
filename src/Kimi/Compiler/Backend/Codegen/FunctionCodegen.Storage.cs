// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The inline storage kernels of FunctionCodegen (SPEC 22.2; R5 plans P2a, C8b and C8e; DECISIONS R5-M45, R5-M116, R5-M124): a compiler function
// of the Body disposition whose legacy row names a kernel's writer, its text the P2a kernel row's (LegacyRuntimeHelpers.StorageRow), the one
// copy both generators write (PD2), over its input spelled from the call. FixedStorage: borrowStorage and borrowStorageUniq over a fixed
// array give the remainder {storage, position, count} = {the first element's address, 0, N} at the result record's Fields 0, 1 and 2 (the
// declaration order of RefRemainder and UniqRemainder), N the array's closed length in the item's instance; the reference operand's value
// is the array's address, so nothing is read or copied. DictionaryKernel: each Dictionary kernel over its call's entry layout, of the K
// and V its substitution binds in the item's instance (its row's TypeArguments need, Needed; DECISIONS R5-M149), with its writer's kernel
// and number: dictionaryStorage's {address, stride} Tuple,
// borrowStorage's and borrowStorageUniq's remainder {storage, stride, link, count}, ownStorage's {storage, stride, link, tail, count} (each
// at the result record's Fields in declaration order) with the entry stride, and keyAt's and valueAt's slot address at the key's or the
// value's offset; another writer is a broken contract (LegacyRow.DictionaryKernel names exactly these five).
internal sealed partial class FunctionCodegen
{
    private void FixedStorage(in MirTerminator call)
    {
        var source = this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)];
        if (this.Referent(this.InstanceType(source)) is not { } array || this.LoweringOf(call.Place) is not { } remainder ||
            !this.Operand(source, out var storage) || !this.Address(call.Place, out var record))
        {
            return; // A broken contract, recorded.
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

    // SPEC 22.2 (DECISIONS R5-M124): a Dictionary storage kernel (LegacyRow.DictionaryKernel) over its call's entry layout
    // (DictionaryEntryLayout.Of of the K and V the call's substitution binds, in the item's instance; one rule for the five writers), its
    // source operand 0's value (a reference's Dictionary address, ownStorage's moved Dictionary in memory, a slot pointer), its result the
    // call Place's record, whose Fields the kernel stores, or, for a result that travels as a scalar (keyAt's and valueAt's pointer), the
    // value the kernel defines.
    private void DictionaryKernel(in MirTerminator call, LegacyWriter writer, Subst subst)
    {
        if (this.Needed(subst, 0, out _) is not { } key || this.Needed(subst, 1, out _) is not { } value || this.LoweringOf(call.Place) is not { } result)
        {
            return; // A broken contract, recorded.
        }

        var entry = DictionaryEntryLayout.Of(key.Layout, value.Layout);
        var (kernel, number) = writer switch
        {
            LegacyWriter.DictionaryLayout => (StorageKernelKind.DictionaryLayout, entry.Stride),
            LegacyWriter.DictionaryBorrow => (StorageKernelKind.BorrowDictionary, entry.Stride),
            LegacyWriter.DictionaryOwn => (StorageKernelKind.OwnDictionary, entry.Stride),
            LegacyWriter.KeyAt => (StorageKernelKind.EntryAddress, entry.KeyOffset),
            LegacyWriter.ValueAt => (StorageKernelKind.EntryAddress, entry.ValueOffset),
            _ => throw new InvalidOperationException($"The legacy writer {writer} is no Dictionary storage kernel."),
        };

        var scalar = StoredValue.TravelsAsScalar(result);
        var record = default(Value);
        var source = this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)];
        if (!this.Operand(source, out var storage) || !(scalar || this.Address(call.Place, out record)))
        {
            return; // A broken contract, recorded.
        }

        var defined = scalar ? new Value('t', this.next++) : default;
        var input = new StorageKernelInput(
            this.Spelled(storage, stackalloc char[SpellingLength]),
            this.Spelled(scalar ? defined : record, stackalloc char[SpellingLength]),
            number,
            scalar ? default : result.Layout.FieldOffsets.Span,
            this.next++);
        LegacyRuntimeHelpers.StorageRow(kernel).Write(this.output, input);
        this.Finish(call, scalar ? defined : null);
    }
}
