// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Storage.reserveDispatch in FunctionCodegen (SPEC 22.9.1, 22.9.4, 22.10.1; DECISIONS R5-M130, R8-M18): the ReserveDispatch intrinsic's
// row (K package 4.5 row 6) is the code address of its target, the reserve witness of the call's Type argument, the word Text.writer's
// body stores in a user Writer's adapter and the kept runtime calls through. Text.writer, the buffers, the window, the adapter's write
// and Console.writeLine of a view are bodies (R8-W1a to W1c, R8-W2), so no compiler function's call is a formatting routine's.
internal sealed partial class FunctionCodegen
{
    // SPEC 22.9.4, 22.10.1 (K package 4.5 row 6; DECISIONS R5-M130): Storage.reserveDispatch's result, the function of its Body target, one
    // without a virtual slot that the kept runtime calls as its Writer's reserve (__kimi_erased_reserve's `void (ptr ret, ptr destination,
    // i64 amount)`). Any other target is a broken contract, recorded once (BodyRun's, else this one): the collector targets the reserve
    // witness of the call's Type argument, and Binding admits no virtual one (a virtual function's receiver is objref/Self or
    // objuniq/Self, which Binding rejects or leaves Unsupported there).
    private void Dispatch(in MirTerminator call, int target)
    {
        if (target >= 0 && this.collector.Set.Items[target].Key.Kind == MonoKind.Body)
        {
            if (this.BodyRun(target) is not { } run)
            {
                return; // Recorded.
            }

            if (run is { Slot: null, Signature: { Result: "void", NoReturn: false, ResultSlot: true } signature } &&
                signature.Parameters is [{ Kind: AbiParameterKind.ResultSlot }, { Type: "ptr" }, { Type: "i64" }])
            {
                this.Finish(call, new Value('e', target));
                return;
            }
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
    }
}
