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
//   evaluation during the run, the proof's limit, the site's declaration's refusal (RefuseProof: one record a declaration and limit),
//   so a verdict cached with it gives every later reader that record (R7-C6 a: the capability proof's limit; a proof keeps no growth
//   bound of its own).
// - Invariants: one compare per check, no table; Binding's walks recurse at most the limit plus a written layer's syntax depth
//   (BU-112), inside the compile stack's margin (DECISIONS U-D2's entry). A refusal outside every consultation frame during the run
//   is a broken contract: InternalInvariant_Kd at the project, never a silent null (a pass step no frame covers yet, until PLAN G651
//   a2-a4 frame the step; a4 makes it a throw), but inside a capability work, whose refusal is RefuseProof's at the work scope's
//   declaration (R7-M38's no-frame rule). After the run it is answered to the innermost after-run owner (PLAN G651 U2b R1): the
//   instance collector's answer frame (R7-T1; TyCtxt.OpenAnswer, CloseAnswer: every substitution the collector, Layout or a witness
//   causes is inside it), an after-run site (a MIR driver's body site and the node sites inside it; Binding's after-Check walk and the
//   editor boundary, whose marks are dropped; TyCtxt.OpenBodySite, OpenSite) or a capability work's evaluation (OpenCapabilityOwner,
//   C8 ruling 30: its refusal answers Unknown with no record, R7-M38). There a refusal is answered to the innermost formation
//   owning the Type (OpenFormation): a substitution at a requesting site (TyCtxt.Answered, giving TyCtxt.Formation.HeightLimit), an
//   instance's stored Types (Instance, published as the instance's HeightLimit, which Binding's storage walk reads as a limit and Layout
//   as its HeightLimit) or a requirement call's witness resolution (TyCtxt.Witness, its target's Cause HeightLimit); a formation
//   publishes it and records nothing. Every after-run read of a published refusal, and a refusal no formation owns, is a use at the
//   innermost owner (AnswerSite): in the frame the collector's readers record it; in a site it marks the site, whose driver records
//   each mark through MirReport's least-use table (R5-M171); with no owner it is a broken contract, thrown (R13.1; code generation
//   opens none: it runs on an accepted program, whose Types the collector formed). A refusal the frame took that no formation owned is
//   R7-T1's broken contract, which the collector's run throws after the frame's close. After the run a consultation frame fails no
//   node (RunSite; G881b N6). SubstituteType's family (SubstituteType, ContractType, StoredType, KeptType, StorageInstance, the
//   identity judge, proof normalization, the relations' context-free fits) takes no site: its refusal reads the frame its caller holds
//   open; the proof entries and openers name their judgment's site (QuerySite, PLAN G651 a1), where the outermost proof scope opens the
//   frame when none is open.
// - Prohibitions: no check before a walk from its inputs' heights (it over-approximates); no other suppression than SPEC 23.1.2's rests.
public sealed partial class Binding
{
    /// <summary>H, the greatest height a Type may have (IMPL A.4).</summary>
    internal const int TypeHeightLimit = 8192;

    // A refusal without a site in this pass (a broken contract, reported by ReportDiagnostics; the library's freeze counts it as a record).
    [Transitional("G651 a4", Reason = "A refusal has no site only during the run, in a pass step no consultation frame covers (RefuseHeight's in-run arm; the proof entries and openers name their site, a1, and SubstituteType's family reads the open frame); after the run it is answered to an owner or thrown (PLAN G651 U2b R13.1). a4 frames the last such step and makes that arm the throw, removing this flag, RecordSitelessHeightRefusal, ReportFailures' re-report, FreezeLibrary's term and ResetPass' clear.")]
    private bool sitelessHeightRefusal;

    // The answer frame (OpenAnswer) and the innermost formation in it (OpenFormation): whether a frame is open and whether a refusal was
    // answered to the formation, else to the frame. The after-run form of the one frame a refusal reads (R7-M54; F's C8 ruling 13): the
    // instance collector's run and, after the run, every formation's state under the innermost after-run owner (G651 U2b, whose owner
    // field carries the site).
    private (bool Open, bool Answered) answer;

    private (AfterRunOwner Kind, SourceRef Subject) owner; // The innermost after-run owner (PLAN G651 U2b R2).
    private SiteMark[] marks = new SiteMark[8]; // The open body sites' marks, a stack of each body site's own (OpenBodySite).
    private int markCount;
    private int markStart; // The innermost body site's first mark.

