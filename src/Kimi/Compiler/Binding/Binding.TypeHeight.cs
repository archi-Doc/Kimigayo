// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// A Type's height limit (IMPL A.4; DECISIONS U-D2's entry): no Type higher than TypeHeightLimit is formed or published.
// - Responsibility: the one check, OverHeight (BoundType.Height, BU-71, against TypeHeightLimit), read where a Type's height grows by
//   more than a written layer: a substitution query's result (SubstituteType), an expression node's Type (BindNode) and a written
//   Type's (BindType); the refusal's one record, TypeHeightLimit_Kd, at Binding's current site; and the one reader rule: a reader of
//   a conformance the limit refused (an associated Type, ResolveAssociated; an inherited path's declared Self with premises, its scope,
//   VerifyInheritedConformance) is refused at its own site (RefusedConformance). Any check whose evidence read a refused node
//   derives its outcome from the record, as from every Resource record (Binding.Diagnostics.Prerequisites.cs, RestsOnRecoveryOrLimit).
// - Inputs: a formed Type and the open consultation frame's node (PLAN G651 U1): the walk's (BindNode's node, BindType's syntax); a
//   judgment frame's use, the node a validation check's Language record names (a Constraint clause, then its owner; a constructed Type
//   node; an environment's owner; an agreement's first path); a site frame's element, whose facts a pass publishes (a source state's
//   declaration, an override's premises, the conformance PrepareAssociatedBindings resolves).
// - Outputs: the site's failure (BindingFailure.TypeHeightLimit, its first failure only); a refused node publishes no Type, so a node
//   reading it rests on it (SPEC 23.1.2, 8.3.3 Limits) and a later failure of the same node is not recorded.
// - Invariants: one compare per check, no table; Binding's walks recurse at most the limit plus a written layer's syntax depth
//   (BU-112), inside the compile stack's margin (DECISIONS U-D2's entry). A refusal outside every consultation frame is a broken
//   contract: InternalInvariant_Kd at the project, never a silent null; a substitution within the limit with no frame open writes
//   nothing (the passes not yet framed: PLAN G651 a1-a4 make the site SubstituteType's input).
// - Prohibitions: no check before a walk from its inputs' heights (it over-approximates); no other suppression than SPEC 23.1.2's rests.
public sealed partial class Binding
{
    /// <summary>H, the greatest height a Type may have (IMPL A.4).</summary>
    internal const int TypeHeightLimit = 8192;

    // A refusal without a site in this pass (a broken contract, reported by ReportDiagnostics).
    [Transitional("G651 a1", Reason = "A refusal during the run has no site only in a pass not yet framed; a1 makes the site SubstituteType's input (the C# build as the census) and a4 removes the last unframed path; U2 gives the after-run queries their outcome.")]
    private bool sitelessHeightRefusal;

    // A Type higher than the limit.
    private static bool OverHeight(BoundType? type) => type is { Height: > TypeHeightLimit };

    // A refusal of a Type higher than the limit, recorded at the current site; null, the refused formation's image.
    private BoundType? RefuseHeight()
    {
        if (this.consultationNode is { } site)
        {
            return this.Fail(site, BindingFailure.TypeHeightLimit);
        }

        this.sitelessHeightRefusal = true;
        this.compilation.Diagnostics.Report(DiagnosticPartition.Binding, DiagnosticCode.InternalInvariant_Kd, this.compilation.Project.FilePath);
        return null;
    }

    // A node's Type under the limit: null when the node was refused while it formed or its own Type passes the limit, which is then
    // refused at the node (a node that failed first keeps that failure); a refused node keeps no Type.
    private BoundType? HeightChecked(Koto node, BoundType? type)
    {
        var over = OverHeight(type);
        if (over)
        {
            this.Fail(node, BindingFailure.TypeHeightLimit);
        }

        if (!over && node.BindingFailure != BindingFailure.TypeHeightLimit)
        {
            return type;
        }

        node.BoundType = null;
        return null;
    }

    // SPEC 23.1.2, 8.3.3 (Limits): a conformance the limit refused: one whose associated Type it refused gives its readers no binding
    // (ResolveAssociated); an inherited path with premises whose declared Self it refused (BoundConformancePath.DeclaredPath) has no
    // conformance scope (VerifyInheritedConformance). A reader at a site would form that Type too, so it is refused there and publishes no
    // Type; a reader without one rests on it.
    private bool RefusedConformance(BoundConformancePath path)
    {
        if (path.Declaration.BindingFailure != BindingFailure.TypeHeightLimit && path.RootPath.Declaration.BindingFailure != BindingFailure.TypeHeightLimit &&
            !(path is { InheritedFrom: not null, Premises: not null, DeclaredSelf: null }))
        {
            return false;
        }

        if (this.consultationNode is not null)
        {
            this.RefuseHeight();
        }

        return true;
    }
}
