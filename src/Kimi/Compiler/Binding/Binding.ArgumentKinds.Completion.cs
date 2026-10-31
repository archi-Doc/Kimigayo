// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument completion (SPEC 10.5, 10.5.4, 10.2, 7.7.4, 23.1.2; DECISIONS BU-2, BU-43): the completion of a call's argument parts, an
// output of Binding.ArgumentKinds.cs. CompleteWaiting completes the selected candidate's waiting arguments and the waiting callables
// at its F (TakeWaiting), and decides a join they alone kept waiting (DecideTaken); CompleteArguments completes its arguments at its
// operations and erases a callable completed at a common Function Type (ErasePart); CompleteValueCallPart completes a value call's
// part at its one candidate's parameter Type; CompleteUnselected binds on its own each part of a call that completes none. The
// selection, its defaults and their re-check (SolveSelected, CheckDefaulted) stay in Binding.ArgumentKinds.cs.
public sealed partial class Binding
{
    // SPEC 10.5, 10.5.4: completes the selected candidate's waiting arguments against its Types, and the waiting callables of its Tuple
    // and array literal arguments and of its control-flow arguments' results that bind an F, `ref/F` or `uniq/F` of it, a join they alone
    // kept waiting then decided (DecideTaken; DECISIONS BU-43). A body or capture failure never retries another candidate, and an
    // inferred result never supplies outer signature evidence. False when one fails.
    private bool CompleteWaiting(ref CallEvaluation evaluation, int winner, bool[] completedWaiting, BoundType?[] waitingContexts)
    {
        var call = evaluation.Call;
        var selected = (FunctionKoto)evaluation.Evaluated[winner].Symbol!.Declaration;
        var solution = this.Solution(evaluation, selected, evaluation.Evaluated[winner].DeclaringType);
        var operations = evaluation.Operations.AsSpan(winner * evaluation.OperationStride, evaluation.OperationStride);
        var contract = this.activeRequirementContract;
        this.activeRequirementContract = null; // The arguments are expressions of the caller's context.
        try
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var argument = call.ArgumentNodes[i];
                var pattern = selected.Parameters[evaluation.Mapping[i]].Type.BoundType!;
                if (this.IsWaiting(argument))
                {
                    completedWaiting[i] = true;
                    waitingContexts[i] = operations[i].ParameterType;
                    if (this.CompleteWaitingPart(argument, pattern, solution.SlotOwner, operations[i].ParameterType, evaluation.Scope) is null)
                    {
                        return false;
                    }
                }
                else
                {
                    var awaited = this.AwaitsPosition(argument);
                    if (!this.TakeWaiting(argument, pattern, solution.SlotOwner, solution) || (awaited && !this.DecideTaken(argument)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            this.activeRequirementContract = contract;
        }
    }

    // SPEC 10.5: a waiting part at F, `ref/F` or `uniq/F`: an anonymous function with the fixed expected call signature S (`context`), a
    // reference selected by S, or without S a value only when it needs none (FailUnfixedReference otherwise); any other at its Type.
    private BoundType? CompleteWaitingPart(Koto part, BoundType pattern, Koto owner, BoundType? context, BindingScope scope)
    {
        var concrete = CallableSlotType(pattern, owner) is not null;
        var node = KotoHelper.UnwrapParentheses(part);
        var kind = this.KindOf(part);
        if (concrete && kind == PartKind.Function && node is FunctionKoto closure)
        {
            return this.BindClosureArgument(part, closure, scope, context);
        }

        if (!concrete || kind != PartKind.Reference)
        {
            return this.BindNode(part, scope, context);
        }

        return context is { } signature
            ? this.BindFunctionReference(part, node.BoundSymbol!, signature, scope, erase: false) : this.BindFunctionItem(part, node.BoundSymbol!, scope);
    }

    // SPEC 10.5, 10.2: completes the selected candidate's arguments at its operations: a part still unbound is bound at its parameter's
    // Type, and a Closure or Function Item the candidate erases is converted to the common Function Type. False when one fails; every
    // failing erasure is reported, since the call rests on it and another candidate is never selected.
    private bool CompleteArguments(InvocationKoto call, BindingScope scope, Span<BoundArgumentOperation> operations)
    {
        var erased = true;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            if (this.PropagateBound(argument, operations[i].ParameterType) is null)
            {
                // The argument is an expression of the caller's context, not of the selected requirement.
                var selectedContract = this.activeRequirementContract;
                this.activeRequirementContract = null;
                this.RequireType(argument, scope, operations[i].SourceType ?? operations[i].ParameterType);
                this.activeRequirementContract = selectedContract;
                if (argument.BindingState != BindingState.Resolved)
                {
                    return false;
                }

                operations[i] = operations[i] with { SourceType = argument.BoundType };
            }
            else if (argument.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } closureType &&
                operations[i].ParameterType is { Kind: BoundTypeKind.Function } erasedType && operations[i].Adaptation == ArgumentAdaptation.Erasure)
            {
                erased &= this.ErasePart(argument, closureType, erasedType);
                operations[i] = operations[i] with { SourceType = erasedType };
            }
        }

        return erased;
    }