    /// <summary>The innermost owner of a height refusal after Binding's run (PLAN G651 U2b R1, R2): none, the instance collector's answer
    /// frame, a body site or a node site.</summary>
    internal enum AfterRunOwner : byte
    {
        /// <summary>No owner: a refusal after the run, or a read of one, is a broken contract, thrown (R13.1).</summary>
        None,

        /// <summary>The instance collector's answer frame (R7-T1): its readers record.</summary>
        Frame,

        /// <summary>A MIR body site, or Binding's after-Check site: a read marks it with its own subject as a fallback.</summary>
        Body,

        /// <summary>A node site inside a body site: a read marks it at its subject.</summary>
        Node,

        /// <summary>A capability work's evaluation (R4 Rev 2's fourth owner; C8 ruling 30): a refusal in it that no nearer formation owns,
        /// or a read of one, is the work's, which answers Unknown with no record (R7-M38: a lint's, MIR's or the editor's question is no
        /// judgment).</summary>
        Capability,
    }

    // The run's site: the open consultation frame's node while the pass produces its failures; none after the pass, where a query fails
    // no node (R7-M38; PLAN G651 U2). The one test of "a site to record at" for RefuseHeight and RefusedConformance (G651 U2c's predicate;
    // R7-C6 a carries it where it lands first).
    private Koto? RunSite => this.producing ? this.consultationNode : null;

    /// <summary>Opens the answer frame of the instance collector's run (TyCtxt.OpenAnswer, its only opener; R7-T1): a refusal of a Type
    /// higher than the limit inside it, with no consultation frame open, is answered, not recorded; it is the innermost after-run owner,
    /// so it closes any open site until <see cref="CloseAnswer(OwnerState)"/>.</summary>
    /// <returns>The state it opens in, which <see cref="CloseAnswer(OwnerState)"/> restores.</returns>
    internal OwnerState OpenAnswer()
    {
        var parent = this.Saved();
        (this.answer, this.owner) = ((true, false), (AfterRunOwner.Frame, default));
        return parent;
    }

    /// <summary>Closes the collector's answer frame (R7-T1, R4 Rev 2): it records nothing.</summary>
    /// <param name="parent">The state it opened in.</param>
    /// <returns>Whether the frame took a refusal no formation owned: R7-T1's broken contract, which the collector's run throws after the
    /// close.</returns>
    internal bool CloseAnswer(OwnerState parent)
    {
        var answered = this.answer.Answered;
        this.Restore(parent);
        return answered;
    }

    /// <summary>Opens a body site after Binding's run (PLAN G651 U2b R2, R5): a MIR driver's element every path of it passes through, or
    /// Binding's after-Check walk and the editor boundary; a refusal or a read of one inside it, outside a nearer formation or node
    /// site, marks it with its subject as a fallback.</summary>
    /// <param name="subject">The body's declaration.</param>
    /// <returns>The state it opens in, which <see cref="CloseBodySite"/> restores.</returns>
    internal OwnerState OpenBodySite(SourceRef subject)
    {
        var parent = this.Saved();
        (this.answer, this.owner, this.markStart) = ((false, false), (AfterRunOwner.Body, subject), this.markCount);
        return parent;
    }

    /// <summary>Closes a body site and hands its driver the marks made in it (nested body sites took theirs), one per key with its least
    /// use, valid until the next mark.</summary>
    /// <param name="parent">The state it opened in.</param>
    /// <returns>Its marks.</returns>
    internal ReadOnlySpan<SiteMark> CloseBodySite(OwnerState parent)
    {
        var (start, end) = (this.markStart, this.markCount);
        this.Restore(parent);
        this.markCount = start;
        return this.marks.AsSpan(start, end - start);
    }

    /// <summary>Opens a node site inside a body site (PLAN G651 U2b R5): the innermost node a driver visits (IMPL 21.3.2), opened at the
    /// dispatching call; its marks wait for the enclosing body site. A location with no syntax (one the builder synthesized: no subject)
    /// is no node, so the enclosing site stays innermost. Only a MIR driver opens one, inside a body or node site and never inside a
    /// formation (no formation calls a driver), so the answer state is closed and the mark range is the body site's: it saves and sets
    /// the owner alone (PLAN G651 U2b ruling 30 (1); slot T-348). Binding's retention site (OpenRetentionSite) saves the whole state.</summary>
    /// <param name="subject">The node.</param>
    /// <returns>The owner it opens in, which <see cref="CloseSite"/> restores.</returns>
    internal (AfterRunOwner Kind, SourceRef Subject) OpenSite(SourceRef subject)
    {
        var parent = this.owner;
        if (subject.Id != 0)
        {
            this.owner = (AfterRunOwner.Node, subject);
        }

        return parent;
    }

