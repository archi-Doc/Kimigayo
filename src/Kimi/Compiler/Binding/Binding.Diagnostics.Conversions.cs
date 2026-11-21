// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The records of a failed common Function conversion (SPEC 7.7.4, 10.7, 15.2.3), a part of the diagnostics adapter.
// - Responsibility: a failed erasure's published outcome (erasureOutcomes); an Owned failure's Constraint record
//   (ReportOwnedConversion), and a failed condition's one record from the comparison's own failure (ContractFact, RecordCondition).
// - Inputs: the comparison's ComparisonFailure, a failed erasure's outcome (ErasureOutcome, published by
//   ErasesToFunction for its two Types) and the Owned fact (OwnedConversionFact); the conversion rules are judged in
//   Binding.FunctionTypes and Binding.OwnedConversions.
// - Outputs: the Owned record with its evidence alternative.
// - Invariants: a record is made only for a conversion that already failed.
// - Prohibitions: no new judgment of the conversion; a record reads the judgment's own outcome.
public sealed partial class Binding
{
    // SPEC 7.7.4: a failed erasure's own outcome (ErasesToFunction), keyed by its two Types (a join records from Types), for its record;
    // the last erasure of a pair in the pass wins (DECISIONS R4-M161).
    private Dictionary<(BoundType Actual, BoundType Expected), ErasureOutcome>? erasureOutcomes;

    // SPEC 7.7.4: the part of a failed erasure, in the erasure's order.
    private enum ErasurePart : byte
    {
        Receiver,
        Shape,
        Owned,
        Dependence, // The result contract depends on the receiver (ReceiverIndependence).
        Condition,
    }

    // SPEC 10.7, 15.6.1, 23.1: the one record of a whole-contract comparison that failed in a condition, from the failure its walk found:
    // the member from the nearest comparison ("the 1st parameter's 1st parameter's well-formedness"), with both ends instantiated under
    // the comparison.
    // A failed erasure's outcome for its two Types (ErasesToFunction), if one was published in this pass.
    private ErasureOutcome? ErasureOf(BoundType actual, BoundType expected)
        => this.erasureOutcomes is { } outcomes && outcomes.TryGetValue((actual, expected), out var outcome) ? outcome : null;

    private OriginContractFact ContractFact(Koto at, in ComparisonFailure failure, Koto use)
    {
        var member = string.Empty;
        for (var depth = failure.Depth - 1; depth >= 0; depth--)
        {
            var step = (byte)(failure.Path >> (8 * depth));
            member += (member.Length == 0 ? "the " : "'s ") + (step == ComparisonFailure.ResultStep ? "result" : $"{Ordinal(step)} parameter");
        }

        var instance = failure.Instance;
        if (failure.Kind == ComparisonFailureKind.Clause)
        {
            return this.ConditionFact(at, $"the clause '{failure.Clause}'", failure.Longer!, failure.Shorter!, failure.Equality, instance, use);
        }

        if (failure.Kind == ComparisonFailureKind.ResultPremise)
        {
            return this.ConditionFact(at, "the result's well-formedness", failure.Longer!, failure.Shorter!, false, instance, use);
        }

        if (failure.Kind == ComparisonFailureKind.WellFormedness)
        {
            var part = failure.Part < 0 ? "result's well-formedness" : $"{Ordinal(failure.Part + 1)} parameter's well-formedness";
            var name = (member.Length == 0 ? "the " : member + "'s ") + part;
            return new(at, name, this.Instantiated(failure.Longer!, instance), this.Instantiated(failure.Shorter!, instance), false);
        }

        return new(at, member, instance.Instantiate(failure.Longer!), instance.Instantiate(failure.Shorter!), failure.Equality);

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 10.7, 15.6.1: the record of an erasure that failed only its signature's fit (ErasesToFunction's Condition part): a condition,
    // with the comparison's structure holding, is the comparison's one record at `node`; a structural difference is left to the
    // consumer's Type mismatch (even where the walk met an Origin first), a limit is the located Unsupported_Kd (design-r2 A1), and no
    // fact a broken contract (design-r2 §2).
    private void RecordCondition(Koto node, in ComparisonFailure failure, BoundType implementation, BoundType required)
    {
        if (failure.IsCondition && TyCtxt.StorageMatches(required, implementation))
        {
            this.FailExplained(ref this.originContracts, node, BindingFailure.OriginContract, this.ContractFact(node, failure, node));
        }
        else if (failure.Kind != ComparisonFailureKind.Shape)
        {
            this.Fail(node, failure.Kind == ComparisonFailureKind.Limit ? BindingFailure.Unsupported : BindingFailure.Internal);
        }
    }

