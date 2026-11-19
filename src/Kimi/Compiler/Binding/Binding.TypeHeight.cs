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
//   contract: InternalInvariant_Kd at the project, never a silent null, but inside the instance collector's answer frame (R7-T1;
//   PLAN G651's after-run site kind "the caller records"), which only the collector's run opens and closes (TyCtxt.OpenAnswer,
//   CloseAnswer: every substitution after Binding's run that the collector, Layout or a witness causes is inside it): there the
//   refusal is answered to the innermost formation owning the Type, one of two (OpenFormation): a substitution at a requesting site
//   (TyCtxt.Answered, giving TyCtxt.Formation.HeightLimit, recorded at that site) or an instance's stored Types (Instance, published
//   as the instance's HeightLimit, which Binding's storage walk reads as a limit and Layout as its HeightLimit, recorded at the use);
//   one no formation owns is the frame's, recorded at its close as the same site-less broken contract; Binding writes nothing else.
//   Outside the frame (Binding's run, code generation) nothing is answered (the passes not yet framed: PLAN G651 a1-a4 make the site
//   SubstituteType's input).
// - Prohibitions: no check before a walk from its inputs' heights (it over-approximates); no other suppression than SPEC 23.1.2's rests.
public sealed partial class Binding
{
    /// <summary>H, the greatest height a Type may have (IMPL A.4).</summary>
    internal const int TypeHeightLimit = 8192;

    // A refusal without a site in this pass (a broken contract, reported by ReportDiagnostics; the library's freeze counts it as a record).
    [Transitional("G651 a1", Reason = "A refusal during the run has no site only in a pass not yet framed; a1 makes the site SubstituteType's input (the C# build as the census) and a4 removes the last unframed path; U2 gives the after-run queries their outcome.")]
    private bool sitelessHeightRefusal;

    // The answer frame (OpenAnswer) and the innermost formation in it (OpenFormation): whether a frame is open and whether a refusal was
    // answered to the formation, else to the frame.
    [Transitional("G651 a1", Reason = "The interim form of the after-run site kind \"the caller records\" for the instance collector's run (R7-T1; PLAN G651 U2's collector row): a1 makes the site SubstituteType's input and this frame that site kind.")]
    private (bool Open, bool Answered) answer;

    /// <summary>Opens the answer frame of the instance collector's run (TyCtxt.OpenAnswer, its only opener; R7-T1): a refusal of a Type
    /// higher than the limit inside it, with no consultation frame open, is answered, not recorded.</summary>
    /// <returns>The frame it opens in, which <see cref="CloseAnswer"/> restores.</returns>
    internal (bool Open, bool Answered) OpenAnswer()
    {
        var parent = this.answer;
        this.answer = (true, false);
        return parent;
    }

    /// <summary>Begins a formation that owns its refusals inside the open frame (TyCtxt.Answered, Instance); outside one it owns
    /// nothing, so a refusal is recorded as before.</summary>
    /// <returns>The frame or formation it begins in, which <see cref="CloseAnswer"/> restores.</returns>
    internal (bool Open, bool Answered) OpenFormation()
    {
        var parent = this.answer;
        this.answer = (parent.Open, false);
        return parent;
    }

    /// <summary>Ends a formation or closes the frame <see cref="OpenAnswer"/> or <see cref="OpenFormation"/> began.</summary>
    /// <param name="parent">The frame or formation it began in.</param>
    /// <returns>Whether a refusal inside it was answered to it.</returns>
    internal bool CloseAnswer((bool Open, bool Answered) parent)
    {
        var answered = this.answer.Answered;
        this.answer = parent;
        if (answered && !parent.Open)
        {
            this.RecordSitelessHeightRefusal(); // The frame's own: no formation owned it (a frame closes to no frame).
        }

        return answered;
    }

    // A Type higher than the limit.
    private static bool OverHeight(BoundType? type) => type is { Height: > TypeHeightLimit };

    // A refusal of a Type higher than the limit, recorded at the current site, else answered to the open frame's innermost formation;
    // null, the refused formation's image.
    private BoundType? RefuseHeight()
    {
        if (this.consultationNode is { } site)
        {
            return this.Fail(site, BindingFailure.TypeHeightLimit);
        }

        if (this.answer.Open)
        {
            this.answer.Answered = true; // The formation owning the Type publishes it (R7-T1).
            return null;
        }

        this.RecordSitelessHeightRefusal();
        return null;
    }

    // G651's site-less broken contract: a refusal no site or formation owns.
    private void RecordSitelessHeightRefusal()
    {
        this.sitelessHeightRefusal = true;
        this.compilation.Diagnostics.Report(DiagnosticPartition.Binding, DiagnosticCode.InternalInvariant_Kd, this.compilation.Project.FilePath);
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