    /// <summary>Closes a node site.</summary>
    /// <param name="parent">The owner it opened in.</param>
    internal void CloseSite((AfterRunOwner Kind, SourceRef Subject) parent) => this.owner = parent;

    /// <summary>Begins a formation that owns its refusals (TyCtxt.Answered, TyCtxt.Witness, Instance): inside the collector's frame, and
    /// after Binding's run (producing over) in every owner, since it publishes what is answered to it as its cause (Formation.HeightLimit,
    /// the target's Cause, the instance's HeightLimit), which its reader answers to the innermost owner (AnswerSite); during the run it
    /// owns nothing, so a refusal is recorded at its site.</summary>
    /// <returns>The frame or formation it begins in, which <see cref="CloseAnswer(ValueTuple{bool, bool})"/> restores.</returns>
    internal (bool Open, bool Answered) OpenFormation()
    {
        var parent = this.answer;
        this.answer = (parent.Open || !this.producing, false);
        return parent;
    }

    /// <summary>Ends a formation <see cref="OpenFormation"/> began; it records nothing.</summary>
    /// <param name="parent">The frame or formation it began in.</param>
    /// <returns>Whether a refusal inside it was answered to it.</returns>
    internal bool CloseAnswer((bool Open, bool Answered) parent)
    {
        var answered = this.answer.Answered;
        this.answer = parent;
        return answered;
    }

    /// <summary>Answers a read of a published height refusal, or a refusal no formation owns, to the innermost after-run owner (PLAN G651
    /// U2b R3, R13.1), in every pass state: a site gets a mark; the collector's frame nothing, its readers record; a capability work
    /// nothing, the work answers Unknown (R7-M38); with no owner, the broken contract, thrown.</summary>
    /// <param name="refused">The refused instance's Type, or null for a substitution's refusal.</param>
    internal void AnswerSite(BoundType? refused)
    {
        switch (this.owner.Kind)
        {
            case AfterRunOwner.Body or AfterRunOwner.Node:
                this.Mark(new(refused, this.owner.Subject, this.owner.Kind == AfterRunOwner.Body));
                break;
            case AfterRunOwner.None:
                throw new InvalidOperationException("A height refusal, or a read of one, reached no after-run owner (InternalInvariant; PLAN G651 U2b R13.1).");
        }
    }

    // A Type higher than the limit.
    private static bool OverHeight(BoundType? type) => type is { Height: > TypeHeightLimit };

    /// <summary>Opens the retention input site (PLAN G651 U2b R11 OP1 Rev 2): input <paramref name="slot"/> of a callee's contract (-1: its
    /// result), read where the innermost owner is the callee's own body site (no node site open; its subject the callee's declaration: A5's
    /// own-contract read), is a node site at the written Type of that input (TyCtxt.SignatureSite; a destructor's receiver, which no
    /// parameter writes, at the declaration). Elsewhere it opens nothing: in the run, in the collector's frame, and for a call's retention,
    /// whose innermost owner is the caller's site.</summary>
    /// <param name="callee">The callee, or null for an erased signature, which names no callee syntax.</param>
    /// <param name="slot">The input's parameter slot, or -1 for the result.</param>
    /// <returns>The state it opens in, which <see cref="Restore"/> restores.</returns>
    private OwnerState OpenRetentionSite(FunctionKoto? callee, int slot)
    {
        var parent = this.Saved();
        if (callee is not null && this.owner.Kind == AfterRunOwner.Body && this.owner.Subject == SourceRef.Written(callee))
        {
            var written = slot < callee.Parameters.Count ? TyCtxt.SignatureSite(callee, slot) : callee;
            (this.answer, this.owner) = ((false, false), (AfterRunOwner.Node, SourceRef.Of(written)));
        }

        return parent;
    }

    // Opens a capability work's evaluation as the innermost after-run owner (R4 Rev 2, C-R4; C8 ruling 30): after the run it saves the
    // state and clears the formation and the site, so a refusal in the work is the work's and one in a formation opened inside it the
    // formation's; during the run it opens nothing (R13.1 P1: the work's refusal is RefuseProof's). Its drain restores the state.
    private OwnerState OpenCapabilityOwner()
    {
        var parent = this.Saved();
        if (!this.producing)
        {
            (this.answer, this.owner) = ((false, false), (AfterRunOwner.Capability, default));
        }

        return parent;
    }

