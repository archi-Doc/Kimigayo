// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The handle operations of FunctionCodegen (IMPL 21.1.3, SPEC 13.6.1-13.6.4, 16.3.3, 22.5.1; R5 plans C6a, C6b; DECISIONS R5-M74, R5-M110,
// R5-M112), the intrinsic rows whose writer is Create, Clone or Test, and an owning handle's release, its glue row (Release). A creation
// (makeObj, makeRc, makeArc, an Item of their function called or erased, and a case adaptation whose case creates, Creation) calls its
// payload descriptor's factory, the target of its site (ModuleCodegen.Factory), with the static control value of the result handle's
// counting (ObjectRuntime.InitialControl: obj 0, rc and arc 2), and a clone the runtime's retain of the result handle's counting, which
// allocates nothing; both are routine rows (FunctionCodegen.Intrinsics' Routine), at their call's location (SPEC 22.5.1: an allocation or a
// count failure reports the invoking expression, through a function value that call). A runtime Type test compares the object of an object
// borrow, its header (Header), with its target's number in the module (ModuleCodegen.Token of the target's identity in the item's
// instance); a target on no Generated descriptor's chain is false without a call: SPEC 13.6.4 admits a struct target only, which an
// object supports exactly when it is its Dynamic Type or a base on that Type's chain, every runtime object comes from a Generated
// creation, whose chain is numbered, and the tokens are module-local (IMPL 21.1.3). An object borrow is its payload's address, so its
// Deref is a borrow's (FunctionCodegen.Places) and an upcast keeps its value (FunctionCodegen.Casts). A call through a virtual slot
// (SPEC 6.4.3; DECISIONS R5-M114) calls the slot's entry in the virtual table of its receiver's object: the header, the descriptor
// there, its table at ObjectRuntime.TableOffset (EntryOf), the one callee value the slot makes.
internal sealed partial class FunctionCodegen
{
    // SPEC 13.6.2: a creation's routine (above) and its slot-less value; null, with InternalInvariant_Kd, for a target that is no descriptor
    // or a result that is no owning handle.
    private FunctionAbi? Create(BoundType? result, int target, out ReadOnlySpan<Value> given)
    {
        given = default;
        if (target < 0 || this.collector.Set.Items[target].Key.Kind != MonoKind.ObjectDescriptor || result?.HandleMode is not { } mode)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A creation targets its payload's descriptor (K package 4.5 row 8).
            return null;
        }

