// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture failure facts and their publication (SPEC 7.7.2, 16.3.1, 23.1): the facts a failed capture entry, a repeated capture name,
// an implicit capture of a contextual binding or a capture of the helper borrow records where its check fails, and the record each
// publishes. The judgments stay with their owners (Binding.Closures, the receiver table in Binding.Structs); this file never re-runs
// one to explain a failure.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The capture list failure fact beside its publication.

// The repeats of one capture list, and the first other entry that failed independently of them (SPEC 23.1.2): its failure, none
// when every other entry is valid, and the Type its facts name, null for an entry without facts, such as an unresolved name.
internal readonly record struct CaptureListFailure(CaptureRepeat[] Repeats, CaptureKoto Entry, BindingFailure Failure, BoundType? Type, BindingSymbol? Source);

public sealed partial class Binding
{
    private const string HelperBorrowNote = "the helper borrow may be passed to helpers but cannot be captured (SPEC 16.3.1)";

    // SPEC 7.7.2, 16.3.1: the capture a closure failed at, recorded only when it fails (CaptureFailure).
    private Dictionary<Koto, CaptureFailure>? captureFailures;

    // Only closures with a repeated capture name need this storage: each later entry or parameter, the entry it repeats, and an
    // independent failure of another entry.
    private Dictionary<Koto, CaptureListFailure>? captureRepeats;

    // SPEC 7.7.2: an anonymous function without a capture list never captures contextual self or a setter's value; its use is
    // the location, and the declaration of the binding is related. In a later default of the receiver's function, an entry can
    // neither move self nor keep a new borrow of it (SPEC 7.2.3), so only a Copy that holds no borrow, [self], can be offered.
    private void ReportContextualCapture(Koto node, BindingSymbol contextual, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var declaring = contextual.Declaration!;
        var self = contextual.Name == "self";
        var prepared = self && DefaultParameters.InLaterDefault(node, contextual);
        var note = self ? "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.7.2)"
            : "A setter's value is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.7.2)";
        if (prepared)
        {
            note += "; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";
        }

        node.Report(requirement, code, note: note, related: [("declaration", declaring is FunctionKoto { Accessor.Declaration: { } accessor } ? accessor : declaring, null)]);
    }

