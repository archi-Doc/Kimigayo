// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Testing;

namespace Kimi.Compiler.Backend;

// The test phases of FunctionCodegen (R5 plan C10b; PD22; DECISIONS R5-M137, R5-M140), the phase every frame of a test build's runtime
// reports (TestRuntime.Phase). In a test build (ModuleCodegen.IsTestBuild) the code of a cleanup position runs in the cleanup phase: a
// Drop whose A2 row destroys (FunctionCodegen.Drops' DestroyPlace) and an Assign or Call in a Deferred scope (SPEC 16.1: a defer body
// runs in Scope Exit; MirBody.Enclosing). One window per run of a block's code: the first cleanup position saves the runtime's phase and
// sets cleanup (TestRuntime.WritePhase); the next Assign or Call outside a defer body, or the block's transfer (Completed, Return),
// restores it (TestRuntime.RestorePhase); an end without a runtime successor restores nothing, since the process ends there. A window
// never spans blocks, so its save dominates its restore. A callee keeps its caller's phase, and its own windows nest (save, set, restore).
// A slot's ensure function runs its StaticInit call in the initialization phase (FunctionCodegen.Statics' WriteStorage).
// Must not decide which code is cleanup from syntax or MIR shape (A2's verdicts and the MIR's scope kinds decide it), or write a phase
// outside a test build, whose runtime has none.
internal sealed partial class FunctionCodegen
{
    private int window = -1; // The open window's saved phase, the value %t<window>, or -1.

    // A position's code (SPEC 16.1): in the block's cleanup window when it lies in a defer body, else outside any window.
    private void Position(in SourceInfo source)
    {
        if (this.module.IsTestBuild && this.body.Enclosing(source.Scope, MirScopeKind.Deferred) >= 0)
        {
            this.Open(TestRuntime.Phase.Cleanup);
            return;
        }

        this.Close();
    }

    // Opens a window of a phase unless one is open: the runtime's phase saved, then the phase set; nothing outside a test build.
    private void Open(TestRuntime.Phase phase)
    {
        if (this.window >= 0 || !this.module.IsTestBuild)
        {
            return;
        }

        this.window = this.next++;
        TestRuntime.WritePhase(this.output, "%t", this.window, phase);
    }

    // Closes the open window, if any: the saved phase restored.
    private void Close()
    {
        if (this.window < 0)
        {
            return;
        }

        TestRuntime.RestorePhase(this.output, "%t", this.window);
        this.window = -1;
    }

    // A terminator's location complete before its transfer to a runtime successor: its flag points (FunctionCodegen.Flags), then the
    // window closed.
    private void Completed()
    {
        this.Points(new(this.at, this.body.Blocks[this.at].Count));
        this.Close();
    }
}
