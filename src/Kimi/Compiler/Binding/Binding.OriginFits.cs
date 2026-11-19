// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Origin fits (SPEC 15.3.5, 15.6.1, 15.6.5): the pass state of a fit's Origin part.
// - Responsibility: whether a recording fit held by a resting position (fitRested); an obligation's record location (RelationAt); the reset
//   of the pass's Origin relation records (ResetOriginRecords). A body's relations are rows A5 judges (FormBodyRelation; R7-O1), so no
//   position search or relation record of a fit lives here.
// - Inputs: the fit's walk (FitOrigin) and the obligations.
// - Outputs: the flag the fit's entries read; the location of a relation record Binding keeps (SPEC 5.7's Unstated rule).
// - Invariants: nothing here judges a relation or changes inference, obligation or declaration state.
// - Prohibitions: no message text (the diagnostics adapter writes it from the facts).
public sealed partial class Binding
{
    // SPEC 23.1.2 (PLAN G739): whether an Origin position of the recording fit being walked held by resting (FitOrigin), cleared at the
    // fit's entry (FitsTypeAt, CheckTypeUseCore), which forms the fit's rows from it (CheckTypeUse).
    private bool fitRested;

    // DIAGNOSTICS (Origin relations): an obligation's record is at its use, a local annotation's fit at the local's initializer, the
    // value that supplies the longer end, as a binding-time fit (RecordMismatch's `at`) and the old reporter record it.
    private static Koto RelationAt(in BindingObligation obligation)
        => IsFitObligation(obligation) && obligation.Use is VariableKoto { InitializerKoto: { } initializer } ? initializer : obligation.Use;

    // Resets the pass's Origin relation records at its start (ResetPrerequisites): each failed node's first record.
    private void ResetOriginRecords() => this.originRelations?.Clear();
}
