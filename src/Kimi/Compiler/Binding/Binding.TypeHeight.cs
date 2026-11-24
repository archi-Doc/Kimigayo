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
//   declaration, an override's premises, the conformance PrepareAssociatedBindings resolves); a judgment's proof query's site frame,
//   opened at its node by the outermost proof scope where none is open (BeginProof, OpenQuerySite; R7-C6 a). A frame is a site only
//   while the pass produces its failures (RunSite).
// - Outputs: the site's failure (BindingFailure.TypeHeightLimit, its first failure only); a refused node publishes no Type, so a node
//   reading it rests on it (SPEC 23.1.2, 8.3.3 Limits) and a later failure of the same node is not recorded; inside a capability work's
//   evaluation, the proof's limit, the site's declaration's refusal (RefuseProof: one record a declaration and limit), so a verdict
//   cached with it gives every later reader that record (R7-C6 a: the capability proof's limit; a proof keeps no growth bound of its own).
// - Invariants: one compare per check, no table; Binding's walks recurse at most the limit plus a written layer's syntax depth
//   (BU-112), inside the compile stack's margin (DECISIONS U-D2's entry). A refusal outside every consultation frame is a broken
//   contract: InternalInvariant_Kd at the project, never a silent null, but inside the instance collector's answer frame (R7-T1;
//   PLAN G651's after-run site kind "the caller records"), which only the collector's run opens and closes (TyCtxt.OpenAnswer,
//   CloseAnswer: every substitution after Binding's run that the collector, Layout or a witness causes is inside it): there the
//   refusal is answered to the innermost formation owning the Type, one of three (OpenFormation; the instance's OpenStorageFormation):
//   a substitution at a requesting site (TyCtxt.Answered, giving TyCtxt.Formation.HeightLimit, recorded at that site), an instance's
//   stored Types (Instance, published as the instance's HeightLimit, which Binding's storage walk reads as a limit and Layout as its
//   HeightLimit, recorded at the use) or a requirement call's witness resolution (TyCtxt.Witness, its target's Cause HeightLimit,
//   recorded at the requesting site); one no formation owns is the frame's, recorded at its close as the same site-less broken
//   contract; Binding writes nothing else. After Binding's run the instance's formation owns its refusals in every frame (PLAN G651
//   U2a, OpenStorageFormation), so no reader meets a part refused without its HeightLimit: after the run a consultation frame fails no
//   node (RunSite; G881b N6); outside the collector's frame it still records the site-less record at its close, since
//   no reader there gives a site yet (G651 U2b). Otherwise nothing is answered outside the frame (Binding's run, code generation).
//   SubstituteType's family (SubstituteType, ContractType, StoredType, KeptType, StorageInstance, the identity judge, proof
//   normalization, the relations' context-free fits) takes no site: its refusal reads the frame its caller holds open; the proof
//   entries and openers name their judgment's site (QuerySite, PLAN G651 a1), where the outermost proof scope opens the frame when
//   none is open. A family formation in a pass step no frame covers keeps the site-less interim until PLAN G651 a2-a4 frame the step,
//   but inside a capability work, whose refusal is RefuseProof's at the work scope's declaration (R7-M38's no-frame rule).
// - Prohibitions: no check before a walk from its inputs' heights (it over-approximates); no other suppression than SPEC 23.1.2's rests.
public sealed partial class Binding
{
    /// <summary>H, the greatest height a Type may have (IMPL A.4).</summary>
    internal const int TypeHeightLimit = 8192;

    // A refusal without a site in this pass (a broken contract, reported by ReportDiagnostics; the library's freeze counts it as a record).
    [Transitional("G651 a4", Reason = "A refusal has no site only in a pass step not yet framed during the run, or in an instance's formation after the run outside the collector's frame (PLAN G651 U2a): the proof entries and openers name their site (a1) and SubstituteType's family reads the open frame, so a4, which frames the last such step, removes the run's path; U2b removes the after-run path by giving each reader of such an instance a site of its own.")]
    private bool sitelessHeightRefusal;

    // The answer frame (OpenAnswer) and the innermost formation in it (OpenFormation): whether a frame is open and whether a refusal was
    // answered to the formation, else to the frame. The after-run form of the one frame a refusal reads (R7-M54; F's C8 ruling 13): the
    // instance collector's run and, after the run, an instance's storage formation outside it (G651 U2a, OpenStorageFormation).
    private (bool Open, bool Answered) answer;

