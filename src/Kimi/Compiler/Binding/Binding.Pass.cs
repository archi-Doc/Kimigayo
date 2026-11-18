// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The pass reset (ResetPass), run when a pass starts and when an edit revokes the latest one (Invalidate), after its run's BeginRun.
// - Responsibility: every per-pass fact discarded, through the tables cleared here and the owning concerns' Reset* steps.
// - Inputs: the mode (read by ResetCapabilities) and the running pass (its modules). Outputs: empty per-pass tables, reset scopes
//   and symbols.
// - Invariants: the cross-pass storage ResetPass's comment lists stays. A kept family's rows are reset in place over the running
//   pass's modules only (In, PLAN G949 S1-r2), so no pass resets another pass's rows. R7-D2 b replaces the Clears with
//   generation-stamped tables.
// - Prohibitions: no fact is computed or recorded here; ResetPass only empties tables and resets state.
public sealed partial class Binding
{
    // Discards every fact of the latest pass. Cross-pass storage stays: interned Types, Origins and Constraints, scopes and
    // symbols (reset here, removed by the prune steps), synthesized nodes, pooled plans and scratch buffers.
    private void ResetPass(BindingMode mode)
    {
        this.inferredConstructionTargets.Clear();
        this.callInferenceFailures?.Clear();
        this.clausesBindable = false;
        this.verifiedClause = null;
        this.earlyReads?.Clear();
        this.earlyObjectForms?.Clear();
        this.earlyProjections.Clear();
        this.storageMarks.Clear();
        this.storageStack.Clear();
        this.issues.Clear();
        this.sitelessHeightRefusal = false;
        this.lints.Clear();
        this.callableSelectionFailures.Clear();
        this.callableSelectionParties.Clear();
        this.importDeclarations.Clear();
        this.libraryImports.Clear();
        this.constraintDiagnosticCauses?.Clear();
        this.ResetPrerequisites();
        this.objectPayloadCauses?.Clear();
        this.ResetMatches();
        this.argumentParts.Clear();
        this.argumentProbe = null;
        this.resultContexts.Clear();
        this.resultLocalEvidence.Clear();
        this.resultCursor = 0;
        this.ResetStartup();
        this.ResetSpecializations();
        this.ResetVirtualDeclarations();
        this.receiverOperations.Clear();
        this.inferredArrayLengths.Clear();
        this.arrayInferenceShapes.Clear();
        this.arrayInferenceFailures?.Clear();
        this.noInitFailures?.Clear();
        this.ResetSyntheticCalls();
        this.pairFollows.Clear();
        this.implicitPairFollows.Clear();
        foreach (var construction in this.enumConstructions.Values.In(this.pass))
        {
            construction!.IsValid = false;
        }

        this.nodes.Clear();
        this.aliases.Clear();
        this.ResetAliases();
        this.obligations.Clear();
        this.ResetLocalRegions();
        this.obligationSet.Clear();
        this.ResetCapabilities(mode);
        this.ResetContracts();
        foreach (var scope in this.scopes.Values.In(this.pass))
        {
            scope.Reset();
        }

        foreach (var symbol in this.symbols.Values.In(this.pass))
        {
            if (symbol.Property is { } property)
            {
                property.IsVerified = false;
            }

            if (symbol.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget))
            {
                symbol.Type = null;
            }

            symbol.Next = null;
            symbol.ConditionalDeclaration = null;
            symbol.Resolving = false;
            symbol.ReceiverIndex = -1;
            symbol.ObjectPayloadOptOut = null;
        }
    }
}
