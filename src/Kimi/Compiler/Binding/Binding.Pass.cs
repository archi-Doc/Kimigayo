// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The pass reset (ResetPass), run when a pass starts and when an edit revokes the latest one (Invalidate), after its run's BeginRun.
// - Responsibility: every per-pass fact discarded, through the tables cleared here and the owning concerns' Reset* steps.
// - Inputs: the mode (read by ResetCapabilities) and the running pass (its modules). Outputs: empty per-pass tables, reset scopes
//   and symbols.
// - Invariants: the cross-pass storage ResetPass's comment lists stays. A kept family's rows are reset in place over the running
//   pass's modules only (In, PLAN G949 S1-r2), so no pass resets another pass's rows. R7-D2 b replaces the Clears with
//   generation-stamped tables. A reader of a per-pass table reachable outside a pass, given a node of the frozen library, refuses
//   it (EnsurePassNode): the library keeps no row of the table, so the refusal is the reader's answer.
// - Prohibitions: no fact is computed or recorded here; ResetPass only empties tables and resets state.
public sealed partial class Binding
{
    // PLAN G949 Stage 2 (U0b-C2, design 7b.1's C2) and R7-N5: a per-pass table holds the running pass's rows only, and after the
    // library's freeze (SyntaxTable.Frozen) it holds no library row: a Dictionary lost them at the program pass's ResetPass; a column
    // follows its pass counter, and either the freeze released the library pass's rows (a working-state family's Release row in
    // NodeColumns.Freeze: the refusal is the answer, the rows being gone) or the library pass writes none (its NotStamps row: "none" is
    // the library's own answer, and the refusal checks the contract that no program pass asks a library node). So such a read is the
    // internal-invariant form for good, not a staged guard. During the library pass the table is not frozen and the reader answers.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsurePassNode(Koto node, string reader)
    {
        if (node.SyntaxOwner is { Frozen: true })
        {
            RefusePassRead(reader);
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RefusePassRead(string reader)
        => throw new InvalidOperationException($"{reader} read a per-pass row of a frozen library node (InternalInvariant; PLAN G949 U0b-C2).");

    // Discards every fact of the latest pass. Cross-pass storage stays: interned Types, Origins and Constraints, scopes and
    // symbols (reset here, removed by the prune steps), synthesized nodes, pooled plans and scratch buffers.
    private void ResetPass(BindingMode mode)
    {
        this.clausesBindable = false;
        this.verifiedClause = null;
        this.earlyObjectForms?.Clear();
        this.earlyProjections.Clear();
        this.storageMarks.Clear();
        this.storageStack.Clear();
        this.issues.Clear();
        this.foreignFailures.Clear();
        this.sitelessHeightRefusal = false;
        this.lints.Clear();
        this.importDeclarations.Clear();
        this.libraryImports.Clear();
        this.environmentsJudged = -1;
        this.judgedOccurrences.Clear();
        this.ResetPrerequisites();
        this.ResetMatches();
        this.argumentParts.Clear();
        this.dependentChecks.Clear();
        this.argumentProbe = null;
        this.resultLocalEvidence.Clear();
        this.resultCursor = 0;
        this.ResetStartup();
        this.ResetSpecializations();
        this.ResetVirtualDeclarations();
        this.ResetSyntheticCalls();
        foreach (var construction in this.enumConstructions.Values.In(this.pass))
        {
            construction!.IsValid = false;
        }

        this.nodes.Clear();
        this.aliases.Clear();
        this.ResetAliases();
        this.obligations.Clear();
        this.completedDefaults = 0;
        this.ResetLocalRegions();
        this.ResetBorrows();
        this.rawFits.Clear();
        this.obligationSet.Clear();
        this.ResetCapabilities(mode);
        this.ResetContracts();
        foreach (var scope in this.scopes.Values.In(this.pass))
        {
            scope.Reset();
        }

        this.premiseFailures = 0; // The Failed state's census, with the environments it counts (ConstraintEnvironment's contract).

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
