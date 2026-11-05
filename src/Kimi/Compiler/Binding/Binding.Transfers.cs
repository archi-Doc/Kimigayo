// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// TransferRequired judged in Binding (SPEC 3.5, 7.2.3, 14.2.2, 23.1.2; R6-check H1; DECISIONS BU-84).
// - Responsibility: the bare acquisitions that move a Place, read from the published columns after a Final pass's publication:
//   S1, a consumed child the Adjust column moves; S2, a discarded Place the column leaves Unset, whose bare acquisition moves. Each
//   fails with TransferRequired through FailAcquisition; a Place rooted in a prepared parameter records DefaultArgumentMove_Kd.
//   S3, an omitted capture list's capture, is judged at closure binding (Binding.Closures).
// - Inputs: each module's published Op, Adjust and PlaceShape rows and its nodes; Binding's bare acquisition (BareAcquire).
// - Outputs: the failed nodes; whether one failed, after which Check publishes once more and asserts the result.
// - Invariants: one linear scan of each module, 0 B; the republication creates no new subject (an InternalInvariant_Kd if it does).
// - Must not: decide an acquisition (the column's writers do) or judge a node Binding failed before (its row is Error).

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Whether a node is the operand of the explicit Move `x@move` (SPEC 3.5), through parentheses.
    private static bool ExplicitTransfer(Koto node)
    {
        var parent = node.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        return parent is ConversionKoto { ConversionBinding: ConversionBinding.Transfer };
    }

    // Whether a Place is a prepared parameter or its owned contents (SPEC 7.2.3): rooted in one through parentheses and projections of owned
    // storage. A follow, or a projection past a reference or object layer, reaches a referent the argument does not own.
    private static bool PreparedRoot(Koto node)
    {
        var current = KotoHelper.UnwrapParentheses(node);
        while (current is MemberAccessKoto or IndexKoto)
        {
            current = KotoHelper.UnwrapParentheses(current is MemberAccessKoto member ? member.Left : ((IndexKoto)current).Left);
            if (current.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner })
            {
                return false;
            }
        }

        return HirTables.ResOf(current) == HirRes.PreparedParam;
    }

    // H1 after the Final publication: fails every subject; true when one failed, so the modules are published again.
    private bool CheckTransfers()
    {
        var failed = false;
        for (var i = 0; i <= this.compilation.SourceModules.Length; i++)
        {
            var nodes = this.TransferTable(i).Nodes;
            for (var id = 1; id < nodes.Length; id++)
            {
                if (nodes[id] is { } node && this.TransferSubject(node))
                {
                    this.FailAcquisition(node, BindingFailure.TransferRequired, node);
                    failed = true;
                }
            }
        }

        return failed;
    }

    // The republication's assertion: a node H1 failed is published Error, which neither S1 nor S2 selects, and a closure S3 failed
    // was failed before the first publication, so the republished columns hold no subject; one that does is an internal error.
    private void AssertTransfers()
    {
        for (var i = 0; i <= this.compilation.SourceModules.Length; i++)
        {
            var nodes = this.TransferTable(i).Nodes;
            for (var id = 1; id < nodes.Length; id++)
            {
                if (nodes[id] is { } node && this.TransferSubject(node))
                {
                    this.Fail(node, BindingFailure.Internal);
                }
            }
        }
    }

    // The modules in publication order, the library last.
    private SyntaxTable TransferTable(int i)
        => i < this.compilation.SourceModules.Length ? this.compilation.SourceModules[i].Syntax : this.Library.Kotonoha.Syntax;

    // S1: a written Place (not a value, not a formatting plan's storage) in a value Op whose Adjust moves it, unless its parent is the
    // explicit Move (`x@move`). S2: such a Place in Discard Context whose Adjust is Unset and whose bare acquisition moves, with a
    // proof other than Error (an Error proof is derived from that error). A failed acquisition is derived too (DECISIONS BU-103).
    private bool TransferSubject(Koto node)
    {
        if (HirTables.Op(node) is HirOp.Unset or HirOp.Syntax or HirOp.Error || HirTables.PlaceOf(node).Kind is HirPlaceKind.Unset or HirPlaceKind.Value ||
            node is FormattingKoto || node.BindingFailure != BindingFailure.None)
        {
            return false;
        }

        return HirTables.AdjustOf(node).Acquire switch
        {
            HirAcquire.Move => !ExplicitTransfer(node),
            HirAcquire.Unset => !KotoHelper.IsValueContext(node) && node.BoundType is { } type &&
                this.BareAcquire(type, node, out var proof) == HirAcquire.Move && proof != ConstraintProof.Error,
            HirAcquire.None or HirAcquire.Copy or HirAcquire.Reborrow or HirAcquire.ByCase => false, // Not a Move.
            HirAcquire.Temporary or HirAcquire.Failed => false, // A value, or one derived from its failure.
            _ => throw new UnreachableException(),
        };
    }

    // S3 (SPEC 7.7.2): an omitted capture list captures each binding only by Copy; it never infers a Move, a borrow or a Reborrow. At
    // the end of the Closure's binding, a capture whose bare acquisition is not Copy fails the Closure's creation, once, at its first
    // such capture in occurrence order (ReportCaptureEntry's omitted-list form); each proven one is a Copy, set explicitly.
    private void JudgeOmittedCaptures(FunctionKoto function, BoundClosure plan)
    {
        if (function.Captures is not null)
        {
            return;
        }

        for (var i = 0; i < plan.Storage.Count; i++)
        {
            var (source, environment) = plan.Storage[i];
            if (source.Type is not { } type)
            {
                continue;
            }

            if (this.BareAcquire(type, function, out _) == HirAcquire.Copy)
            {
                environment.CaptureAcquisition = CaptureAcquisition.Copy;
            }
            else if (function.BindingFailure == BindingFailure.None)
            {
                var capture = new CaptureFailure(default, source.Name, type, source, null, null, null);
                this.FailExplained(ref this.captureFailures, function, BindingFailure.TransferRequired, capture);
            }
        }
    }

    // SPEC 7.2.3 (Q8-4, Q8-8): whether a failed bare move is of the node's own Place (FailAcquisition at the Place itself) rooted in a
    // prepared parameter, which records DefaultArgumentMove_Kd: a default never moves a preceding argument.
    private bool PreparedMove(Koto node)
        => this.acquisitionPlaces?.TryGetValue(node, out var acquisition) == true && ReferenceEquals(acquisition.Place, node) && PreparedRoot(node);
}