    // Folds a mark at mark time (R2 Rev 2, D-C4): one per key (the refused Type, or a null mark's subject) in the open body site, its least
    // (Fallback, subject) use.
    private void Mark(SiteMark mark)
    {
        for (var i = this.markCount - 1; i >= this.markStart; i--)
        {
            ref var kept = ref this.marks[i];
            if (ReferenceEquals(kept.Refused, mark.Refused) && (mark.Refused is not null || kept.Subject == mark.Subject))
            {
                if (SourceRef.UseOrder((mark.Fallback, mark.Subject), (kept.Fallback, kept.Subject)) < 0)
                {
                    kept = mark;
                }

                return;
            }
        }

        if (this.markCount == this.marks.Length)
        {
            Array.Resize(ref this.marks, this.markCount * 2);
        }

        this.marks[this.markCount++] = mark;
    }

    // The state an opener saves and its close restores.
    private OwnerState Saved() => new(this.answer, this.owner.Kind, this.owner.Subject, this.markStart);

    private void Restore(OwnerState parent) => (this.answer, this.owner, this.markStart) = (parent.Answer, (parent.Kind, parent.Subject), parent.Start);

    // Clears the after-run owner and its marks (the pass reset).
    private void ResetSites()
    {
        Array.Clear(this.marks, 0, this.markCount);
        (this.owner, this.markStart, this.markCount) = (default, 0, 0);
    }

    // A refusal of a Type higher than the limit, recorded at the current site, else answered to the open frame's innermost formation,
    // else during the run the site-less record (PLAN G651 a4's arm) and after it the innermost after-run owner's; null, the refused
    // formation's image. Inside a capability work's evaluation during the run the refusal is the proof's limit (L, DECISIONS BU-22): the
    // site's declaration's refusal (RefuseProof: one record a declaration and limit, at the declaration's first refused site; with no
    // frame open the work scope's declaration, R7-M38), on which a later refusal of the declaration and every reader of a verdict cached
    // with it rest (SPEC 8.3.3 Limits, 23.1.2; O-321 (b); RestsOnRecoveryOrLimit): the engine's works are keyed by a scope, not a
    // declaration, so a nested block's or Closure's work forms the Type again (R7-C6 a, F's C8 rulings 6 and 13). Every other frame, a
    // validation pass's included, keeps its own record (BU-161; F's C2 ruling 257 (2)). After the run the work is an after-run owner
    // (OpenCapabilityOwner; C8 ruling 30): a formation opened inside it is nearer and takes the refusal (R4 Rev 2, C-R4), else the work's
    // AnswerSite answers Unknown with no record, as the round bound's (R7-M38: a lint's, MIR's or the editor's question is no judgment).
    private BoundType? RefuseHeight()
    {
        if (this.producing && this.capabilityConsumer is { } work)
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

        if (this.producing)
        {
            this.RecordSitelessHeightRefusal(); // A pass step no consultation frame covers (PLAN G651 a4).
            return null;
        }

        this.AnswerSite(null); // No formation after the run: the innermost after-run owner's, none the throw (PLAN G651 U2b R3.1, R13.1).
        return null;
    }

    // G651's site-less broken contract during the run: a refusal in a pass step no consultation frame covers (PLAN G651 a4).
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

/// <summary>The state an after-run owner's opener saves and its close restores (PLAN G651 U2b R2): the formation state, the innermost
/// after-run owner and its subject, and the open body site's first mark.</summary>
/// <param name="Answer">The answer frame's state.</param>
/// <param name="Kind">The innermost after-run owner.</param>
/// <param name="Subject">Its site's subject.</param>
/// <param name="Start">The open body site's first mark.</param>
internal readonly record struct OwnerState((bool Open, bool Answered) Answer, Binding.AfterRunOwner Kind, SourceRef Subject, int Start);

/// <summary>A read of a published height refusal in an after-run site (PLAN G651 U2b R2): the refused instance's Type, or null for a
/// substitution's refusal (keyed by its subject, as the collector's Formed keys its forming site); the innermost site's subject; whether
/// that site is a body site, no node site open (a fallback after every use subject).</summary>
/// <param name="Refused">The refused instance's Type, or null.</param>
/// <param name="Subject">The innermost site's subject.</param>
/// <param name="Fallback">Whether the site is a body site.</param>
internal readonly record struct SiteMark(BoundType? Refused, SourceRef Subject, bool Fallback);
