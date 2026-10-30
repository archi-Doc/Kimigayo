// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The whole-value updates of FunctionCodegen (SPEC 15.7, 15.7.1-15.7.3; R5 plan C8b; DECISIONS R5-M116): replace, exchange and swap, the
// compiler functions of the Body disposition whose legacy row names their writer (LegacyRuntimeHelpers.Row; R8 writes them in Kimigayo),
// written through the place writer and the one destruction writer (PD3) in the caller's body, at a direct call and at an Item's Value call
// over the call's operands (SPEC 7.7.4: an Item's call is its direct call's row), and in an update Item's adapter entry over the entry's
// own parameters (FunctionCodegen.Erasures' WriteAdapter); both take the same steps (DestroyOld, Keep, SwapValues). The target is the
// address the first input's reference holds, T the update's Type argument in the item's instance (its row's TypeArguments need, Needed;
// DECISIONS R5-M149). Replace destroys the old value by T's glue, the
// site's target (the collector's, from Binding's published in-place row), at the call's location (SPEC 22.5.1; an entry's or a
// caller-location body's received one), then puts the new value (a string constant written whole); exchange keeps the old value at its
// result (a call's destination, a fresh temp, DECISIONS R5-M89; an entry's result slot, or the value it returns), then puts the new one at
// the target; swap holds the first value (a value in memory in a slot of the entry block: a call's held slot, FunctionCodegen.Arguments),
// puts the second at the first and the held one at the second, the targets disjoint (SPEC 15.7.2). Exchange and swap run no user code and
// report nothing; a zero-sized T has no bytes, its replace still destroying. A missing glue is a broken contract.
internal sealed partial class FunctionCodegen
{
    // An entry parameter of an input of a Type at a logical index, by the kind the internal convention gives it (Abi.Of), for Own.
    private static AbiParameter Input(BoundType type, int logical) => new(string.Empty, string.Empty, Abi.Kind(Abi.Convention.Internal, type), logical);

    // A call's update by its row's writer under the call's substitution, with the target its call site carries (-1: none).
    private void Update(in MirTerminator call, LegacyWriter writer, int glue, Subst subst)
    {
        var first = this.body.Operands[call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0)];
        if (this.Needed(subst, 0, out var type) is not { } lowering || !this.Operand(first, out var target))
        {
            return; // A broken contract, recorded.
        }

        if (writer == LegacyWriter.Replace && !this.DestroyOld(type!, target, glue, call.Source))
        {
            return; // Recorded.
        }

        if (lowering.Layout.Size != 0 && !this.Updated(call, writer, type!, lowering, target))
        {
            return; // A broken contract, recorded.
        }

