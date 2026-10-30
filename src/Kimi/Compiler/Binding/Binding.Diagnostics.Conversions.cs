// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The records of a failed common Function conversion (SPEC 7.7.4, 10.7, 15.2.3): why a Function Item, closure or Function value does
// not convert to the expected Function Type, and an Owned failure's Constraint record. The conversion rules are judged in
// Binding.FunctionTypes and Binding.OwnedConversions; this adapter reads their facts without judging the conversion again.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private string? ClosureConversionNote(Koto node, object actual, object expected)
    {
        if (expected is not BoundType { Kind: BoundTypeKind.Function } signature)
        {
            return null;
        }

        // SPEC 10.5, 7.7.4: a generic reference binds its slots from the expected signature, and its Item converts only
        // when its bound arguments are Owned.
        if (actual is BoundType { Kind: BoundTypeKind.FunctionItem } item && (item.Components.Count != 0 || item.LengthArguments.Length != 0))
        {
            return this.FunctionItemSignature(item) is { } itemSignature && this.CallableSubtype(itemSignature, signature, SignatureOwner(item), out _)
                ? "Common Function conversion requires Owned bound generic arguments; this Item's arguments are not proven Owned"
                : null;
        }

        if (actual is BoundType { Kind: BoundTypeKind.Function } && node.BoundSymbol is { Kind: BindingSymbolKind.Function, Next: null, Declaration: FunctionKoto { GenericArguments.Count: > 0 } })
        {
            return ReferenceSlotNote(this.referenceSlotFacts is { } facts && facts.TryGetValue(node, out var fact) ? fact : null);
        }

        // The definition, not the closure as created: a closure that failed at a call argument or a default keeps it (SPEC 7.7.4).
        if (actual is not BoundType { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureDefinition: { } closure } declaration })
        {
            return null;
        }

        return closure.Receiver switch
        {
            SemanticsKind.Uniq => "This closure requires an Exclusive call; a common Function value permits Shared calls only",
            SemanticsKind.Owner => "This closure requires a Consuming call; a common Function value permits Shared calls only",
            _ => !this.CallableSignatureFits(closure.Signature, signature, declaration, node, out _)
                ? "The closure's parameter or result contract does not match the expected common Function signature"
                : "Common Function conversion requires an Owned environment; captured non-static borrows cannot be erased",
        };

        // SPEC 10.5, 10.8: why the one generic candidate's slots did not bind from S, when the binding recorded it.
        static string ReferenceSlotNote(ReferenceSlotFact? fact)
        {
            var prefix = fact is { } entry && ItemTypeArgumentCount(entry.Declaration) != entry.Declaration.GenericArguments.Count
                ? "The generic function's Type and length parameters are bound from the expected signature without adaptations; "
                : "The generic function's Type parameters are bound from the expected signature without adaptations; ";
            var slot = fact is { Slot: >= 0 } known && known.Slot < known.Declaration.GenericArguments.Count
                ? $"{(known.Declaration.GenericArguments[known.Slot] is LengthParameterKoto ? "length" : "Type")} parameter '{known.Declaration.GenericArguments[known.Slot].Identifier}'" : "a generic parameter";
            return fact?.Failure switch
            {
                ReferenceSlotFailure.Structure when fact.Value.Slot >= 0 => prefix + $"the signature does not bind {slot}",
                ReferenceSlotFailure.Structure => prefix + "no binding of them fits its structure",
                ReferenceSlotFailure.Constraint => prefix + "the binding fits, but it is not proven to satisfy the declaration's Constraints or to form its signature Types",
                ReferenceSlotFailure.OriginConflict when fact.Value.Explicit => prefix + $"the written Type arguments and the signature give {slot} Types that differ only in their Origins, " +
                    "and a bound Type argument holds one Origin for every call",
                ReferenceSlotFailure.OriginConflict => prefix + $"its parameters and result bind {slot} to Types that differ only in their Origins, " +
                    "and a bound Type argument holds one Origin for every call",
                ReferenceSlotFailure.InputOrigin => prefix + $"{slot} would hold an input Origin that is bound at each call, such as a per-call Origin of the signature, " +
                    "which never becomes part of a bound Type argument (SPEC 10.5)",
                _ => prefix + "no binding fits it, or a bound argument fails its Constraints",
            };
        }
    }

    // SPEC 10.7, 15.6.1, 23.1 (DECISIONS R4-M155): the one record of a whole-contract comparison that failed in a condition, from the
    // failure its judgment found: the member from the root ("the 1st parameter's 1st parameter's well-formedness"), a clause or a result
    // premise, with both ends instantiated under the comparison; a meet at the longer end of a clause or premise names its failing operand.
    private OriginContractFact ContractFact(Koto at, in ComparisonFailure failure, Koto use)
    {
        var member = string.Empty;
        for (var depth = failure.Depth - 1; depth >= 0; depth--)
        {
            var step = (byte)(failure.Path >> (8 * depth));
            member += (member.Length == 0 ? "the " : "'s ") + (step == ComparisonFailure.ResultStep ? "result" : $"{Ordinal(step)} parameter");
        }

        var instance = failure.Instance;
        switch (failure.Kind)
        {
            case ComparisonFailureKind.WellFormedness:
                var part = failure.Part < 0 ? "result's well-formedness" : $"{Ordinal(failure.Part + 1)} parameter's well-formedness";
                return new(at, (member.Length == 0 ? "the " : member + "'s ") + part, this.Instantiated(failure.Longer!, instance), this.Instantiated(failure.Shorter!, instance), false);
            case ComparisonFailureKind.Clause:
                return this.ConditionFact(at, $"the clause '{failure.Clause}'", failure.Longer!, failure.Shorter!, failure.Equality, instance, use);
            case ComparisonFailureKind.ResultPremise:
                return this.ConditionFact(at, "the result's well-formedness", failure.Longer!, failure.Shorter!, false, instance, use);
            default:
                return new(at, member, instance.Instantiate(failure.Longer!), instance.Instantiate(failure.Shorter!), failure.Equality);
        }

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 10.7, 15.6.1: whether a failed comparison of `implementation` with `required` is that comparison's one record at `node`, which
    // it then is: a condition, with the comparison's structure holding (a structural difference is a Type mismatch, even where the walk
    // met an Origin first).
    private bool RecordsCondition(Koto node, in ComparisonFailure failure, BoundType implementation, BoundType required)
    {
        if (!failure.IsCondition || !ReferenceTypes.StorageMatches(required, implementation))
        {
            return false;
        }

        this.FailExplained(ref this.originContracts, node, BindingFailure.OriginContract, this.ContractFact(node, failure, node));
        return true;
    }

    // SPEC 15.2.3, 23.1: an Owned failure of a common Function conversion is a Constraint record whose Reason names the subject, the
    // member through which the Origin enters OwnedOrigins and, when one is displayable, that Origin; a capture borrow is shown by its
    // entry and related there.
    private void ReportOwnedConversion(Koto node, OwnedConversionFact owned, DiagnosticRequirement requirement, DiagnosticCode code)
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
                ? $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} is not proven Owned") +
                "; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";
            // An exclusive reference is captured by a Reborrow, which no proof makes valid there.
            var parameter = $"give the Function Type a parameter for {owned.Member} and pass {owned.Member} where the function value is called";
            // A borrowing entry of a Copy Owned argument, such as [k@ref] of k: i32, is repaired by its bare Copy entry, [k].
        }
        else if (owned.Closure is not null)
        {
            note = origin is { } shown
                ? $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.7.4, 15.2.3); the capture {owned.Member} is not proven Owned";
        }
        else
        {
            note = $"Common Function conversion requires Owned bound generic arguments (SPEC 7.7.4, 15.2.3); the {owned.Member} is not proven Owned";
        }

        object[] evidence = origin is { } fact ? [subject, owned.Member, fact] : [subject, owned.Member];
        node.Report(requirement, code, note: note, evidence: evidence, related: related, relatedSpans: spans, at: owned.At);

        static string OriginText(DiagnosticOrigin origin) => origin.Kind switch
        {
            "borrow" => "the borrow " + origin.Text,
            "omitted" => "the omitted Origin of " + origin.Text,
            "closure" => "the closure's " + origin.Text,
            _ => origin.Text,
        };
    }
}
