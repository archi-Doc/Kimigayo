// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int Verification(TestVerificationKoto node)
    {
        var entry = this.current;
        var mark = this.temporaries.Count;
        var condition = this.Value(this.Expression(node.Condition, PlaceUseKind.Read));
        // Latch before destroying condition temporaries. The native result is an
        // IssueId, not a language value or ownership acquisition.
        var observed = this.Emit(OwnershipOperationKind.TestObserve, node);
        if (condition >= 0)
        {
            this.SetValue(observed, OwnershipValueKind.Alias, [condition]);
        }

        this.Cleanup(mark, this.locals.Count, node.Condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        var branch = this.Emit(OwnershipOperationKind.Branch, node);
        if (condition >= 0)
        {
            this.SetValue(branch, OwnershipValueKind.Alias, [condition]);
        }

        var join = this.New(OwnershipOperationKind.Branch, node);
        var failed = this.New(OwnershipOperationKind.Branch, node);
        this.Connect(branch, join, OwnershipEdgeKind.True);
        this.Connect(branch, failed, OwnershipEdgeKind.False);
        this.current = failed;
        var region = this.checkingRegion;
        if (node.Message is { } message)
        {
            var value = this.Expression(message);
            this.Emit(OwnershipOperationKind.TestMessage, node, value, observed);
            this.Cleanup(mark, this.locals.Count, message, CleanupReason.ExpressionEnd);
            this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        }

        if (node.IsRequire)
        {
            var abort = this.Emit(OwnershipOperationKind.TestAbort, node, input: observed);
            this.Connect(abort, this.abortExit, OwnershipEdgeKind.Abort);
        }
        else
        {
            this.Connect(this.current, join);
        }

        this.checkingRegion = region;
        this.current = join;
        this.body.RecordCompletion(entry, join, this.flow!.Nodes[node].CanCompleteNormally);
        return -1;
    }
}
