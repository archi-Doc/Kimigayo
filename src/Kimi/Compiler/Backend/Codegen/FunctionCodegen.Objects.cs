// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The handle operations of FunctionCodegen (IMPL 21.1.3, SPEC 13.6.1-13.6.4, 22.5.1; R5 plan C6a; DECISIONS R5-M74, R5-M110), the intrinsic
// rows whose writer is Create, Clone or Test. A creation (makeObj, makeRc, makeArc, and an Item of their function called or erased) calls
// its payload descriptor's factory, the target of its call (ModuleCodegen.Factory), with the static control value of the result handle's
// counting (ObjectRuntime.InitialControl: obj 0, rc and arc 2), and a clone the runtime's retain of the result handle's counting, which
// allocates nothing; both are routine rows (FunctionCodegen.Intrinsics' Routine), at their call's location (SPEC 22.5.1: an allocation or a
// count failure reports the invoking expression, through a function value that call). A runtime Type test compares the object of an object
// borrow, the payload's address less ObjectRuntime.PayloadOffset, with its target's number in the module (ModuleCodegen.Token of the
// target's identity in the item's instance); a target on no Generated descriptor's chain is false without a call: SPEC 13.6.4 admits a struct
// target only, which an object supports exactly when it is its Dynamic Type or a base on that Type's chain, and every runtime object comes
// from a Generated creation, whose chain is numbered. An object borrow is its payload's address, so its Deref is a borrow's
// (FunctionCodegen.Places) and an upcast keeps its value (FunctionCodegen.Casts).
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
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        this.module.Request(ModuleCodegen.Needs.Objects);
        this.Finish(call, this.WriteCall(ObjectRuntime.Supports, [this.Offset(borrow, -ObjectRuntime.PayloadOffset), new('\0', number)]));
    }
}
