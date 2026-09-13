// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private void ExecuteDeferred(DeferredBlockKoto deferred)
    {
        // The caller has already advanced past this registration. Self-exit cleans only
        // this body's locals, then joins the pending outer exit sequence exactly once.
        var loopBase = this.deferredLoopBase;
        var selectionBase = this.deferredSelectionBase;
        this.deferredLoopBase = this.loops.Count;
        this.deferredSelectionBase = this.selections.Count;
        this.deferredDepth++;

        var parent = this.activeDeferred;
        var plan = this.body.DeferredPlanStorage.Count;
        this.body.DeferredPlanStorage.Add(default);
        this.activeDeferred = plan;

        var entry = this.Emit(OwnershipOperationKind.Branch, deferred);
        var edge = this.body.IncomingEdges[entry];
        var join = this.New(OwnershipOperationKind.Branch, deferred);
        this.selections.Add(new(deferred, -1, join, this.locals.Count, this.temporaries.Count));
        this.Block(deferred.Body);
        this.Connect(this.current, join);
        this.selections.RemoveAt(this.selections.Count - 1);
        var completes = this.flow!.Nodes[deferred].CanCompleteNormally;
        this.body.RecordCompletion(entry, join, completes);
        this.body.DeferredPlanStorage[plan] = new(deferred, edge, entry, join, this.body.Operations.Count, parent, completes);
        this.current = completes ? join : -1;

        this.activeDeferred = parent;
        this.deferredDepth--;
        this.deferredLoopBase = loopBase;
        this.deferredSelectionBase = selectionBase;
        if (!completes)
        {
            // A checking-only continuation preserves diagnostics after divergent cleanup.
            // It has no runtime edge and cannot deliver the pending transfer/result.
            this.checkingRegion = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(new(entry, -1));
        }
    }

    private sealed class DeferredExpansionLimitException(Koto source) : Exception
    {
        internal Koto SourceNode { get; } = source;
    }
}
