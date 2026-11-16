// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The pass driver (PLAN G949 S1-r1a; DECISIONS BU-220): a Bind's runs and, in each run, its passes in order.
// - Responsibility: the three levels of a Bind's state: per Bind (BeginBind: the SyntaxIds, the Result discarded with the later
//   phases' facts, StartRuns), per run (BeginRun: the counter every stamp compares, the work counters) and per pass (ResetPass); the
//   runs, a new one whenever a pass asks for it (VerifyEarlyReads); each pass's steps in order.
// - Inputs: the compilation's modules, the mode. Outputs: the pass summary (Result).
// - Invariants: one counter value per run; a restart starts a new run of every pass, never a new Bind (StartRuns runs once per Bind,
//   so a second run cannot ask again and the runs end); until PLAN G949 S1-r1b one pass binds every module.
// - Prohibitions: no step body here; no flag or mode steering a step; no second copy of a step for the library.
public sealed partial class Binding
{
    // Per Bind: the SyntaxIds, the Result and the later phases' facts discarded, the runs started.
    private void BeginBind(BindingMode mode)
    {
        // SyntaxIds: each module is renumbered only when parsing or an edit changed its syntax since it was last numbered. Its
        // ordinal is its position; the library's is the fixed SyntaxTable.LibraryModule.
        var modules = this.compilation.SourceModules;
        for (var i = 0; i < modules.Length; i++)
        {
            modules[i].Syntax.Number(modules[i].RootKoto, (ushort)i);
        }

        this.Library.Kotonoha.Syntax.Number(this.Library.Kotonoha.RootKoto, SyntaxTable.LibraryModule);

        // Every later phase rests on Binding, so its facts are discarded with Binding's; a pass that does not finish
        // leaves no result behind.
        this.Result = default;
        this.compilation.Diagnostics.InvalidateSemantics();
        this.compilation.InvalidateMir();
        this.StartRuns(mode);
    }

    // Per run, a restart's included, and per edit (Invalidate): a new counter value, so no stamp of an earlier run reads current, and
    // the work counters, which the scaling checks read as the last run's work.
    private void BeginRun()
    {
        this.storageVersion++;
        this.signatureWalks = 0;
        this.memberWalks = 0;
        this.termWalks = 0;
        this.baseSteps = 0;
        this.inheritedSteps = 0;
        this.inheritedPremiseSteps = 0;
        this.sourceChecks = 0;
        this.ResetInferenceMetrics();
    }

    // The runs: each runs every pass in order; a pass whose declarations ask for a rerun ends its run, and the next run starts.
    private void Run(BindingMode mode)
    {
        do
        {
            this.BeginRun();
        }
        while (!this.RunPass(mode));
    }

    // One pass: its reset, indexing, declarations, bodies, the bound-declaration validations and Check. False: its declarations asked
    // for a rerun (VerifyEarlyReads).
    private bool RunPass(BindingMode mode)
    {
        this.ResetPass(mode);
        this.IndexSources();
        this.IndexLibrary();
        if (!this.BindDeclarations(mode))
        {
            return false;
        }

        this.BindBodies();
        this.ValidateBoundDeclarations(mode);
        this.Result = this.Check(mode);
        return true;
    }
}
