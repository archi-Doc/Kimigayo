// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The publication of Binding's diagnostics (ReportDiagnostics), after the final analysis.
// - Responsibility: each direct issue reported with the facts its check recorded (ReportProblem or ReportIssue; at most one explanation
//   table answers for a node's first failure), each derived issue with its prerequisite keys, the non-failing records and SPEC 23.2.2's fallback for an incomplete pass without an Error.
// - Inputs: the latest pass's issues, derived issues and explanation tables; a refused library's refusal (LibraryLayer.Refusal), which
//   Core publishes. Outputs: the Binding partition's records. The library's freeze reports a refused library pass's records through
//   the same per-record steps (ReportFailures, ReportNonFailingRecords) while its tables stand.
// - Invariants: invoked after final analysis, not between Mods; each call replaces the partition's records. R7-E5 deletes ReportIssue's chain.
// - Prohibitions: no failure is decided and no node's binding state is written here; a query's memo of a final fact
//   (ReachedThroughBorrow) may be filled.
public sealed partial class Binding
{
    /// <summary>Records Binding's facts, replacing any it recorded before. Invoke after final analysis, not between Mods.</summary>
    public void ReportDiagnostics()
    {
        var diagnostics = this.compilation.Diagnostics;
        diagnostics.Invalidate(DiagnosticPartition.Binding);
        this.ReportFailures(this.issuesView, this.derivedIssuesView);
        if (this.Library.Layer.Refusal is { } refusal)
        {
            this.compilation.PublishRefusal(refusal); // A refused library's records (PLAN G949 Stage 2 U2a, C2 (4); ruling 40).
        }

        if (this.Result.Mode == BindingMode.Final)
        {
            this.ReportNonFailingRecords();

            // SPEC 23.2.2: an incomplete Binding without an Error in it or an earlier phase reports one fallback at its
            // first incomplete node. It marks a missing report, a compiler defect to repair where it occurs.
            if (!this.Result.IsComplete && !diagnostics.HasErrorsThrough(DiagnosticPartition.Binding) && this.FirstIncompleteNode() is { } first)
            {
                var failure = this.RecordedFailure(first);
                first.ReportDerivedIn(this.compilation.StoreOf(first), DiagnosticRequirement.Binding(failure == BindingFailure.None ? BindingFailure.MissingType : failure), [DiagnosticKey.Unresolved]);
            }
        }
    }

    // The target of the plain numeric conversion whose direct literal operand failed to fit, if the node is such a literal.
    private static BoundType? LiteralConversionTarget(Koto node)
    {
        if (KotoHelper.SignedNumber(node, out _) is null)
        {
            return null;
        }

        var parent = node.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        return parent is ConversionKoto { Right: not TypeSemanticsKoto { ConversionOperation: not null } } conversion ? conversion.Right.BoundType : null;
    }

    // SPEC 7.7.3: the call a closure's minimum receiver needs, and the calls a Callable receiver permits.
    private static string CallReceiverText(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => "a Shared call", SemanticsKind.Uniq => "an Exclusive call", _ => "a Consuming call" };

    private static string PermittedCallsText(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => "Shared calls only", SemanticsKind.Uniq => "Shared and Exclusive calls only", _ => "every call" };

    // The limit a Resource record of Binding names (SPEC 8.3.3 Limits, 23.1.1): each implementation limit Binding records at a node is a row.
    private static long? ImplementationLimit(BindingFailure failure)
        => failure switch { BindingFailure.TypeHeightLimit => TypeHeightLimit, BindingFailure.PremiseClosureLimit => PremiseClosureLimit, _ => null };

