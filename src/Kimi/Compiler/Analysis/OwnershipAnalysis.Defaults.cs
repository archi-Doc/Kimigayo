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

    private void PrepareDefaults(BoundCall plan, int mark)
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
                if (this.DefiniteDefaultMove(target, this.defaultParameter, omitted.Expression))
                {
                    // Declaration checking owns the rejected Move. Do not execute it against the pending caller's slots and
                    // manufacture a moved-argument error at callee entry; no value is acquired from this invalid default.
                }
                else if (this.flow!.DefaultCompletionPending(omitted.Expression) || !ScalarDefaults.Supports(target, this.defaultParameter))
                {
                    this.Unsupported(omitted.Expression);
                }
                else if (complete)
                {
                    // The explicit arguments are already acquired; the declaration expression reads those prepared slots.
                    var contexts = this.body.DefaultContexts ??= new();
                    var previousContext = this.defaultContext;
                    var context = contexts.Count;
                    contexts.Add((this.body.Operations.Count, int.MaxValue, plan, previousContext));
                    this.defaultContext = context;
                    try
                    {
                        place = this.Argument(omitted.Expression, ArgumentOperationKind.Value);
                    }
                    finally
                    {
                        var entry = contexts[context];
                        contexts[context] = (entry.Start, this.body.Operations.Count, entry.Call, entry.Parent);
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
