// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture failure facts and their publication (SPEC 7.7.2, 16.3.1, 23.1): the one recorder of a failed capture check (RecordCapture) with
// the facts it records where the check fails (a written entry, a repeated capture name, an omitted list's use or capture, an implicit
// capture of a contextual binding, a capture of the helper borrow, a later value that brings it into a captured var's region), and the
// record each publishes (ReportCapture). The judgments stay with their owners (Binding.Captures, Binding.Closures, Binding.Transfers,
// the receiver table in Binding.Structs); this file never re-runs one to explain a failure.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private const string HelperBorrowNote = "the helper borrow may be passed to helpers but cannot be captured (SPEC 16.3.1)";

    private const string PreparedNote = "a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";

    // A capture record's facts, a kept scratch (CaptureFacts), so a warm rebind of a rejected capture allocates none.
    private readonly List<ProblemFact> captureFacts = [];

    // The walk that finds a capture's first occurrence in an omitted-list Closure (CaptureUse), kept so a warm judgment allocates none.
    private CaptureUse? captureUse;

    // SPEC 7.7.2, 16.3.1: the closed reason a capture check fails for, the low byte of its record's Reason (CaptureFacts); the adapter
    // explains each (ReportCapture).
    private enum CaptureProblem : byte
    {
        Entry, // A written entry's own initialization (CaptureEntry): TransferRequired or InvalidAssignment.
        Receiver, // A written entry of a construction or destruction receiver (SPEC 7.7.2, 6.3).
        Helper, // A written entry or an omitted list's use of a value that carries the helper borrow (SPEC 16.3.1).
        LateHelper, // A later value that brings the helper borrow into the region of a var a Closure captured, per capture (SPEC 16.3.1).
        Contextual, // A use of contextual self or a setter's value outside a written list (SPEC 7.7.2).
        Omitted, // An omitted list's capture that is not proven Copy (SPEC 7.7.2, S3).
        Repeat, // A later entry that repeats an earlier entry's name (SPEC 7.7.2).
        ParameterRepeat, // A parameter that repeats an entry's name (SPEC 7.7.2).
    }

    // The scratch's first facts of a capture record (RecordCapture), cleared first: its Span, the part of `node` it is located at, and its
    // Reason: the problem, whether the capture is in a later default of the source's function (InLaterDefault, SPEC 7.2.3; never for a
    // record without a source) and, above them, `named` plus one: the written entry the Note names, or the omitted list's storage row,
    // -1 for none. The caller appends its kind's further facts: Related by role (0 the source's declaration, 1 the helper's borrow, 2 a
    // late source's capture site, whose entry `named` is), then an entry's Type (Actual) and the cases its bare acquisition moves in (a
    // second Reason, read by position).
    private List<ProblemFact> CaptureFacts(Koto node, CaptureProblem problem, SourceSpan at, int named, BindingSymbol? source)
    {
        var prepared = source is not null && InLaterDefault(node, source) ? 1L << 8 : 0;
        this.captureFacts.Clear();
        this.captureFacts.Add(LocatedFact(node, at));
        this.captureFacts.Add(new(ProblemFactKind.Reason, SourceRef.Of(node), (long)problem | prepared | ((long)(named + 1) << 16)));
        return this.captureFacts;
    }

    // The one recorder of a failed capture check (R7-E4 E4b): the scratch's facts (CaptureFacts), numbered (RecordNumbered), so each repeat,
    // entry and use of one node is its own problem beside its node's other failures (SPEC 23.1.2) and an equal one found again its first's.
    private void RecordCapture(Koto node, BindingFailure failure)
        => this.RecordNumbered(node, failure, CollectionsMarshal.AsSpan(this.captureFacts));

    // SPEC 16.3.1, 23.1: a capture site (a written entry, `named`, or an omitted list's use, -1) that captures the helper borrow (H) through
    // `carrier`: the captured binding's declaration related, and the value that brought H into a local region the binding names
    // (LocalRegionSourceUse), unless that value is the binding's own initializer, the related borrow.
    private void RecordHelperCapture(Koto node, SourceSpan at, int named, BindingSymbol source, (BoundOrigin Carrier, BoundOrigin Helper) helper)
    {
        var facts = this.CaptureFacts(node, CaptureProblem.Helper, at, named, source);
        facts.Add(new(ProblemFactKind.Related, this.FactSite(source.Declaration), 0));
        if (this.LocalRegionSourceUse(helper.Carrier, helper.Helper)?.Value is { } borrow && !ReferenceEquals(borrow, (source.Declaration as VariableKoto)?.InitializerKoto))
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(borrow), 1));
        }

        this.RecordCapture(node, BindingFailure.Capture);
    }

    // SPEC 7.7.2, 16.3.1, 23.1: a capture record (RecordCapture) from its facts alone. A written entry initializes its environment binding as
    // `let x = x` or `let x = x@op` would, and the report names that initialization at the entry; a bare entry of a Non-Copy binding offers
    // the transfer and the borrow as candidates (SPEC 23.1.3). In a default, the binding of a preceding argument admits neither, nor an
    // exclusive borrow (SPEC 7.2.3), so no candidate is offered. A repeat is located at the later name, with the entry it repeats related.
    private void ReportCapture(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        if (store is null)
        {
            return;
        }

        var facts = this.report.Facts(index);
        var at = this.LocatedPart(index)!.Value;
        var reason = facts[1].Value;
        var (kind, prepared, named) = ((CaptureProblem)(byte)reason, ((reason >> 8) & 1) != 0, (int)(reason >> 16) - 1);
        Koto? declaration = null;
        Koto? borrow = null;
        Koto? site = null;
        BoundType? type = null;
        var moving = SemanticsMask.None;
        for (var i = 2; i < facts.Length; i++)
        {
            switch (facts[i].Kind)
            {
                case ProblemFactKind.Related when facts[i].Value == 0:
                    declaration = this.compilation.SyntaxOf(facts[i].Site);
                    break;
                case ProblemFactKind.Related when facts[i].Value == 1:
                    borrow = this.compilation.SyntaxOf(facts[i].Site);
                    break;
                case ProblemFactKind.Related:
                    site = this.compilation.SyntaxOf(facts[i].Site);
                    break;
                case ProblemFactKind.Actual:
                    type = this.report.Value(facts[i]);
                    break;
                default:
                    moving = (SemanticsMask)facts[i].Value; // The second Reason.
                    break;
            }
        }

        // The written entry `named` is the capture site's (a late source's) or the node's own; an omitted list names its storage row.
        var list = site ?? node;
        var name = kind == CaptureProblem.Omitted ? ((FunctionKoto)node).ClosureStorage!.Storage[named].Source.Name
            : named >= 0 ? ((FunctionKoto)list).Captures![named].Name : list.ToString();
        var (requirement, code, condition) = (problem.Requirement, problem.Code, problem.Condition);
        switch (kind)
        {
            case CaptureProblem.Entry when (BindingFailure)requirement.Kind == BindingFailure.InvalidAssignment:
                var exclusive = $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would";
                node.ReportIn(store, requirement, code, note: prepared ? $"{exclusive}; {PreparedNote}" : exclusive, evidence: [name], span: at, condition: condition);
                break;
            case CaptureProblem.Entry when prepared:
                node.ReportIn(
                    store,
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type!)} is not proven Copy, and {PreparedNote}",
                    evidence: [name],
                    span: at,
                    condition: condition);
                break;
            case CaptureProblem.Entry:
                var document = node.CodeContext.SourceDocument;
                node.ReportIn(
                    store,
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type!)} is neither proven Copy nor an exclusive reference{this.FailingCaseNote(type!, moving)}",
                    evidence: [name],
                    span: at,
                    repairs:
                    [
                        new(RepairKind.Transfer, [name, "the closure's environment"], [store.Edit(new(at.End, 0), "@move", document)], RepairConditionSet.Take, RepairConditionSet.UsageLegality),
                        new(RepairKind.Borrow, [name, "the closure's environment"], [store.Edit(new(at.End, 0), "@ref", document)], RepairConditionSet.None, RepairConditionSet.UsageLegality),
                    ],
                    condition: condition);
                break;
            case CaptureProblem.Omitted:
                // SPEC 7.7.2 (R6-check H1, S3): the omitted list's capture, located at the Closure; no candidate is offered.
                var omitted = $"The omitted capture list captures {name} only by Copy; it never infers a Move, a borrow or a Reborrow";
                node.ReportIn(store, requirement, code, note: prepared ? $"{omitted}, and {PreparedNote}" : omitted, evidence: [name], span: at, condition: condition);
                break;
            case CaptureProblem.Receiver:
                // SPEC 7.7.2: an explicit capture of self obeys the construction and destruction restrictions.
                node.ReportIn(store, requirement, code, note: "In a constructor or destructor, self is reached only through its Fields, so a capture entry cannot take self (SPEC 7.7.2)", span: at, condition: condition);
                break;
            case CaptureProblem.Helper:
                // SPEC 16.3.1: the helper borrow, or a value that carries it, captured; the captured binding's declaration is related, and
                // the value that brought the helper borrow into its region when that is not its initializer.
                (string Role, Koto At, string? Label)[] related = borrow is null ? [("declaration", declaration!, null)] : [("declaration", declaration!, null), ("borrow", borrow, null)];
                node.ReportIn(store, requirement, code, note: $"{name} carries the helper borrow of the whole destruction receiver (self@ref); {HelperBorrowNote}", span: at, related: related, condition: condition);
                break;
            case CaptureProblem.LateHelper:
                // SPEC 16.3.1, 15.6.5 (O-D1 (a)): the later value, one record per capture it reaches: the captured var's declaration and
                // the capture, the written entry or the binding's first occurrence, related.
                node.ReportIn(
                    store,
                    requirement,
                    code,
                    note: $"This value carries the helper borrow of the whole destruction receiver (self@ref) into {name}, which a Closure captures; " + HelperBorrowNote,
                    span: at,
                    related: [("declaration", declaration!, null)],
                    relatedSpans: [("capture", list, named >= 0 ? ((FunctionKoto)list).Captures![named].Span : list.Span, null)],
                    condition: condition);
                break;
            case CaptureProblem.Contextual:
                // SPEC 7.7.2: an anonymous function without a capture list never captures contextual self or a setter's value; the use is
                // the location, and the declaration of the binding is related (an accessor's, for a binding its function declares). In a
                // later default of the receiver's function, an entry can neither move self nor keep a new borrow of it (SPEC 7.2.3).
                var self = name == "self";
                var contextual = self ? "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.7.2)"
                    : "A setter's value is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.7.2)";
                var declaring = declaration is FunctionKoto { Accessor.Declaration: { } accessor } ? accessor : declaration!;
                node.ReportIn(store, requirement, code, note: self && prepared ? $"{contextual}; {PreparedNote}" : contextual, span: at, related: [("declaration", declaring, null)], condition: condition);
                break;
            case CaptureProblem.Repeat or CaptureProblem.ParameterRepeat:
                // SPEC 7.7.2, 23.1.2: a repeated capture name is one problem at the later entry or parameter, with the entry it repeats related.
                var note = kind == CaptureProblem.ParameterRepeat
                    ? $"The capture entry {name} and the parameter {name} would both declare {name} in the anonymous function's body; a parameter cannot repeat a capture name (SPEC 7.7.2)"
                    : $"The capture list names {name} twice; each entry declares its own environment binding, so a name is captured once (SPEC 7.7.2)";
                node.ReportIn(store, requirement, code, note: note, evidence: [name], span: at, relatedSpans: [("declaration", node, ((FunctionKoto)node).Captures![named].Span, "capture entry")], condition: condition);
                break;
        }
    }

    // SPEC 16.3.1, 15.6.5: a var a Closure captured before a later value bounding its region brought the helper borrow (H): that value
    // fails, a node no Closure value depends on, located at itself, the value whose bound enters H into the region graph that reaches
    // the binding (LocalRegionSourceUse). Each capture it reaches is its own record (O-D1 (a)), numbered at the value, with the captured
    // var's declaration and the capture site related: the written list, whose entry of the name is the capture, or the binding's first
    // occurrence (named -1).
    private void FailLateHelperSource(FunctionKoto closure, BoundCapture capture, BoundOrigin carrier, BoundOrigin helper)
    {
        var site = closure.Captures is not null ? closure : (this.captureUse ??= new()).Find(closure, capture.Environment);
        var named = site is FunctionKoto { Captures: not null } list ? CaptureEntryIndex(list, capture.Environment.Name) : -1;
        if (this.LocalRegionSourceUse(carrier, helper)?.Value is not { } late || site is null || named is not { } entry)
        {
            // The capture site judged every carrier but a region's later source, and CarrierOf found this one through the walk that finds
            // its use; the storage entry exists because of its entry or first occurrence. A broken contract, without recovery.
            this.Fail(closure, BindingFailure.Internal);
            return;
        }

        var facts = this.CaptureFacts(late, CaptureProblem.LateHelper, late.Span, entry, capture.Source);
        facts.Add(new(ProblemFactKind.Related, this.FactSite(capture.Source.Declaration), 0));
        facts.Add(new(ProblemFactKind.Related, this.FactSite(site), 2));
        this.RecordCapture(late, BindingFailure.Capture);
    }

    // The first occurrence of an environment binding in its omitted-list Closure, in source order: a name that resolves to it, or, for
    // a nested Closure that forwards it, that Closure when its list is written (its entry of the name is the capture) or else the nested
    // binding's first occurrence. One kept walk (Binding.captureUse): Find saves and restores the binding and the found node around a
    // nested Closure's walk, so the walk is reentrant and allocates nothing.
    private sealed class CaptureUse : KotoVisitor
    {
        private BindingSymbol? environment;
        private Koto? found;

        public override void Visit(Koto node)
        {
            if (this.found is not null)
            {
                return;
            }

            if (node is FunctionKoto nested)
            {
                for (var i = 0; nested.ClosureStorage is { } plan && i < plan.Storage.Count && this.found is null; i++)
                {
                    var forwarded = plan.Storage[i];
                    if (ReferenceEquals(forwarded.Source, this.environment))
                    {
                        this.found = nested.Captures is not null ? nested : this.Find(nested, forwarded.Environment);
                    }
                }

                return;
            }

            if (ReferenceEquals(node.BoundSymbol, this.environment))
            {
                this.found = node;
                return;
            }

            node.VisitChildren(this);
        }

        internal Koto? Find(FunctionKoto closure, BindingSymbol environment)
        {
            var (savedEnvironment, savedFound) = (this.environment, this.found);
            (this.environment, this.found) = (environment, null);
            closure.VisitChildren(this);
            var found = this.found;
            (this.environment, this.found) = (savedEnvironment, savedFound);
            return found;
        }
    }
}
