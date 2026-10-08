// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<int[]> defaultPlaceFrames = new();
    private int[] defaultPlaces = [];
    private FunctionKoto? defaultFunction;
    private int defaultParameter;
    private int defaultDepth;
    private int defaultContext = -1;

    // The builder's active interpretation context: the innermost default being evaluated, or the root.
    private InterpretationContext Active => new(this.defaultContext);

    private void PrepareDefaults(Koto call, BoundCall plan, int mark)
    {
        if (plan.DefaultArguments.IsEmpty || plan.Target.Declaration is not FunctionKoto target)
        {
            return;
        }

        // A nested call evaluates its explicit arguments in the outer context, then prepares its own defaults. Retain the
        // outer slots until this frame ends, including when an inner argument never completes. Warm calls reuse the buffers.
        var previousPlaces = this.defaultPlaces;
        var previousFunction = this.defaultFunction;
        var previousParameter = this.defaultParameter;
        var depth = this.defaultDepth;
        if (depth == this.defaultPlaceFrames.Count)
        {
            this.defaultPlaceFrames.Add([]);
        }

        var slots = this.defaultPlaceFrames[depth];
        if (slots.Length < target.Parameters.Count)
        {
            Array.Resize(ref slots, target.Parameters.Count);
            this.defaultPlaceFrames[depth] = slots;
        }

        this.defaultDepth = depth + 1;
        try
        {
            this.defaultPlaces = slots;
            this.defaultFunction = target;
            slots.AsSpan(0, target.Parameters.Count).Fill(-1);
            var cursor = mark;
            var complete = true;
            if (plan.Receiver is not null)
            {
                var place = this.arguments[cursor++];
                slots[plan.ReceiverOperation.ParameterIndex] = place;
                complete &= place >= 0;
            }

            for (var i = 0; i < plan.ArgumentToParameter.Length; i++)
            {
                var place = this.arguments[cursor++];
                complete &= place >= 0;
                slots[plan.ArgumentToParameter[i]] = place;
            }

            foreach (var omitted in plan.DefaultArguments)
            {
                this.defaultParameter = omitted.Parameter.Slot;
                var place = -1;
                if (this.InvalidDefault(target, this.defaultParameter, omitted.Expression))
                {
                    // Declaration checking owns the rejected default. Do not execute it against the pending caller's slots
                    // and manufacture call-entry conflicts; no value is acquired from this invalid default.
                }
                else if (this.flow!.DefaultCompletionPending(omitted.Expression))
                {
                    this.Unsupported(omitted.Expression);
                }
                else if (complete)
                {
                    if (this.flow.RecursiveDefault(omitted.Expression))
                    {
                        place = this.EvaluateDefault(plan, this.defaultParameter, omitted.Expression);
                        this.arguments.Add(place);
                        slots[this.defaultParameter] = place;
                        complete &= place >= 0;
                        continue;
                    }

                    // The explicit arguments are already acquired; the declaration expression reads those prepared slots.
                    var contexts = this.body.DefaultContexts ??= new();
                    var previousContext = this.defaultContext;
                    var context = contexts.Count;
                    contexts.Add((this.body.Operations.Count, int.MaxValue, plan, previousContext, previousContext < 0 ? call : contexts[previousContext].Anchor));
                    this.defaultContext = context;
                    try
                    {
                        place = this.Argument(omitted.Expression, ArgumentOperationKind.Value);
                    }
                    finally
                    {
                        var entry = contexts[context];
                        contexts[context] = entry with { End = this.body.Operations.Count };
                        this.defaultContext = previousContext;
                    }
                }

                this.arguments.Add(place);
                this.defaultPlaces[this.defaultParameter] = place;
                complete &= place >= 0;
            }
        }
        finally
        {
            this.defaultPlaces = previousPlaces;
            this.defaultFunction = previousFunction;
            this.defaultParameter = previousParameter;
            this.defaultDepth = depth;
        }
    }

    // SPEC 7.2.3: an evaluator only inspects the slots already acquired by the pending call. It takes no ownership of them;
    // its one normal result becomes the next pending argument. Recursive evaluator entries are registered before their bodies.
    private int EvaluateDefault(BoundCall call, int parameter, Koto expression)
    {
        var inputs = this.body.DefaultInputs ??= new();
        var evaluations = this.body.DefaultEvaluations ??= new();
        var start = inputs.Count;
        for (var i = 0; i < parameter; i++)
        {
            var place = this.defaultPlaces[i];
            if (place < 0)
            {
                return -1;
            }

            var value = this.Value(place);
            var read = this.Emit(OwnershipOperationKind.Read, expression, place);
            this.SetValue(read, OwnershipValueKind.DefaultRead, [], constant: inputs.Count);
            this.placeValues[place] = value;
            inputs.Add((place, value, read));
        }

        var invoke = this.Emit(OwnershipOperationKind.Call, expression);
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        var type = this.Resolve(this.compilation.Binding.InstantiateStorageType(((FunctionKoto)call.Target.Declaration).Parameters[parameter].Type.BoundType!, call), this.Active)!;
        var result = this.Place(expression, type, OwnershipPlaceKind.Temporary, false);
        var produce = this.Emit(OwnershipOperationKind.Produce, expression, result);
        this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        this.SetValue(invoke, OwnershipValueKind.DefaultCall, [], constant: evaluations.Count);
        evaluations.Add(new(invoke, call, parameter, start)); // The call plan is already resolved in the active context.
        if (ScalarResult(type) || ReferenceTypes.IsString(type))
        {
            this.SetValue(produce, OwnershipValueKind.Alias, [invoke]);
        }

        this.placeValues[result] = produce;
        return this.RegisterTemporary(result);
    }

    // SPEC 7.2.3: inside a default evaluated at a call, a preceding parameter, read or captured, names the slot that call
    // prepared, or -1 when that slot is not prepared.
    private bool TryDefaultSlot(BindingSymbol? symbol, out int place)
    {
        if (this.defaultFunction is { } function && symbol is { Kind: BindingSymbolKind.Parameter } && ReferenceEquals(symbol.Scope.Owner, function))
        {
            place = (uint)symbol.Slot < (uint)this.defaultParameter ? this.defaultPlaces[symbol.Slot] : -1;
            return true;
        }

        place = -1;
        return false;
    }
}
