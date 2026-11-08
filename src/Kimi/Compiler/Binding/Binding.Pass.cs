// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The pass reset (ResetPass), run when a pass starts and when an edit revokes the latest one (Invalidate).
// - Responsibility: every per-pass fact discarded, through the tables cleared here and the owning concerns' Reset* steps.
// - Inputs: the mode (read by ResetCapabilities). Outputs: empty per-pass tables, reset scopes and symbols, the next storageVersion.
// - Invariants: the cross-pass storage ResetPass's comment lists stays. R7-D2 b replaces the Clears with generation-stamped tables.
// - Prohibitions: no fact is computed or recorded here; ResetPass only empties tables and resets state.
public sealed partial class Binding
{
    // Discards every fact of the latest pass. Cross-pass storage stays: interned Types, Origins and Constraints, scopes and
    // symbols (reset here, removed by the prune steps), synthesized nodes, pooled plans and scratch buffers.
    private void ResetPass(BindingMode mode)
    {
        this.ResetInferenceMetrics();
        this.inferredConstructorTargets.Clear();
        this.callInferenceFailures?.Clear();
        this.storageVersion++;
        this.signatureWalks = 0;
        this.memberWalks = 0;
        this.baseSteps = 0;
        this.clausesBindable = false;
        this.verifiedClause = null;
        this.earlyReads?.Clear();
        this.earlyObjectForms?.Clear();
        this.storageMarks.Clear();
        this.storageStack.Clear();
        this.issues.Clear();
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
        this.adaptations.Clear();
        this.inferredArrayLengths.Clear();
        this.arrayInferenceShapes.Clear();
        this.arrayInferenceFailures?.Clear();
        this.noInitFailures?.Clear();
        this.ResetSyntheticCalls();
        this.pairFollows.Clear();
        this.implicitPairFollows.Clear();
        foreach (var construction in this.enumConstructions.Values)
        {
            construction.IsValid = false;
        }

        this.nodes.Clear();
        this.aliases.Clear();
        this.ResetAliases();
        this.obligations.Clear();
        this.ResetLocalRegions();
        this.obligationSet.Clear();
        this.inheritedOriginTypes.Clear();
        this.ResetCapabilities(mode);
        this.ResetContracts();
        foreach (var scope in this.scopes.Values)
        {
            scope.Reset();
        }

        foreach (var symbol in this.symbols.Values)
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
