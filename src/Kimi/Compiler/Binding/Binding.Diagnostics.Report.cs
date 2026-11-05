// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The publication of Binding's diagnostics (ReportDiagnostics), after the final analysis.
// - Responsibility: each direct issue reported with the facts its check recorded (ReportIssue; at most one explanation table answers
//   for a node), each derived issue with its prerequisite keys, the duplicate declaration groups, the non-failing records and SPEC
//   23.2.2's fallback for an incomplete pass without an Error.
// - Inputs: the latest pass's issues, derived issues and explanation tables. Outputs: the Binding partition's records.
// - Invariants: invoked after final analysis, not between Mods; each call replaces the partition's records. R7-E5 deletes ReportIssue's chain.
// - Prohibitions: no failure is decided and no node's binding state is written here.
public sealed partial class Binding
{
    /// <summary>Records Binding's facts, replacing any it recorded before. Invoke after final analysis, not between Mods.</summary>
    public void ReportDiagnostics()
    {
        var diagnostics = this.compilation.Diagnostics;
        diagnostics.Invalidate(DiagnosticPartition.Binding);
        for (var i = 0; i < this.issues.Count; i++)
        {
            var issue = this.issues[i];
            if (issue.Failure == BindingFailure.Duplicate && this.duplicateDeclarations?.ContainsKey(issue.Node) == true)
            {
                continue; // Pairwise declaration failures are normalized as one group below.
            }

            this.ReportIssue(issue);
        }

        foreach (var node in this.derivedIssues)
        {
            node.ReportDerived(DiagnosticRequirement.Binding(node.BindingFailure), this.PrerequisiteKeys(node));
        }

        if (this.Result.Mode == BindingMode.Final)
        {
            this.ReportDuplicateDeclarations();
            this.ReportNonFailingRecords();

            // SPEC 23.2.2: an incomplete Binding without an Error in it or an earlier phase reports one fallback at its
            // first incomplete node. It marks a missing report, a compiler defect to repair where it occurs.
            if (!this.Result.IsComplete && !diagnostics.HasErrorsThrough(DiagnosticPartition.Binding) &&
                this.nodes.Find(static x => x.BindingState != BindingState.Resolved) is { } first)
            {
                first.ReportDerived(DiagnosticRequirement.Binding(first.BindingFailure == BindingFailure.None ? BindingFailure.MissingType : first.BindingFailure), [DiagnosticKey.Unresolved]);
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

    // Publishes one direct failure with the facts its check recorded. Each fact table holds the explanation of its node's one
    // failure only (FailExplained), so at most one table answers for a node.
    private void ReportIssue(BindingIssue issue)
    {
        var requirement = DiagnosticRequirement.Binding(issue.Failure);
        if (issue.Failure == BindingFailure.Unsupported)
        {
            // SPEC 23.1.1: a form outside the implemented subset is located only: at the part its check recorded, a function at its
            // signature.
            var span = this.unsupportedSpans?.TryGetValue(issue.Node, out var part) == true ? part
                : issue.Node is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : (SourceSpan?)null;
            issue.Node.Report(requirement, issue.Code, span: span);
        }
        else if (issue.Failure == BindingFailure.ArithmeticConformance && this.arithmeticConformanceFailures?.TryGetValue(issue.Node, out var arithmetic) == true)
        {
            issue.Node.Report(requirement, issue.Code, DiagnosticTypeName(arithmetic.Type), arithmetic.Contract.Name, evidence: [arithmetic.Condition], related: [("contract", arithmetic.Contract.Declaration, "arithmetic Contract declaration")]);
        }
        else if (issue.Failure == BindingFailure.ArithmeticSelection && this.arithmeticSelectionFailures?.TryGetValue(issue.Node, out var selection) == true)
        {
            this.ReportArithmeticSelection(issue.Node, requirement, selection);
        }
        else if (issue.Failure == BindingFailure.ArithmeticDirection && this.arithmeticDirectionFailures?.TryGetValue(issue.Node, out var direction) == true)
        {
            issue.Node.Report(requirement, issue.Code, ArithmeticOperatorText(ArithmeticContracts.Operator(direction.Id)), evidence: [DiagnosticTypeName(direction.Left), DiagnosticTypeName(direction.Right), direction.Ordinary.Name, direction.Reverse.Name], related: [("ordinary", direction.OrdinaryUse, direction.Ordinary.Name), ("reverse", direction.ReverseUse, direction.Reverse.Name)]);
        }
        else if (issue.Failure == BindingFailure.CaseLimit)
        {
            this.ReportCaseLimit(issue.Node, requirement);
        }
        else if (issue.Failure == BindingFailure.QualificationRequired)
        {
            this.ReportQualification(issue.Node, requirement);
        }
        else if (issue.Code == DiagnosticCode.InvalidConstraint_Kd &&
            this.constraintDiagnosticCauses?.TryGetValue(issue.Node, out var cause) == true &&
            cause.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported)
        {
            // The recorded missing Name of the Constraint is its prerequisite.
            issue.Node.ReportDerived(requirement, [cause.KeyOf(DiagnosticRequirement.Binding(cause.BindingFailure))]);
        }
        else if (issue.Failure == BindingFailure.NoInit)
        {
            var variable = (VariableKoto)issue.Node.Parent!;
            var reason = this.noInitFailures![issue.Node] switch
            {
                NoInitProblem.LocalVar => "the declaration is not a local var",
                NoInitProblem.Holes => "the annotation contains a length or element Type hole",
                NoInitProblem.Element => $"owner Scalar is not proven for element {DiagnosticTypeName(variable.TypeKoto!.BoundType!.Components[0])}",
                _ => "the declaration requires an explicit fixed-array Type annotation",
            };
            issue.Node.Report(
                requirement,
                issue.Code,
                evidence: [reason],
                related: [("declaration", variable.TypeKoto ?? variable.NameKoto, "declaration requiring initialization")],
                note: "noinit skips stores but completes construction; the programmer must write a valid value before any element read, including a whole-array Copy or Move");
        }
        else if (issue.Failure == BindingFailure.ArrayAnnotationInference)
        {
            var fact = this.arrayInferenceFailures![issue.Node];
            var reason = fact.Problem switch
            {
                ArrayInferenceProblem.Element => "the initializer supplies no element Type for this hole",
                ArrayInferenceProblem.Length => "the initializer supplies no fixed-array length for this hole",
                ArrayInferenceProblem.LengthConflict => $"this dimension has length {DiagnosticLengthName(fact.Actual!)} but earlier initializer evidence established {DiagnosticLengthName(fact.Expected!)}",
                _ => $"the annotation requires a fixed array here, but the initializer has {DiagnosticTypeName(fact.Type!)}",
            };
            issue.Node.Report(
                requirement,
                issue.Code,
                evidence: [reason],
                related: [("context", fact.Related, fact.Problem is ArrayInferenceProblem.Element or ArrayInferenceProblem.Length ? "initializer supplying the evidence" : "fixed-array annotation")]);
        }
        else if (issue.Code == DiagnosticCode.NonExhaustiveMatch_Kd)
        {
            this.ReportMatchCoverage((MatchKoto)issue.Node, requirement);
        }
        else if (issue.Code == DiagnosticCode.InvalidNumericLiteral_Kd && LiteralConversionTarget(issue.Node) is { } literalTarget)
        {
            // SPEC 13.5.4: a direct literal is converted at compile time, so its range failure is explained at the literal.
            var truncated = (issue.Node as NumberLiteralKoto ?? ((UnaryKoto)issue.Node).Operand) is NumberLiteralKoto { IsInteger: false };
            issue.Node.Report(requirement, issue.Code, note: $"The direct literal is converted at compile time and its {(truncated ? "truncated " : string.Empty)}value is outside the range of {DiagnosticTypeName(literalTarget)} (SPEC 13.5.4)");
        }
        else if (issue.Code == DiagnosticCode.InvalidTry_Kd)
        {
            var (code, evidence, note) = this.TryFailure(issue.Node);
            issue.Node.Report(requirement, code, note: note, evidence: evidence);
        }
        else if (issue.Code == DiagnosticCode.InvalidTypeFormation_Kd && issue.Node is GenericsKoto { BoundSymbol.LibraryDeclaration: KimiDeclarationId.Loan })
        {
            // SPEC 15.3.5: the formation condition of Loan<T>.
            issue.Node.Report(
                requirement,
                issue.Code,
                note: "Loan<T> keeps the dependency of a borrow value, so T must be a complete ref, uniq, objref or objuniq borrow Type");
        }
        else if (issue.Code == DiagnosticCode.NotObjectPayload_Kd)
        {
            // FailObjectPayload records the declaring Type before it fails the use.
            issue.Node.Report(requirement, issue.Code, this.objectPayloadCauses![issue.Node].Name);
        }
        else if (this.ReportCallableSelection(issue.Node, requirement, issue.Code))
        {
        }
        else if (issue.Code == DiagnosticCode.AmbiguousBinding_Kd && this.associatedAmbiguities?.TryGetValue(issue.Node, out var associated) == true)
        {
            var first = $"{DiagnosticTypeName(this.ProjectionContract(associated.First.Symbol, associated.First.Contract))}.{associated.First.Symbol.Name}";
            var second = $"{DiagnosticTypeName(this.ProjectionContract(associated.Second.Symbol, associated.Second.Contract))}.{associated.Second.Symbol.Name}";
            var head = AssociatedHead(issue.Node);
            issue.Node.Report(
                requirement,
                issue.Code,
                at: head is OriginApplicationKoto application ? application.Type : head,
                evidence: [associated.Count],
                related: [("candidate", associated.First.Symbol.Declaration, first), ("candidate", associated.Second.Symbol.Declaration, second)],
                note: $"{first} and {second} are distinct associated-Type identities; the declaring Contract's bindings are part of each identity (SPEC 8.4.3-3)");
        }
        else if (issue.Code is DiagnosticCode.NoApplicableOverload_Kd or DiagnosticCode.AmbiguousBinding_Kd && this.rejectedCandidates?.TryGetValue(issue.Node, out var rejected) == true)
        {
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
            issue.Node.Report(requirement, issue.Code, evidence: [rejected.Length], related: candidates, note: shapeNote, at: at);
        }
        else if (this.callInferenceFailures?.TryGetValue(issue.Node, out var callInference) == true)
        {
            this.ReportCallInferenceFailure(issue.Node, requirement, issue.Code, callInference);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && this.unboundSlots?.TryGetValue(issue.Node, out var unboundSlot) == true)
        {
            // SPEC 10.6, 10.8: the selected call's slot that no evidence binds, with the waiting argument whose signature holds it.
            ReportUnboundSlots(issue.Node, unboundSlot, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && KotoHelper.UnwrapParentheses(issue.Node).BoundSymbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
        {
            // SPEC 10.5: the generic parameter that no expected call signature or explicit argument binds.
            var lengths = ItemTypeArgumentCount(generic) != generic.GenericArguments.Count;
            var note = lengths ? "A function reference without a fixed expected call signature binds its Type and length parameters only from a complete explicit argument list (SPEC 10.5)" : UnboundReferenceNote;
            issue.Node.Report(requirement, issue.Code, evidence: [generic.GenericArguments[0].Identifier], related: [("declaration", generic, null)], note: note);
        }
        else if (issue.Code == DiagnosticCode.BoundMethodValue_Kd && KotoHelper.UnwrapParentheses(issue.Node) is { BoundSymbol.Declaration: FunctionKoto method })
        {
            // SPEC 7.3: the method named through a value; its declaration shows the receiver it would need.
            issue.Node.Report(requirement, issue.Code, evidence: [method.Name], related: [("declaration", method, null)]);
        }
        else if (issue.Code == DiagnosticCode.ParameterShapeMismatch_Kd && this.parameterShapeConflicts.TryGetValue(issue.Node, out var shapes))
        {
            this.ReportParameterShapes(issue.Node, requirement, shapes);
        }
        else if (issue.Code == DiagnosticCode.AccessorReceiverShape_Kd && issue.Node is PropertyAccessorKoto { ReceiverType: { } writtenReceiver } shapedAccessor)
        {
            this.ReportAccessorReceiverShape(shapedAccessor, writtenReceiver, requirement);
        }
        else if (issue.Code is DiagnosticCode.MissingOverrideTarget_Kd or DiagnosticCode.AmbiguousOverrideTarget_Kd or DiagnosticCode.OverrideContractMismatch_Kd or DiagnosticCode.DuplicateOverride_Kd or DiagnosticCode.UnprovenOverrideCondition_Kd && issue.Node is FunctionKoto overrideFunction)
        {
            this.ReportOverrideFailure(overrideFunction, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidVirtualDeclaration_Kd && issue.Node is FunctionKoto virtualFunction)
        {
            virtualFunction.Report(requirement, issue.Code, span: virtualFunction.DispatchModifierSpan, evidence: [this.virtualDeclarationFailures![virtualFunction]], related: [("declaration", virtualFunction, "the virtual or override declaration")]);
        }
        else if (issue.Code == DiagnosticCode.InvalidBaseCall_Kd && issue.Node is BaseReferenceKoto baseReference)
        {
            baseReference.Report(requirement, issue.Code, evidence: [this.baseCallFailures![baseReference]]);
        }
        else if (issue.Code == DiagnosticCode.ProtectedPlacement_Kd)
        {
            ReportProtectedPlacement(issue.Node, requirement);
        }
        else if (issue.Code == DiagnosticCode.InvalidEffectBound_Kd && issue.Node is EffectBoundKoto effect)
        {
            this.ReportEffectBound(effect, requirement);
        }
        else if (issue.Code == DiagnosticCode.AssociatedTypeInferenceFailed_Kd && this.ReportAssociatedInference(issue.Node, requirement))
        {
            // Declaration evidence is retained by the common binder, independent of the adapter.
        }
        else if (issue.Code is DiagnosticCode.IncompatibleContractImplementation_Kd or DiagnosticCode.InvalidAssociatedType_Kd && this.contractAgreementFailures?.TryGetValue(issue.Node, out var agreement) == true)
        {
            var first = DiagnosticTypeName(agreement.Left.Type!);
            var second = DiagnosticTypeName(agreement.Right.Type!);
            object[] evidence = agreement.AssociatedLeft is { } a
                ? [first, second, agreement.Associated!.Name, DiagnosticTypeName(a), DiagnosticTypeName(agreement.AssociatedRight!)]
                : [$"At {first} = {second}, effective implementation mappings do not agree"];
            issue.Node.Report(
                requirement,
                issue.Code,
                evidence: evidence,
                related: [("contract", agreement.Left.Declaration, first), ("contract", agreement.Right.Declaration, second)],
                note: "Associated Types and effective implementation mappings must agree for every overlapping binding allowed by the declaration and conformance conditions (SPEC 8.9.2)");
        }
        else if (issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd && this.ReportCallablePremiseFailure(issue.Node, requirement))
        {
            // SPEC 8.11.4: an implementation cannot add an unrequired Callable bound.
        }
        else if (issue.Code is DiagnosticCode.MissingOriginBinding_Kd or DiagnosticCode.InvalidOriginBinding_Kd && this.ReportFormationCause(issue.Node, requirement, issue.Code))
        {
            // SPEC 8.1.2: a pair occurrence's Language cause, at the occurrence.
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallSlots?.TryGetValue(issue.Node, out var perCall) == true)
        {
            ReportPerCallSlot(issue.Node, perCall, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallOrigins?.TryGetValue(issue.Node, out var perCallOrigin) == true)
        {
            // SPEC 15.3.6, 10.8: only the argument's own per-call Origin would satisfy the slot; the argument is related.
            issue.Node.Report(requirement, issue.Code, evidence: [perCallOrigin.Reason], related: [("argument", perCallOrigin.Argument, null)]);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.ReportUndeclaredStorageOrigin(issue.Node, requirement, issue.Code))
        {
            // SPEC 15.3.2: an undeclared storage name, at the name.
        }
        else if (issue.Code == DiagnosticCode.InvalidOriginBinding_Kd && this.ReportAbsentSlot(issue.Node, requirement, issue.Code))
        {
            // SPEC 15.3.2: a projection of a slot its Type does not declare, at the slot name.
        }
        else if (issue.Code is DiagnosticCode.TransferRequired_Kd or DiagnosticCode.ExclusiveBorrowRequired_Kd or DiagnosticCode.DefaultArgumentMove_Kd &&
            this.acquisitionPlaces?.TryGetValue(issue.Node, out var acquisition) == true)
        {
            this.ReportAcquisition(issue.Node, acquisition.Place, acquisition.Object, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidCaptureBinding_Kd && issue.Node is IdentifierNameKoto { BoundSymbol: { } contextual } && IsContextualBinding(contextual))
        {
            this.ReportContextualCapture(issue.Node, contextual, requirement, issue.Code);
        }
        else if (this.ReportOriginRecords(issue.Node, issue.Code, requirement))
        {
            // SPEC 15.6.1, 23.1.2: a node's Origin relation or contract record, then its records beyond the first.
        }
        else if (issue.Code == DiagnosticCode.InvalidTypeFormation_Kd && this.arityFailures?.TryGetValue(issue.Node, out var arity) == true)
        {
            ReportArity(issue.Node, arity, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidOriginBinding_Kd && this.ReportInvalidOriginSlot(issue.Node, requirement, issue.Code))
        {
            // SPEC 15.2.3, 5.7: an Origin slot the written binding cannot supply, at the annotation.
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && issue.Node is TypeSemanticsKoto { BindingSetName: { } reusedSet } bindingSet)
        {
            this.ReportDuplicateBindingSet(bindingSet, reusedSet, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && this.captureRepeats?.TryGetValue(issue.Node, out var repeats) == true)
        {
            this.ReportCaptureRepeats(issue.Node, repeats, requirement, issue.Code);
        }
        else if (this.captureFailures?.TryGetValue(issue.Node, out var entry) == true)
        {
            this.ReportCaptureEntry(issue.Node, entry, requirement, issue.Code);
        }
        else if (this.writeTargets?.TryGetValue(issue.Node, out var target) == true)
        {
            this.ReportWrite(issue.Node, target, requirement, issue.Code);
        }
        else if (this.takeLayers?.TryGetValue(issue.Node, out var takeCases) == true)
        {
            this.ReportTake(issue.Node, takeCases, requirement, issue.Code);
        }
        else if (this.operatorOperands?.TryGetValue(issue.Node, out var operand) == true)
        {
            this.ReportOperatorOperand(issue.Node, operand, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.TypeMismatch_Kd && this.mismatches?.TryGetValue(issue.Node, out var mismatch) == true)
        {
            // The subject stays the failed node; the location is the syntax that shows the two Types. A numeric conversion
            // rejected for a wrapping integer Type explains the same-argument rule (SPEC 13.5.4).
            var wrappingConversion = issue.Node is ConversionKoto && (mismatch.Actual is BoundType { IsWrappingInteger: true } || mismatch.Expected is BoundType { IsWrappingInteger: true });
            var conversion = wrappingConversion ? null : this.ClosureConversionNote(issue.Node, mismatch);
            issue.Node.Report(requirement, issue.Code, note: wrappingConversion ? WrappingConversionNote : conversion ?? this.OmittedResultNote(issue.Node), at: mismatch.At, evidence: [DiagnosticTypeName(mismatch.Actual), DiagnosticTypeName(mismatch.Expected)]);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.clauseConstraints?.TryGetValue(issue.Node, out var reference) == true)
        {
            this.ReportClauseConstraint(issue.Node, reference, requirement, issue.Code);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.objectErasureFailures?.TryGetValue(issue.Node, out var erasure) == true)
        {
            var subject = DiagnosticTypeName(erasure.Source);
            var viewTarget = DiagnosticTypeName(erasure.Target);
            issue.Node.Report(requirement, issue.Code, note: $"Erasing {subject} to the {viewTarget} object View requires {subject} is Owned", evidence: issue.Code == DiagnosticCode.UnprovenConstraint_Kd ? [subject, "Owned"] : null);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.ownedConversions?.TryGetValue(issue.Node, out var owned) == true)
        {
            this.ReportOwnedConversion(issue.Node, owned, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.pendingJudgments?.TryGetValue(issue.Node, out var pending) == true)
        {
            ReportPendingJudgment(issue.Node, pending, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.callableConstraints?.TryGetValue(issue.Node, out var callable) == true)
        {
            // SPEC 8.3, 15.6.1: a Callable proof that is Unknown only in its Origin part, at the argument whose Type binds F.
            ReportCallableConstraint(issue.Node, callable, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnsatisfiedConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var rangeFailure) == true)
        {
            var subject = rangeFailure.Subject;
            issue.Node.Report(requirement, issue.Code, note: "Range iteration requires both boundaries to have the same integer Type", evidence: [DiagnosticTypeName(subject), rangeFailure.Entry.Name, DiagnosticTypeName(subject.Components[0]), DiagnosticTypeName(subject.Components[1])]);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var unproven) == true)
        {
            // SPEC 4.6.4: the boundary Types are not known to be one integer Type in this generic context.
            var start = DiagnosticTypeName(unproven.Subject.Components[0]);
            var end = DiagnosticTypeName(unproven.Subject.Components[1]);
            issue.Node.Report(requirement, issue.Code, note: $"Range iteration requires both boundaries to have one integer Type; the boundary Types {start} and {end} are not proven to be one integer Type");
        }
        else if (issue.Code is DiagnosticCode.UnresolvedBinding_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.constructorAbsences?.TryGetValue(issue.Node, out var absent) == true)
        {
            this.ReportConstructorAbsence(issue.Node, absent, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InaccessibleBinding_Kd && issue.Node is MemberAccessKoto { BoundSymbol: { } inaccessible })
        {
            issue.Node.Report(requirement, issue.Code, related: [("declaration", inaccessible.Declaration, $"inaccessible declaration '{inaccessible.Name}'")]);
        }
        else
        {
            issue.Node.Report(requirement, issue.Code, note: this.OmittedResultNote(issue.Node), evidence: issue.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.TransferRequired_Kd ? [issue.Node.ToString()] : null);
        }
    }
}