    // SPEC 15.2.3, 23.1: an Owned failure of a common Function conversion is a Constraint record whose Reason names the subject, the
    // member through which the Origin enters OwnedOrigins and, when one is displayable, that Origin; a capture borrow is shown by its
    // entry and related there.
    private void ReportOwnedConversion(Koto node, DiagnosticCollection? store, OwnedConversionFact owned, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var subject = DiagnosticTypeName(owned.Subject);
        var entry = owned.Entry is { Operation: not null and not Constants.MoveOperation } borrowEntry ? borrowEntry : (CaptureKoto?)null;
        var origin = owned.Origin is not { } failing ? (DiagnosticOrigin?)null
            : entry is { } written ? new DiagnosticOrigin("borrow", written.Name + "@" + written.Operation) : OriginDisplay(failing, owned.Borrow);

        // SPEC 23.1: a borrow end is related at its syntax: a capture entry that borrows, or the Borrow a captured binding was
        // initialized by; otherwise at the capture entry or the converted value. An omitted end is related at its Type occurrence.
        (string Role, Koto Owner, SourceSpan Span, string? Label)[]? spans = null;
        (string Role, Koto At, string? Label)[]? related = null;
        if (origin is { Kind: "borrow" or "omitted" } relatedOrigin)
        {
            if (entry is { } at && owned.Closure is { } closure)
            {
                spans = [("origin", closure, at.Span, null)];
            }
            else if (relatedOrigin.Kind == "omitted" && OmittedAt(owned.Origin!) is { } occurrence)
            {
                related = [("origin", occurrence, null)];
            }
            else if (owned.Borrow is { } borrow)
            {
                related = [("origin", borrow, null)];
            }
            else if (owned.Entry is { } named && owned.Closure is { } owner)
            {
                spans = [("origin", owner, named.Span, null)];
            }
            else
            {
                related = [("origin", owned.At, null)];
            }
        }

        string note;
        if (owned.Closure is { } converted && PreparedCapture(converted, owned.Member) is { } preparedSource)
        {
            // SPEC 7.2.3: in a default, a capture of a preceding argument can neither move it nor keep a new borrow of it, and the
            // closure always converts to the parameter's Function Type, so only a Copy that holds no borrow or a new parameter remain.
            note = (origin is { } shown
                ? "Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); " +
                    $"the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} is not proven Owned") +
                "; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";
            // An exclusive reference is captured by a Reborrow, which no proof makes valid there.
            var parameter = $"give the Function Type a parameter for {owned.Member} and pass {owned.Member} where the function value is called";
            // A borrowing entry of a Copy Owned argument, such as [k@ref] of k: i32, is repaired by its bare Copy entry, [k].
        }
        else if (owned.Closure is not null)
        {
            note = origin is { } shown
                ? "Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); " +
                    $"the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} is not proven Owned";
        }
        else
        {
            // An inherited Origin, whose member Type is the subject itself (FailOwnedConversion), is an Origin that is not static.
            note = $"Common Function conversion requires Owned bound generic arguments (SPEC 7.7.4, 15.2.3); the {owned.Member} " +
                (ReferenceEquals(owned.MemberType, owned.Subject) ? "is not static" : "is not proven Owned");
        }

        object[] evidence = origin is { } fact ? [subject, owned.Member, fact] : [subject, owned.Member];
        node.ReportIn(store, requirement, code, note: note, evidence: evidence, related: related, relatedSpans: spans, at: owned.At);

        static string OriginText(DiagnosticOrigin origin) => origin.Kind switch
        {
            "borrow" => "the borrow " + origin.Text,
            "omitted" => "the omitted Origin of " + origin.Text,
            "closure" => "the closure's " + origin.Text,
            _ => origin.Text,
        };
    }

    // A failed erasure's outcome (ErasesToFunction): its first failed part and its Owned failure.
    private readonly record struct ErasureOutcome(ErasurePart Part, OwnedFact Owned);

    // An erasure's Owned fact (R4-M161; G379 R4, OwnedMember): an Error's held cause; the explaining member (-1 none, -2 no work) with
    // its Origin; whether a part at any depth is Refuted in the forming body; and whether one is decided only once the body is bound
    // (Final: read by applicability, ErasedArgumentsEnvironment; the record keeps O-57's recorded limit).
    private readonly record struct OwnedFact(ICheckCause? Cause, int Member, BoundOrigin? Origin, bool Refuted, bool Final);
}
