// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int[] defaultPlaces = [];
    private FunctionKoto? defaultFunction;
    private int defaultParameter;

    private void PrepareDefaults(BoundCall plan, int mark)
    {
        if (plan.DefaultArguments.IsEmpty || plan.Target.Declaration is not FunctionKoto target)
        {
            return;
        }

        if (this.defaultPlaces.Length < target.Parameters.Count)
        {
            Array.Resize(ref this.defaultPlaces, target.Parameters.Count);
        }

        this.defaultPlaces.AsSpan(0, target.Parameters.Count).Fill(-1);
        var cursor = mark;
        var complete = true;
        if (plan.Receiver is not null)
        {
            var place = this.arguments[cursor++];
            this.defaultPlaces[plan.ReceiverOperation.ParameterIndex] = place;
            complete &= place >= 0;
        }

        for (var i = 0; i < plan.ArgumentToParameter.Length; i++)
        {
            var place = this.arguments[cursor++];
            complete &= place >= 0;
            this.defaultPlaces[plan.ArgumentToParameter[i]] = place;
        }

        this.defaultFunction = target;
        try
        {
            foreach (var omitted in plan.DefaultArguments)
            {
                this.defaultParameter = omitted.Parameter.Slot;
                var place = -1;
                if (!ScalarDefaults.Supports(target, this.defaultParameter))
                {
                    if (!this.DefiniteDefaultMove(target, this.defaultParameter, omitted.Expression))
                    {
                        this.Unsupported(omitted.Expression);
                    }
                }
                else if (complete)
                {
                    // Explicit scalar arguments are already acquired copies. Reads in
                    // the declaration expression now address those prepared slots.
                    place = this.Argument(omitted.Expression, ArgumentOperationKind.Value);
                }

                this.arguments.Add(place);
                this.defaultPlaces[this.defaultParameter] = place;
                complete &= place >= 0;
            }
        }
        finally
        {
            this.defaultFunction = null;
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
