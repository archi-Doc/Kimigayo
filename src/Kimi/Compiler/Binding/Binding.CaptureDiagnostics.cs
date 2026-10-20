// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture failure facts and their publication (SPEC 7.7.2, 23.1): the facts a failed capture entry, a repeated capture name or an
// implicit capture of a contextual binding records where its check fails, and the record each publishes. The judgments stay with
// their owners (Binding.Closures); this file never re-runs one to explain a failure.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The capture list failure fact beside its publication.

// The repeats of one capture list, and the first other entry that failed independently of them (SPEC 23.1.2): its failure, none
// when every other entry is valid, and the Type its facts name, null for an entry without facts, such as an unresolved name.
internal readonly record struct CaptureListFailure(CaptureRepeat[] Repeats, CaptureKoto Entry, BindingFailure Failure, BoundType? Type, BindingSymbol? Source);

public sealed partial class Binding
{
    // SPEC 7.7.2: the explicit capture entry a closure failed at, with its outer binding's Type, recorded only when it fails.
    private Dictionary<Koto, (CaptureKoto Capture, BoundType Type, BindingSymbol? Source)>? captureFailures;

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
    private void ReportCaptureEntry(Koto node, CaptureKoto capture, BoundType type, BindingSymbol? source, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = capture.Name;
        switch (code)
        {
            case DiagnosticCode.InvalidAssignment_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)", evidence: [name], span: capture.Span);
                break;
            case DiagnosticCode.InvalidAssignment_Kd:
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would", evidence: [name], span: capture.Span);
                break;
            case DiagnosticCode.TransferRequired_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is not proven Copy, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)",
                    evidence: [name],
                    span: capture.Span);
                break;
            case DiagnosticCode.TransferRequired_Kd:
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is neither proven Copy nor an exclusive reference{this.FailingCaseNote(type, node)}",
                    evidence: [name],
                    span: capture.Span,
                    repairs:
                    [
                        new(RepairKind.Transfer, [name, "the closure's environment"], [node.Edit(new(capture.Span.End, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality),
                        new(RepairKind.Borrow, [name, "the closure's environment"], [node.Edit(new(capture.Span.End, 0), "@ref")], RepairConditionSet.None, RepairConditionSet.UsageLegality),
                    ]);
                break;
            case DiagnosticCode.InvalidCaptureBinding_Kd:
                // SPEC 7.7.2: an explicit capture of self obeys the construction and destruction restrictions.
                node.Report(requirement, code, note: "In a constructor or destructor, self is reached only through its Fields, so a capture entry cannot take self (SPEC 7.7.2)", span: capture.Span);
                break;
            default:
                node.Report(requirement, code, span: capture.Span);
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
                this.ReportCaptureEntry(function, list.Entry, type, list.Source, entryRequirement, entryCode);
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
            : type is not null ? this.FailExplained(ref this.captureFailures, function, failure, (capture, type, source)) : this.Fail(function, failure);
}