    // SPEC 4.3.4: a noinit directive's closed reason, with the declaration it initializes.
    private static void ReportNoInit(Koto directive, DiagnosticCollection? store, in BindingProblem problem, NoInitProblem reason)
    {
        var variable = (VariableKoto)directive.Parent!;
        var text = reason switch
        {
            NoInitProblem.LocalVar => "the declaration is not a local var",
            NoInitProblem.Holes => "the annotation contains a length or element Type hole",
            NoInitProblem.Element => $"owner Scalar is not proven for element {DiagnosticTypeName(variable.TypeKoto!.BoundType!.Components[0])}",
            _ => "the declaration requires an explicit fixed-array Type annotation",
        };
        directive.ReportIn(store, problem.Requirement, problem.Code, evidence: [text], related: [("declaration", variable.TypeKoto ?? variable.NameKoto, "declaration requiring initialization")]);
    }

    // SPEC 4.3.1: a local fixed-array annotation's closed reason, with the syntax that supplies or requires the evidence and the sides a
    // conflict compared (RecordArrayInference).
    private void ReportArrayInference(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var reason = (ArrayInferenceProblem)facts[0].Value;
        var text = reason switch
        {
            ArrayInferenceProblem.Element => "the initializer supplies no element Type for this hole",
            ArrayInferenceProblem.Length => "the initializer supplies no fixed-array length for this hole",
            ArrayInferenceProblem.LengthConflict => $"this dimension has length {DiagnosticLengthName(this.report.Length(facts[2]))} but earlier initializer evidence established {DiagnosticLengthName(this.report.Length(facts[3]))}",
            _ => $"the annotation requires {this.SideOf(facts[3])} here, but the initializer has {DiagnosticTypeName(this.report.Value(facts[2]))}",
        };
        var label = reason is ArrayInferenceProblem.Element or ArrayInferenceProblem.Length ? "initializer supplying the evidence" : "fixed-array annotation";
        node.ReportIn(store, problem.Requirement, problem.Code, evidence: [text], related: [("context", this.compilation.SyntaxOf(facts[1].Site), label)]);
    }

    // SPEC 23.2.2's fallback site: the first node whose recorded state is not Resolved, the program part's nodes first, then the library
    // layer's.
    private Koto? FirstIncompleteNode()
    {
        foreach (var node in this.nodesView)
        {
            if (this.RecordedState(node) != BindingState.Resolved)
            {
                return node;
            }
        }

        return null;
    }