        this.slotless[0] = new('\0', ObjectRuntime.InitialControl(mode.Counting));
        given = this.slotless.AsSpan(0, 1);
        return this.module.Factory(target);
    }

    // SPEC 13.6.2, 8.14 (DECISIONS R5-M111): a case adaptation that creates an object (MirCases.Adapt's ObjectCreation, its rvalue the Use
    // of its operand) calls the factory of the statement's target descriptor as a creation call does: its Place the result, the operand at
    // the payload's kind, the control value of the Place's handle and the statement's location, in the factory's parameter order.
    private void Creation(in MirStatement statement, in MirRvalue use)
    {
        var factory = this.Create(this.InstanceType(statement.Place), this.Target(new(this.at, this.position)), out var given);
        if (factory is null || !this.Location(statement.Source, out var site) || !this.Destination(statement, out var result))
        {
            return; // Recorded.
        }

        var parameters = factory.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        for (var i = 0; i < parameters.Length; i++)
        {
            var kind = parameters[i].Kind;
            if (kind is AbiParameterKind.Value or AbiParameterKind.OwnedSlot or AbiParameterKind.SharedReference)
            {
                if (!this.Argument(this.body.Operands[use.Start], -1, kind, out this.arguments[i]))
                {
                    return; // A broken contract, recorded.
                }

                continue;
            }

            this.arguments[i] = kind switch
            {
                AbiParameterKind.ResultSlot => result,
                AbiParameterKind.Context => given[0],
                AbiParameterKind.Location => site.Text,
                _ => site.Length,
            };
        }

        _ = this.WriteCall(factory, this.arguments.AsSpan(0, parameters.Length));
    }

    // SPEC 13.6.2: a clone's routine (above); null, with InternalInvariant_Kd, for a result that is no counted handle (Binding admits a clone
    // of rc and arc only).
    private FunctionAbi? Retain(BoundType? result)
    {
        if (result?.HandleMode is not { Counting: not ObjectCountingStep.None and var counting })
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return null;
        }

        this.module.Request(ModuleCodegen.Needs.Objects);
        return ObjectRuntime.Clone(counting);
    }

    // SPEC 16.3.3, SHARED_OBJECT_RUNTIME, Release (DECISIONS R5-M112): an owning handle's release, its glue function's one level (its
    // parameters the drop routine's, slot, location and length, which the release text names): a Sealed payload's (DropGlue.Parts gives it)
    // directly, ObjectRuntime.WriteSealedDrop's counted transition, the payload's glue (the glue item's one target, when the payload may need
    // a drop) and the free; any other through the runtime's release of its counting, whose descriptor's finalizer destroys the actual Type.
    private void Release(int glue, BoundType type, Value address, Site site)
    {
        var (parts, start) = (this.parts, this.parts.Count);
        DropGlue.Parts(this.typeContext, type, parts);
        var direct = parts.Count > start;
        parts.RemoveRange(start, parts.Count - start);
        var targets = this.collector.Set.Targets(glue);
        if (targets.Length > (direct ? 1 : 0) || type.HandleMode is not { } mode)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets a Sealed payload's glue only (K7).
            return;
        }

        this.module.Request(ModuleCodegen.Needs.Objects);
        if (!direct)
        {
            this.Destroy(ObjectRuntime.Release(mode.Counting), address, site);
            return;
        }

        ObjectRuntime.WriteSealedDrop(this.output, mode.Counting, targets.Length == 0 ? default : this.module.Glue(targets[0].Item).Name);
    }

    // IMPL 21.1.3 (DECISIONS R5-M110, ruling Q2): the header of the object an object borrow borrows, the payload's address less
    // ObjectRuntime.PayloadOffset; the one rule of a slot's descriptor load (EntryOf) and a runtime Type test (Test).
    private Value Header(Value borrow) => this.Offset(borrow, -ObjectRuntime.PayloadOffset);

    // SPEC 6.4.3, IMPL 21.1.3 (above): the callee value a call writes, the one thing the slot it runs through decides: none, its routine's
    // symbol, without a slot; for the slot a function runs (VTableDef.SlotOf, the same in every table of its family), that slot's entry in
    // the virtual table of the object its receiver argument borrows (the routine's parameter at the function's receiver slot). Null, with
    // InternalInvariant_Kd, for a function that runs no slot or a routine without that parameter.
    private Value? EntryOf(FunctionAbi routine, BindingSymbol? slot)
    {
        if (slot is null)
        {
            return null;
        }

        var index = VTableDef.SlotOf(slot);
        var receiver = -1;
        for (var i = 0; i < routine.Parameters.Length; i++)
        {
            receiver = routine.Parameters[i] is { Kind: AbiParameterKind.Value } parameter && parameter.LogicalIndex == slot.ReceiverIndex ? i : receiver;
        }

        if (index < 0 || receiver < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A slot's call passes its receiver, an object borrow (SPEC 6.4.1).
            return null;
        }

        var descriptor = this.LoadAt(this.Header(this.arguments[receiver]), PointerLayout);
        var table = this.LoadAt(this.Offset(descriptor, ObjectRuntime.TableOffset), PointerLayout);
        return this.LoadAt(this.Offset(table, (long)index * PointerLayout.Size), PointerLayout);
    }

    // SPEC 13.6.4: a runtime Type test (above) of its operand, an object borrow, against its TypeToken constant's target, its value stored
    // into the call's destination; the operand was evaluated by its own statement, and the test acquires nothing.
    private void Test(in MirTerminator call)
    {
        var token = this.body.Operands[call.Start + 1];
        if (token.Kind != MirOperandKind.Const || this.collector.TypeOf(this.item, this.body.Consts[token.Arg].Type) is not { } target)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // The builder's test names its target by a token (MirBuilder.TypeTest).
            return;
        }

        var number = this.module.Token(target);
        if (number < 0)
        {
            this.Finish(call, new('\0', 0));
            return;
        }

        if (!this.Operand(this.body.Operands[call.Start], out var borrow))
        {
            return; // A broken contract, recorded.
        }

        this.module.Request(ModuleCodegen.Needs.Objects);
        this.Finish(call, this.WriteCall(ObjectRuntime.Supports, [this.Header(borrow), new('\0', number)]));
    }
}