    // The run's site: the open consultation frame's node while the pass produces its failures; none after the pass, where a query fails
    // no node (R7-M38; PLAN G651 U2). The one test of "a site to record at" for RefuseHeight and RefusedConformance (G651 U2c's predicate;
    // R7-C6 a carries it where it lands first).
    private Koto? RunSite => this.producing ? this.consultationNode : null;

    /// <summary>Opens the answer frame of the instance collector's run (TyCtxt.OpenAnswer, its only opener; R7-T1): a refusal of a Type
    /// higher than the limit inside it, with no consultation frame open, is answered, not recorded.</summary>
    /// <returns>The frame it opens in, which <see cref="CloseAnswer"/> restores.</returns>
    internal (bool Open, bool Answered) OpenAnswer()
    {
        var parent = this.answer;
        this.answer = (true, false);
        return parent;
    }

    /// <summary>Begins a formation that owns its refusals inside the open frame (TyCtxt.Answered, TyCtxt.Witness); outside one it owns
    /// nothing, so a refusal is recorded as before.</summary>
    /// <returns>The frame or formation it begins in, which <see cref="CloseAnswer"/> restores.</returns>
    internal (bool Open, bool Answered) OpenFormation()
    {
        var parent = this.answer;
        this.answer = (parent.Open, false);
        return parent;
    }

    /// <summary>Begins an instance's stored-Types formation (Instance; PLAN G651 U2a): inside the open frame as <see cref="OpenFormation"/>,
    /// and after Binding's run (producing over) in every frame, since it publishes what is answered to it as the instance's HeightLimit,
    /// which every reader of the instance reads (Binding's storage walk a limit, Layout its HeightLimit). Closing to no frame it still
    /// records the site-less record (<see cref="CloseAnswer"/>): outside the collector's frame no reader of the instance gives a site yet.
    /// Its premise (U2b reads it too): no formation nests inside it after the run, since instance substitution never queries storage
    /// (PrepareTypeStorage's guard) and Close substitutes by TyCtxt.Apply alone; a nested Instance, TyCtxt.Answered or TyCtxt.Witness
    /// would close to its Open parent and make no site-less record.</summary>
    /// <returns>The frame or formation it begins in, which <see cref="CloseAnswer"/> restores.</returns>
    internal (bool Open, bool Answered) OpenStorageFormation()
    {
        var parent = this.answer;
        this.answer = (parent.Open || !this.producing, false);
        return parent;
    }

    /// <summary>Ends a formation or closes the frame <see cref="OpenAnswer"/>, <see cref="OpenFormation"/> or
    /// <see cref="OpenStorageFormation"/> began.</summary>
    /// <param name="parent">The frame or formation it began in.</param>
    /// <returns>Whether a refusal inside it was answered to it.</returns>
    internal bool CloseAnswer((bool Open, bool Answered) parent)
    {
        var answered = this.answer.Answered;
        this.answer = parent;
        if (answered && !parent.Open)
        {
            this.RecordSitelessHeightRefusal(); // Closing to no frame: the frame's own, or an instance's after the run outside it (U2a).
        }

        return answered;
    }

    // A Type higher than the limit.
    private static bool OverHeight(BoundType? type) => type is { Height: > TypeHeightLimit };

    // A refusal of a Type higher than the limit, recorded at the current site, else answered to the open frame's innermost formation;
    // null, the refused formation's image. Inside a capability work's evaluation, in every pass state, the refusal is the proof's limit
    // (L, DECISIONS BU-22): the site's declaration's refusal (RefuseProof: one record a declaration and limit, at the declaration's first
    // refused site; with no frame open the work scope's declaration, R7-M38), on which a later refusal of the declaration and every
    // reader of a verdict cached with it rest (SPEC 8.3.3 Limits, 23.1.2; O-321 (b); RestsOnRecoveryOrLimit): the engine's works are keyed
    // by a scope, not a declaration, so a nested block's or Closure's work forms the Type again (R7-C6 a, F's C8 rulings 6 and 13). After
    // the pass RefuseProof fails no node, so the work answers Unknown with no record, as the round bound's (a lint's, MIR's or the
    // editor's question is no judgment). Every other frame, a validation pass's included, keeps its own record (BU-161; F's C2 ruling
    // 257 (2)).
    private BoundType? RefuseHeight()
    {
        if (this.capabilityConsumer is { } work)
        {
            this.RefuseProof(this.RunSite is { } judged ? this.ConstraintScope(judged) : work.Scope, BindingFailure.TypeHeightLimit);
            return null;
        }

        if (this.RunSite is { } site)
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

        if (this.RunSite is not null)
        {
            this.RefuseHeight();
        }

        return true;
    }
}