    // Publishes one direct failure with the facts its check recorded. Each fact table holds the explanation of its node's one
    // failure only (FailExplained), so at most one table answers for a node.
    private void ReportIssue(BindingIssue issue)
    {
        var requirement = DiagnosticRequirement.Binding(issue.Failure);
        var store = this.compilation.StoreOf(issue.Node); // Core's store decision (PLAN G949 Stage 2 U3-3): a library node's record is this compilation's.
        if (issue.Problem >= 0 && this.ReportProblem(issue.Node, store, issue.Problem))
        {
            // Its facts are the record (Binding.Problems).
        }
        else if (issue.Failure == BindingFailure.Unsupported)
        {
            // SPEC 23.1.1: a form outside the implemented subset is located only: at the part its check recorded, a function at its
            // signature.
            var span = this.LocatedPart(issue.Problem) ??
                (issue.Node is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : (SourceSpan?)null);
            issue.Node.ReportIn(store, requirement, issue.Code, condition: this.ConditionOf(issue.Problem), span: span);
        }
        else if (issue.Failure == BindingFailure.ArithmeticConformance && this.arithmeticConformanceFailures.TryGetValue(issue.Node, out var arithmetic))
        {
            ReportArithmeticConformance(issue.Node, requirement, issue.Code, arithmetic);
        }
        else if (issue.Failure == BindingFailure.ArithmeticSelection && this.arithmeticSelectionFailures?.TryGetValue(issue.Node, out var selection) == true)
        {
            this.ReportArithmeticSelection(issue.Node, store, requirement, selection);
        }
        else if (issue.Failure == BindingFailure.ArithmeticDirection && this.arithmeticDirectionFailures?.TryGetValue(issue.Node, out var direction) == true)
        {
            issue.Node.ReportIn(
                store,
                requirement,
                issue.Code,
                ArithmeticOperatorText(ArithmeticContractSyntax.Operator(direction.Id)),
                evidence: [DiagnosticTypeName(direction.Left), DiagnosticTypeName(direction.Right), direction.Ordinary.Name, direction.Reverse.Name],
                related: [("ordinary", direction.OrdinaryUse, direction.Ordinary.Name), ("reverse", direction.ReverseUse, direction.Reverse.Name)]);
        }
        else if (issue.Failure == BindingFailure.CaseLimit)
        {
            this.ReportCaseLimit(issue.Node, store, requirement);
        }
        else if (ImplementationLimit(issue.Failure) is { } limit)
        {
            issue.Node.ReportIn(store, requirement, issue.Code, limit);
        }
        else if (issue.Failure == BindingFailure.QualificationRequired)
        {
            this.ReportQualification(issue.Node, store, requirement);
        }
        else if (issue.Code == DiagnosticCode.NonExhaustiveMatch_Kd)
        {
            this.ReportMatchCoverage((MatchKoto)issue.Node, store, requirement);
        }
        else if (issue.Code == DiagnosticCode.InvalidNumericLiteral_Kd && LiteralConversionTarget(issue.Node) is { } literalTarget)
        {
            // SPEC 13.5.4: a direct literal is converted at compile time, so its range failure is explained at the literal.
            var truncated = (issue.Node as NumberLiteralKoto ?? ((UnaryKoto)issue.Node).Operand) is NumberLiteralKoto { IsInteger: false };
            issue.Node.ReportIn(store, requirement, issue.Code, note: $"The direct literal is converted at compile time and its {(truncated ? "truncated " : string.Empty)}value is outside the range of {DiagnosticTypeName(literalTarget)} (SPEC 13.5.4)");
        }
        else if (issue.Code == DiagnosticCode.InvalidTry_Kd)
        {
            var (code, evidence, note) = this.TryFailure(issue.Node);
            issue.Node.ReportIn(store, requirement, code, note: note, evidence: evidence);
        }
        else if (issue.Code == DiagnosticCode.InvalidTypeFormation_Kd && issue.Node is GenericsKoto { BoundSymbol.LibraryDeclaration: KimiDeclarationId.Loan })
        {
            // SPEC 15.3.5: the formation condition of Loan<T>.
            issue.Node.ReportIn(
                store,
                requirement,
                issue.Code,
                note: "Loan<T> keeps the dependency of a borrow value, so T must be a complete ref, uniq, objref or objuniq borrow Type");
        }
        else if (issue.Code == DiagnosticCode.NotObjectPayload_Kd)
        {
            // FailObjectPayload records the declaring Type before it fails the use.
            issue.Node.ReportIn(store, requirement, issue.Code, this.objectPayloadCauses![issue.Node].Name);
        }
        else if (this.ReportCallableSelection(issue.Node, store, requirement, issue.Code))
        {
        }
        else if (issue.Code is DiagnosticCode.NoApplicableOverload_Kd or DiagnosticCode.AmbiguousBinding_Kd && this.rejectedCandidates?.TryGetValue(issue.Node, out var range) == true)
        {
            var rejected = CollectionsMarshal.AsSpan(this.rejectedRows).Slice(range.Start, range.Count);
            var relatedCount = rejected.Length;
            foreach (var candidate in rejected)
            {
                relatedCount += candidate.ConstraintFailure is null ? 0 : 1;
            }

            var candidates = new (string Role, Koto At, string? Label)[relatedCount];
            var nextConstraint = rejected.Length;
            string? shapeNote = issue.Code == DiagnosticCode.AmbiguousBinding_Kd ? "No candidate is better than every other remaining candidate under the argument, parameter Type, generic and default ranking rules. Anonymous bodies, captures and waiting function references do not select an overload" : null;
            var referenceHasGenericCandidate = false;
            for (var c = 0; c < rejected.Length; c++)
            {
                var candidate = rejected[c];
                var label = candidate.Function.Name;
                if (candidate.ReferenceSignature && issue.Code == DiagnosticCode.AmbiguousBinding_Kd)
                {
                    shapeNote = "No function reference candidate is better than every other fitting candidate under the parameter Type and generic ranking rules. Results do not rank candidates";
                }

                if (candidate.UnfixedReference)
                {
                    // SPEC 10.5: without a fixed expected call signature, an overload set is not a value.
                    shapeNote = "A function reference without a fixed expected call signature is a value only when exactly one candidate remains and its Type parameters are bound";
                    referenceHasGenericCandidate |= candidate.Function.GenericArguments.Count != 0;
                    candidates[c] = ("candidate", candidate.Function, candidate.Actual is { } signature ? $"{label}: callable signature {DiagnosticText.Bound(DiagnosticTypeName(signature), 48).Text}" : label);
                    continue;
                }

                if (candidate.ErasureIncomparable && issue.Code == DiagnosticCode.AmbiguousBinding_Kd)
                {
                    shapeNote = ErasureAmbiguityNote;
                }

                if (candidate.Selected)
                {
                    shapeNote = "This declaration was selected before completing its callable arguments; its argument constraints failed. Another overload is not selected";
                    if (candidate.ActualReceiver is { } actualReceiver && candidate.RequiredReceiver is { } requiredReceiver)
                    {
                        label = $"{candidate.Function.Name}: closure requires {actualReceiver.ToString().ToLowerInvariant()}; Callable requires {requiredReceiver.ToString().ToLowerInvariant()}";
                        shapeNote = $"{label}. {shapeNote}";
                    }
                }
                else if (candidate.ActualReceiver is { } closureReceiver && candidate.RequiredReceiver is { } callableReceiver &&
                    (uint)candidate.ReceiverParameter < (uint)candidate.Function.Parameters.Count)
                {
                    // SPEC 7.7.3, 8.10: the closure argument's minimum call receiver is the candidate's only refuted condition
                    // (TryCandidate).
                    var parameter = candidate.Function.Parameters[candidate.ReceiverParameter];
                    var pattern = parameter.Type.BoundType;
                    var slot = (pattern is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? pattern.Components[0] : pattern)?.Symbol?.Name ?? "F";
                    var needed = closureReceiver.ToString().ToLowerInvariant();
                    label = $"{candidate.Function.Name}: closure requires {needed}; Callable requires {callableReceiver.ToString().ToLowerInvariant()}";
                    shapeNote ??= $"{label}. The closure argument of {parameter.InternalName} needs {CallReceiverText(closureReceiver)}, and the Callable Constraint of {slot} permits {PermittedCallsText(callableReceiver)} (SPEC 7.7.3, 8.10)";
                }

                if (candidate.Actual is { } actual && candidate.Expected is { } expected)
                {
                    var (shownActual, shownExpected) = DiagnosticText.BoundPair(DiagnosticTypeName(actual), DiagnosticTypeName(expected));
                    label = candidate.ReferenceSignature && issue.Code == DiagnosticCode.AmbiguousBinding_Kd ? $"{candidate.Function.Name}: callable signature {shownActual.Text}"
                        : candidate.CallableSignature ? $"{candidate.Function.Name}: callable signature is {shownActual.Text}; requires {shownExpected.Text}"
                        : candidate.SharedReceiver ? $"{candidate.Function.Name}: receiver has {shownActual.Text}; requires {shownExpected.Text}"
                        : $"{candidate.Function.Name}: argument has {shownActual.Text}; parameter requires {shownExpected.Text}";
                    // Keep the compared Types even when the related-location limit omits this candidate.
                    shapeNote ??= candidate.ReferenceSignature ? $"The function reference requires the fixed call signature {shownExpected.Text}; no candidate applies"
                        : candidate.CallableSignature ? $"The argument's known call signature is {shownActual.Text}; the candidate requires {shownExpected.Text} from the supplied Type evidence"
                        : candidate.SharedReceiver ? $"{SharedObjectAuthorityNote}. Receiver: {DiagnosticText.Bound(DiagnosticTypeName(actual), 48).Text}; required: {DiagnosticText.Bound(DiagnosticTypeName(expected), 48).Text}"
                        : $"The range argument has {shownActual.Text}; a candidate parameter requires {shownExpected.Text}";
                }

                if (candidate.ConstraintFailure is { } condition)
                {
                    var subject = ClauseSubject(condition.Constraint) is { } type ? DiagnosticTypeName(type) : "the supplied bindings";
                    var requirer = ForeignClauseOwner(condition.Clause, candidate.Function) is { } owner
                        ? $"{candidate.Function.Name}: {owner}" : candidate.Function.Name;
                    var conditionNote = $"{requirer} requires {condition.Clause}; this condition is refuted for {subject}";
                    if (this.InheritedSelfMismatchNote(condition.Constraint) is { } inherited)
                    {
                        conditionNote += $". {inherited}";
                    }

                    label = conditionNote;
                    shapeNote ??= conditionNote;
                    candidates[nextConstraint++] = ("constraint", condition.Clause, conditionNote);
                }

                candidates[c] = ("candidate", candidate.Function, label);
            }

            // A synthesized formatting write spans its whole literal; its failure is located at the value it writes, so the
            // writes of one literal are distinct problems at distinct locations (SPEC 23.3, 23.1).
            var at = issue.Node is InvocationKoto { Method: FormattingKoto or GenericsKoto { Identifier: FormattingKoto }, ArgumentNodes: [_, var value] } ? value : null;
            issue.Node.ReportIn(store, requirement, issue.Code, evidence: [rejected.Length], related: candidates, note: shapeNote, at: at);
        }
        else if (this.callInferenceFailures.TryGetValue(issue.Node, out var callInference))
        {
            this.ReportCallInferenceFailure(issue.Node, store, requirement, issue.Code, callInference);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && this.unboundSlots.TryGetValue(issue.Node, out var unboundSlot))
        {
            // SPEC 10.6, 10.8: the selected call's slot that no evidence binds, with the waiting argument whose signature holds it.
            ReportUnboundSlots(issue.Node, unboundSlot, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && KotoHelper.UnwrapParentheses(issue.Node).BoundSymbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
        {
            // SPEC 10.5: the generic parameter that no expected call signature or explicit argument binds.
            var lengths = ItemTypeArgumentCount(generic) != generic.GenericArguments.Count;
            var note = lengths ? "A function reference without a fixed expected call signature binds its Type and length parameters only from a complete explicit argument list (SPEC 10.5)" : UnboundReferenceNote;
            issue.Node.ReportIn(store, requirement, issue.Code, evidence: [generic.GenericArguments[0].Identifier], related: [("declaration", generic, null)], note: note);
        }
        else if (issue.Code == DiagnosticCode.BoundMethodValue_Kd && KotoHelper.UnwrapParentheses(issue.Node) is { BoundSymbol.Declaration: FunctionKoto method })
        {
            // SPEC 7.3: the method named through a value; its declaration shows the receiver it would need.
            issue.Node.ReportIn(store, requirement, issue.Code, evidence: [method.Name], related: [("declaration", method, null)]);
        }
        else if (issue.Code == DiagnosticCode.InvalidBaseCall_Kd && issue.Node is BaseReferenceKoto baseReference)
        {
            this.ReportBaseCallFailure(baseReference, store, requirement, issue.Code);
        }
        else if (issue.Code is DiagnosticCode.MissingOriginBinding_Kd or DiagnosticCode.InvalidOriginBinding_Kd && this.ReportFormationCause(issue.Node, store, requirement, issue.Code))
        {
            // SPEC 8.1.2: a pair occurrence's Language cause, at the occurrence.
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallSlots?.TryGetValue(issue.Node, out var perCall) == true)
        {
            ReportPerCallSlot(issue.Node, perCall, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallOrigins.TryGetValue(issue.Node, out var perCallOrigin))
        {
            ReportPerCallOrigin(issue.Node, perCallOrigin, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.ReportUndeclaredStorageOrigin(issue.Node, store, requirement, issue.Code))
        {
            // SPEC 15.3.2: an undeclared storage name, at the name.
        }
        else if (issue.Code == DiagnosticCode.InvalidOriginBinding_Kd && this.ReportAbsentSlot(issue.Node, store, requirement, issue.Code))
        {
            // SPEC 15.3.2: a projection of a slot its Type does not declare, at the slot name.
        }
        else if (issue.Code is DiagnosticCode.TransferRequired_Kd or DiagnosticCode.ExclusiveBorrowRequired_Kd or DiagnosticCode.DefaultArgumentMove_Kd &&
            this.acquisitionPlaces?.TryGetValue(issue.Node, out var acquisition) == true)
        {
            this.ReportAcquisition(issue.Node, store, acquisition.Place, acquisition.Object, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidCaptureBinding_Kd && issue.Node is IdentifierNameKoto { BoundSymbol: { } contextual } && IsContextualBinding(contextual))
        {
            this.ReportContextualCapture(issue.Node, store, contextual, requirement, issue.Code);
        }
        else if (this.ReportOriginRecords(issue.Node, issue.Code, requirement))
        {
            // SPEC 15.6.1, 23.1.2: a node's Origin relation or contract record, then its records beyond the first.
        }
        else if (issue.Code == DiagnosticCode.InvalidOriginBinding_Kd && this.ReportInvalidOriginSlot(issue.Node, store, requirement, issue.Code))
        {
            // SPEC 15.2.3, 5.7: an Origin slot the written binding cannot supply, at the annotation.
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && issue.Node is TypeSemanticsKoto { BindingSetName: { } reusedSet } bindingSet)
        {
            this.ReportDuplicateBindingSet(bindingSet, store, reusedSet, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && this.captureRepeats?.TryGetValue(issue.Node, out var repeats) == true)
        {
            this.ReportCaptureRepeats(issue.Node, store, repeats, requirement, issue.Code);
        }
        else if (this.captureFailures?.TryGetValue(issue.Node, out var entry) == true)
        {
            this.ReportCaptureEntry(issue.Node, store, entry, requirement, issue.Code);
        }
        else if (this.writeTargets?.TryGetValue(issue.Node, out var target) == true)
        {
            this.ReportWrite(issue.Node, store, target, requirement, issue.Code);
        }
        else if (this.takeLayers?.TryGetValue(issue.Node, out var takeCases) == true)
        {
            this.ReportTake(issue.Node, store, takeCases, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.TypeMismatch_Kd && issue.Problem >= 0)
        {
            // The subject stays the failed node; the location is the syntax that shows the two Types.
            var mismatch = this.Mismatch(issue.Problem);
            issue.Node.ReportIn(store, requirement, issue.Code, at: mismatch.At, evidence: [DiagnosticTypeName(mismatch.Actual), DiagnosticTypeName(mismatch.Expected)], condition: this.ConditionOf(issue.Problem));
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.objectErasureFailures.TryGetValue(issue.Node, out var erasure))
        {
            var subject = DiagnosticTypeName(erasure.Source);
            var viewTarget = DiagnosticTypeName(erasure.Target);
            issue.Node.ReportIn(store, requirement, issue.Code, note: $"Erasing {subject} to the {viewTarget} object View requires {subject} is Owned", evidence: issue.Code == DiagnosticCode.UnprovenConstraint_Kd ? [subject, "Owned"] : null);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.ownedConversions?.TryGetValue(issue.Node, out var owned) == true)
        {
            this.ReportOwnedConversion(issue.Node, store, owned, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.pendingJudgments?.TryGetValue(issue.Node, out var pending) == true)
        {
            ReportPendingJudgment(issue.Node, pending, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.callableConstraints.TryGetValue(issue.Node, out var callable))
        {
            // SPEC 8.3, 15.6.1: a Callable proof that is Unknown only in its Origin part, at the argument whose Type binds F.
            ReportCallableConstraint(issue.Node, callable, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnsatisfiedConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var rangeFailure) == true)
        {
            var subject = rangeFailure.Subject;
            issue.Node.ReportIn(store, requirement, issue.Code, note: "Range iteration requires both boundaries to have the same integer Type", evidence: [DiagnosticTypeName(subject), rangeFailure.Entry.Name, DiagnosticTypeName(subject.Components[0]), DiagnosticTypeName(subject.Components[1])]);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var unproven) == true)
        {
            // SPEC 4.6.4: the boundary Types are not known to be one integer Type in this generic context.
            var start = DiagnosticTypeName(unproven.Subject.Components[0]);
            var end = DiagnosticTypeName(unproven.Subject.Components[1]);
            issue.Node.ReportIn(store, requirement, issue.Code, note: $"Range iteration requires both boundaries to have one integer Type; the boundary Types {start} and {end} are not proven to be one integer Type");
        }
        else if (issue.Code == DiagnosticCode.InaccessibleBinding_Kd && issue.Node is MemberAccessKoto { BoundSymbol: { } inaccessible })
        {
            issue.Node.ReportIn(store, requirement, issue.Code, related: [("declaration", inaccessible.Declaration, $"inaccessible declaration '{inaccessible.Name}'")]);
        }
        else if (issue.Failure == BindingFailure.InvalidConstraint)
        {
            // A plain Fail(node, InvalidConstraint) keeps no problem until its site records one (R7-E3's call sites; until R7-E5,
            // Constraints.Requirements' cause-less branch and Contracts' invalid Contract, which read the node's own earlier failure or a
            // failed part); it reaches the adapter's one InvalidConstraint row (F's C2 ruling 306).
            this.ReportConstraint(issue.Node, store, requirement, issue.Code, 0, null);
        }
        else
        {
            issue.Node.ReportIn(store, requirement, issue.Code, note: this.OmittedResultNote(issue.Node), evidence: issue.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.TransferRequired_Kd ? [issue.Node.ToString()] : null);
        }
    }

    // Each failure record of a pass's lists through its per-record step (ReportIssue, ReportDerived), and the pass's siteless height
    // refusal: ReportDiagnostics over the composed views, and the library's freeze over a refused library pass's own lists while its
    // explanation tables stand (PLAN G949 Stage 2 U2a, C1). Generic, so a composed view is read without boxing.
    private void ReportFailures<TIssues, TDerived>(TIssues issues, TDerived derivedIssues)
        where TIssues : IReadOnlyList<BindingIssue>
        where TDerived : IReadOnlyList<BindingIssue>
    {
        if (this.sitelessHeightRefusal)
        {
            this.compilation.Diagnostics.Report(DiagnosticPartition.Binding, DiagnosticCode.InternalInvariant_Kd, this.compilation.Project.FilePath); // Binding.TypeHeight.cs
        }

        for (var i = 0; i < issues.Count; i++)
        {
            this.ReportIssue(issues[i]);
        }

        this.ReportResolvedOriginRecords();
        for (var i = 0; i < derivedIssues.Count; i++)
        {
            var issue = derivedIssues[i];
            issue.Node.ReportDerivedIn(this.compilation.StoreOf(issue.Node), DiagnosticRequirement.Binding(issue.Failure), this.PrerequisiteKeys(issue.Node), this.ConditionOf(issue.Problem));
        }
    }
}