    // SPEC 7.7.4, 10.5: a Closure or Function Item part completed at a common Function Type is converted to it; its signature, Shared
    // receiver and Owned environment are judged here, after selection, and a failure is the part's own record, an environment that is
    // not Owned the Constraint record and a receiver that is not Shared a Type mismatch with its Note (SPEC 23.1.2).
    private bool ErasePart(Koto part, BoundType actual, BoundType erased)
    {
        if (!this.ErasesToFunction(part, actual, erased))
        {
            this.RecordMismatch(part, part, actual, erased);
            return false;
        }

        part.ErasedFunctionType = erased;
        return true;
    }

    // SPEC 12.5.2, 10.5.1, 10.1.1 (D1): a value call's part, classified as a named call's is (PrepareArgument) and completed at its
    // known parameter Type with no selection probe, since a value call has one candidate: a Typed part keeps its own Type, which the
    // call adapts; every other part is bound at the parameter Type, a Literal at its literal input Type. Null when it fails.
    private BoundType? CompleteValueCallPart(Koto part, BoundType parameter, BindingScope scope)
    {
        var literal = IsUnfittedLiteral(part);
        if (!this.PrepareArgument(part, scope, null, callableContext: true))
        {
            return null;
        }

        if (this.KindOf(part) != PartKind.Typed || part.BoundType is not { } typed)
        {
            return this.BindNode(part, scope, literal ? LiteralInputType(parameter) : parameter);
        }

        // SPEC 7.7.4: a Closure or Function Item completed at a Function Type is converted to it, as at a named call (ErasePart).
        return typed.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem && parameter.Kind == BoundTypeKind.Function &&
            this.ErasesToFunction(part, typed, parameter) ? (this.ErasePart(part, typed, parameter) ? parameter : null) : typed;
    }

    // SPEC 23.1.2, 10.1: a call that ends unresolved, with no selected candidate or one that failed before completing its arguments,
    // binds each argument part still unbound on its own under the argument probe, so that the part's own problems are reported once
    // and none that only the missing expectation causes: a control-flow part's literal sources and its source calls that would wait
    // stay unbound, and the parts of a Tuple or array literal, a contextual Case and a waiting nested call are completed alike. A
    // control-flow part already bound in this pass is not bound again. Anonymous functions stay unbound (PLAN R1e); no candidate is selected
    // again (SPEC 10.1); a deferred call is completed by its outer call.
    private BoundType? CompleteUnselected(InvocationKoto call, BindingScope scope, BoundType? type)
    {
        for (var i = 0; type is null && !this.IsWaitingPart(call) && i < call.ArgumentNodes.Count; i++)
        {
            this.CompleteUnselectedPart(call.ArgumentNodes[i], scope);
        }

        return type;
    }

    private void CompleteUnselectedPart(Koto part, BindingScope scope)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        if (node.BoundType is not null)
        {
            return;
        }

        if (ControlFlowExpression(node) is { } flow)
        {
            if (!this.resultContexts.ContainsKey(flow))
            {
                this.BindIndependentArgument(node, scope); // Not yet bound in this pass: a Literal or waiting part.
            }

            if (this.KindOf(node) == PartKind.Join)
            {
                this.CompleteJoinCases(flow, scope);
            }

            return;
        }

        var waitingCase = this.KindOf(node) == PartKind.Case;
        IReadOnlyList<Koto>? parts = node switch
        {
            TupleLiteralKoto tuple => tuple.Elements,
            ArrayLiteralKoto array => array.Elements,
            InvocationKoto nested when waitingCase || this.WasDeferred(nested) => nested.ArgumentNodes,
            _ => null,
        };
        for (var i = 0; parts is not null && i < parts.Count; i++)
        {
            if (waitingCase && ClassifyPart(parts[i]) == PartKind.Typed && parts[i].BindingState == BindingState.Unvisited)
            {
                this.BindIndependentArgument(parts[i], scope); // A waiting Case's own payload, bound on its own.
            }
            else
            {
                this.CompleteUnselectedPart(parts[i], scope);
            }
        }
    }

    // SPEC 23.1.2: each waiting Case result of a Join a call completes none of, completed on its own (its payloads bound alone).
    private void CompleteJoinCases(Koto join, BindingScope scope)
    {
        var start = this.positionParts.Count;
        this.AddResults(join);
        for (var i = start; i < this.positionParts.Count; i++)
        {
            if (this.KindOf(this.positionParts[i]) == PartKind.Case)
            {
                this.CompleteUnselectedPart(this.positionParts[i], scope);
            }
        }

        this.ClosePosition(start);
    }
}