        this.Finish(call, null);
    }

    // A call's steps after replace's destruction for a T of nonzero size, over its second operand: swap's exchange through the call's held
    // slot; exchange's old value kept at its destination, then, as replace's, the new value put. False after a broken contract's record (an
    // operand's or the destination's).
    private bool Updated(in MirTerminator call, LegacyWriter writer, BoundType type, ValueLowering lowering, Value target)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        var second = this.body.Operands[call.Start + ArgumentPosition(key, 1)];
        if (writer == LegacyWriter.Swap)
        {
            if (!this.Operand(second, out var other))
            {
                return false;
            }

            this.SwapValues(type, lowering, target, other, new('s', this.copyOf[call.Start + ArgumentPosition(key, 0)]));
            return true;
        }

        if (writer == LegacyWriter.Exchange)
        {
            if (!this.Address(call.Place, out var destination))
            {
                return false;
            }

            _ = this.Keep(type, lowering, target, destination);
        }

        return this.Put(target, type, lowering, second);
    }

    // SPEC 7.7.4 (ruling Q3 (a)): an update Item's adapter entry, the update's steps over the entry's own parameters, those of its target
    // Function Type (its inputs' Tuple, then its result), each of its input's kind and logical index (Own, Abi.Kind): the target its first
    // input's pointer, the incoming value its second input in its storage form (a Value widened, an OwnedSlot's value at its address),
    // swap's second target that input's pointer, exchange's old value kept in the result slot or returned, swap's held value in a slot of the
    // entry block. Its table has no drop entry: a function Item has no environment.
    private void WriteUpdateEntry(int item, FunctionAbi entry, LegacyWriter writer, int glue)
    {
        var inputs = this.collector.Set.Items[item].Key.Function!.Components[0].Components;
        if (this.typeContext.SubstOfItem(this.collector.Set.Items[item].RepresentativeType!) is not { } subst)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // An update Item instantiates its function.
            return;
        }

        if (this.Needed(subst, 0, out var type) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        this.Table(item, entry, null);
        this.abi = entry;
        this.output.Write(entry.GetDefinition(exported: false));
        this.output.Write("entry:\n");
        var first = this.Own(Input(inputs[0], 0));
        var second = lowering.Layout.Size == 0 ? -2 : this.Own(Input(inputs[1], 1));
        if (first < 0 || second == -1)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The entry's parameters are its target Function Type's (ModuleCodegen.Adapter).
            return;
        }

        var target = new Value('p', first);
        if (writer == LegacyWriter.Replace && !this.DestroyOld(type!, target, glue, default))
        {
            return; // Recorded.
        }

        this.End(entry, second < 0 ? null : this.Updated(writer, type!, lowering, target, second));
    }

    // An entry's steps after replace's destruction for a T of nonzero size, over its parameter `second`: swap's exchange through a slot of the
    // entry block; exchange's old value kept in the result slot, or returned in its computation form; then the new value put.
    private Value? Updated(LegacyWriter writer, BoundType type, ValueLowering lowering, Value target, int second)
    {
        if (writer == LegacyWriter.Swap)
        {
            var slot = default(Value);
            if (InMemory(type, lowering))
            {
                slot = new('t', this.next++);
                this.WriteSlot("  %t", (int)slot.Number, lowering.Layout);
            }

            this.SwapValues(type, lowering, target, new('p', second), slot);
            return null;
        }

        var incoming = this.abi.Parameters[second].Kind == AbiParameterKind.OwnedSlot
            ? this.ValueAt(new('p', second), type, lowering) : this.Widen(lowering, new('p', second));
        var resultSlot = this.Own(new AbiParameter(string.Empty, string.Empty, AbiParameterKind.ResultSlot));
        var result = this.abi.ResultSlot ? new Value('p', resultSlot) : default(Value?);
        var old = writer == LegacyWriter.Exchange ? this.Keep(type, lowering, target, result) : null;
        this.Put(target, type, lowering, incoming);
        return old is { } value ? this.Narrow(lowering, value) : null;
    }

    // The Type below the reference layer of a Type in the item's instance (TyCtxt.Layer, as the collector reads it); null, with
    // InternalInvariant_Kd at the body, for a Type with no reference layer.
    private BoundType? Referent(BoundType? reference)
    {
        if (TyCtxt.Layer(reference, out var referent, out _) == HirPlaceKind.Deref)
        {
            return referent;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The validator checked the input against the declaration.
        return null;
    }

    // SPEC 15.7.1, 16.3.2: replace's old value destroyed in place by T's glue, the site's target, when T may need a drop, at the site's
    // location. False after a record.
    private bool DestroyOld(BoundType type, Value target, int glue, in SourceInfo source)
    {
        if (!MirTypes.MayNeedDrop(type, this.seen))
        {
            return true;
        }

        if (glue < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // The collector targets the glue of the Type replace destroys.
            return false;
        }

        if (!this.Location(source, out var site))
        {
            return false;
        }

        this.Destroy(this.module.Glue(glue), target, site);
        return true;
    }

    // SPEC 15.7.1: exchange's old value kept before the target changes, put at its result's address, or without one returned as loaded.
    private Value? Keep(BoundType type, ValueLowering lowering, Value target, Value? result)
    {
        var kept = this.ValueAt(target, type, lowering);
        if (result is not { } address)
        {
            return kept;
        }

        this.Put(address, type, lowering, kept);
        return null;
    }

    // SPEC 15.7, 15.7.2: swap's two values exchanged: the first held (a value in memory copied into the slot), the second put at the first,
    // the held one at the second.
    private void SwapValues(BoundType type, ValueLowering lowering, Value first, Value second, Value slot)
    {
        var held = this.ValueAt(first, type, lowering);
        if (InMemory(type, lowering))
        {
            this.Put(slot, type, lowering, held);
            held = slot;
        }

        this.Put(first, type, lowering, this.ValueAt(second, type, lowering));
        this.Put(second, type, lowering, held);
    }
}