    // SPEC 7.7.2: a capture entry initializes its environment binding as `let x = x` or `let x = x@op` would. The report is
    // located at the entry and names the initialization it stands for; a bare entry of a Non-Copy binding offers the transfer
    // and the borrow as candidates (SPEC 23.1.3). In a default, the binding of a preceding argument admits neither, nor an
    // exclusive borrow (SPEC 7.2.3), so no candidate is offered.
    private void ReportCaptureEntry(Koto node, CaptureFailure capture, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = capture.Name;
        var type = capture.Type;
        var source = capture.Source;
        switch (code)
        {
            case DiagnosticCode.InvalidAssignment_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)", evidence: [name], span: capture.At);
                break;
            case DiagnosticCode.InvalidAssignment_Kd:
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would", evidence: [name], span: capture.At);
                break;
            case DiagnosticCode.TransferRequired_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is not proven Copy, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)",
                    evidence: [name],
                    span: capture.At);
                break;
            case DiagnosticCode.TransferRequired_Kd:
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is neither proven Copy nor an exclusive reference{this.FailingCaseNote(type, node)}",
                    evidence: [name],
                    span: capture.At,
                    repairs:
                    [
                        new(RepairKind.Transfer, [name, "the closure's environment"], [node.Edit(new(capture.At.End, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality),
                        new(RepairKind.Borrow, [name, "the closure's environment"], [node.Edit(new(capture.At.End, 0), "@ref")], RepairConditionSet.None, RepairConditionSet.UsageLegality),
                    ]);
                break;
            case DiagnosticCode.InvalidCaptureBinding_Kd when Receiver(source) is not null:
                // SPEC 7.7.2: an explicit capture of self obeys the construction and destruction restrictions.
                node.Report(requirement, code, note: "In a constructor or destructor, self is reached only through its Fields, so a capture entry cannot take self (SPEC 7.7.2)", span: capture.At);
                break;
            case DiagnosticCode.InvalidCaptureBinding_Kd when source is not null && capture.Captured is { } captured:
                // SPEC 16.3.1, 15.6.5: a later value of a captured var brings the helper borrow; each capture and the declaration are related.
                var reaches = $"This value carries the helper borrow of the whole destruction receiver (self@ref) into {name}, which a Closure captures; ";
                node.Report(
                    requirement,
                    code,
                    note: reaches + HelperBorrowNote,
                    span: capture.At,
                    related: [("declaration", source.Declaration, null)],
                    relatedSpans: Array.ConvertAll(captured, span => ("capture", node, span, (string?)null)));
                break;
            case DiagnosticCode.InvalidCaptureBinding_Kd when source is not null:
                // SPEC 16.3.1: the helper borrow, or a value that carries it, captured; the captured binding's declaration is related, and
                // the value that brought the helper borrow into its region when that is not its initializer.
                (string Role, Koto At, string? Label)[] related = capture.Borrow is { } borrow
                    ? [("declaration", source.Declaration, null), ("borrow", borrow, null)] : [("declaration", source.Declaration, null)];
                var carries = $"{name} carries the helper borrow of the whole destruction receiver (self@ref); ";
                node.Report(requirement, code, note: carries + HelperBorrowNote, span: capture.At, related: related);
                break;
            default:
                node.Report(requirement, code, span: capture.At);
                break;
        }
    }

    // SPEC 7.7.2, 23.1.2: a repeated capture name is one problem at the later entry or parameter, with the entry it repeats related.
    // Another entry that failed on its own is reported beside the repeats, exactly as it is in a list that repeats no name.
    private void ReportCaptureRepeats(Koto function, CaptureListFailure list, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var repeats = list.Repeats;
        if (list.Failure != BindingFailure.None)
        {
            var entryRequirement = DiagnosticRequirement.Binding(list.Failure);
            var entryCode = list.Failure switch
            {
                BindingFailure.TransferRequired => DiagnosticCode.TransferRequired_Kd,
                BindingFailure.InvalidAssignment => DiagnosticCode.InvalidAssignment_Kd,
                _ => DiagnosticCode.InvalidCaptureBinding_Kd,
            };
            if (list.Type is { } type)
            {
                this.ReportCaptureEntry(function, new(list.Entry.Span, list.Entry.Name, type, list.Source, null, null), entryRequirement, entryCode);
            }
            else
            {
                function.Report(entryRequirement, entryCode);
            }
        }

        for (var i = 0; i < repeats.Length; i++)
        {
            var repeat = repeats[i];
            var name = repeat.Name;
            var note = repeat.Parameter
                ? $"The capture entry {name} and the parameter {name} would both declare {name} in the anonymous function's body; a parameter cannot repeat a capture name (SPEC 7.7.2)"
                : $"The capture list names {name} twice; each entry declares its own environment binding, so a name is captured once (SPEC 7.7.2)";
            function.Report(
                requirement,
                code,
                note: note,
                evidence: [name],
                span: repeat.Later,
                relatedSpans: [("declaration", function, repeat.Earlier, "capture entry")],
                condition: (ushort)i);
        }
    }

    // A capture entry that failed (its facts name the Type, or none for an unresolved name), or, in a list that repeats a name, the
    // repeats with that entry's failure beside them.
    private BoundType? FailCaptureEntry(FunctionKoto function, CaptureRepeat[]? repeats, CaptureKoto capture, BindingFailure failure, BoundType? type, BindingSymbol? source)
        => repeats is not null ? this.FailExplained(ref this.captureRepeats, function, BindingFailure.Duplicate, new CaptureListFailure(repeats, capture, failure, type, source))
            : type is not null
                ? this.FailExplained(ref this.captureFailures, function, failure, new CaptureFailure(capture.Span, capture.Name, type, source, null, null))
                : this.Fail(function, failure);

    // SPEC 16.3.1: an explicit entry that captures a value carrying the helper borrow fails its Closure, as a capture of self does.
    private BoundType? FailHelperEntry(
        FunctionKoto function, CaptureRepeat[]? repeats, CaptureKoto capture, BindingSymbol source, (BoundOrigin Carrier, BoundOrigin Helper) helper)
        => repeats is not null ? this.FailCaptureEntry(function, repeats, capture, BindingFailure.Capture, source.Type, source)
            : this.FailExplained(ref this.captureFailures, function, BindingFailure.Capture, this.HelperCaptureFact(capture.Span, source, helper));

    // SPEC 16.3.1, 23.1: the facts of a capture site that captures the helper borrow (H) through `carrier`: the value that brought H into
    // a local region the binding names (LocalRegionSourceUse), unless that value is the binding's own initializer, is the related borrow.
    private CaptureFailure HelperCaptureFact(SourceSpan at, BindingSymbol source, (BoundOrigin Carrier, BoundOrigin Helper) helper)
    {
        var use = this.LocalRegionSourceUse(helper.Carrier, helper.Helper);
        return new(at, source.Name, source.Type!, source, ReferenceEquals(use, (source.Declaration as VariableKoto)?.InitializerKoto) ? null : use, null);
    }

    // SPEC 16.3.1, 15.6.5: a var a Closure captured before a later value bounding its region brought the helper borrow (H): that value
    // fails, a node no Closure value depends on, located at itself, the value whose bound enters H into the region graph that reaches
    // the binding (LocalRegionSourceUse). Each capture it reaches is related: the written entry or the binding's first occurrence.
    private void FailLateHelperSource(FunctionKoto closure, BoundCapture capture, BoundOrigin carrier, BoundOrigin helper)
    {
        var name = capture.Environment.Name;
        var captured = closure.Captures is not null ? CaptureEntryOf(closure, name)?.Span : CaptureUse.Of(closure, capture.Environment);
        if (this.LocalRegionSourceUse(carrier, helper) is not { } late || captured is not { } at)
        {
            // The capture site judged every carrier but a region's later source, and CarrierOf found this one through the walk that finds
            // its use; the storage entry exists because of its entry or first occurrence. A broken contract, without recovery.
            this.Fail(closure, BindingFailure.Internal);
            return;
        }

        if (this.captureFailures?.TryGetValue(late, out var reached) == true && reached.Captured is { } captures)
        {
            this.captureFailures[late] = reached with { Captured = [.. captures, at] };
            return;
        }

        var fact = new CaptureFailure(late.Span, name, capture.Environment.Type!, capture.Source, null, [at]);
        this.FailExplained(ref this.captureFailures, late, BindingFailure.Capture, fact);
    }

    // A capture a closure failed at: the written entry's span or the use that captures, its name, the Type its facts name, its outer
    // binding when it has one, the value that brought the helper borrow into the binding's region, and, for a late source of a var's
    // region, each capture it reaches.
    private readonly record struct CaptureFailure(SourceSpan At, string Name, BoundType Type, BindingSymbol? Source, Koto? Borrow, SourceSpan[]? Captured);

    // The first occurrence of an environment binding in its omitted-list Closure, in source order: a name that resolves to it, or the
    // entry or first occurrence of the nested Closure's binding that it forwards.
    private sealed class CaptureUse(BindingSymbol environment) : KotoVisitor
    {
        private SourceSpan? found;

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
                    if (ReferenceEquals(forwarded.Source, environment))
                    {
                        this.found = nested.Captures is not null ? CaptureEntryOf(nested, forwarded.Environment.Name)?.Span : Of(nested, forwarded.Environment);
                    }
                }

                return;
            }

            if (ReferenceEquals(node.BoundSymbol, environment))
            {
                this.found = node.Span;
                return;
            }

            node.VisitChildren(this);
        }

        internal static SourceSpan? Of(FunctionKoto closure, BindingSymbol environment)
        {
            var finder = new CaptureUse(environment);
            closure.VisitChildren(finder);
            return finder.found;
        }
    }
}
