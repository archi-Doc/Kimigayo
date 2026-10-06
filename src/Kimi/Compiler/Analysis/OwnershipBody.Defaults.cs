// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // Preorder evaluation intervals preserve the declaration-to-call Type/Origin substitution at each replica of a default.
    // A nested default substitutes its own call first, then its enclosing defaults, then the body's case or instance.
    internal List<(int Start, int End, BoundCall Call, int Parent)>? DefaultContexts { get; set; }

    internal Dictionary<(BindingSymbol Symbol, int Context), int>? DefaultSymbolPlaces { get; set; }

    internal bool TrySymbolPlace(BindingSymbol symbol, int context, out int place)
    {
        for (; context >= 0; context = this.DefaultContexts![context].Parent)
        {
            if (this.DefaultSymbolPlaces is { } locals && locals.TryGetValue((symbol, context), out place))
            {
                return true;
            }
        }

        return this.SymbolPlaces.TryGetValue(symbol, out place);
    }

    internal bool TrySymbolPlaceAt(BindingSymbol symbol, int operation, out int place)
        => this.TrySymbolPlace(symbol, this.DefaultContextAt(operation), out place);

    internal int DefaultContextAt(int operation)
    {
        if (this.DefaultContexts is not { Count: > 0 } contexts)
        {
            return -1;
        }

        var low = 0;
        var high = contexts.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (contexts[middle].Start <= operation)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var context = low - 1;
        while (context >= 0 && operation >= contexts[context].End)
        {
            context = contexts[context].Parent;
        }

        return context;
    }

    internal BoundType? SubstituteDefaultType(BoundType? type, int context)
    {
        while (type is not null && context >= 0)
        {
            var entry = this.DefaultContexts![context];
            type = this.Function.CodeContext.Compilation.Binding.InstantiateStorageType(type, entry.Call);
            context = entry.Parent;
        }

        return type;
    }

    internal BoundType? ConcreteAt(BoundType? type, int operation)
        => this.Concrete(this.SubstituteDefaultType(type, this.DefaultContextAt(operation)));

    internal BoundCall? CallAt(int operation)
        => this.SubstituteDefaultCall((this.Operations[operation].Source as Parsing.InvocationKoto)?.BoundCall, this.DefaultContextAt(operation));

    internal BoundCall? SubstituteDefaultCall(BoundCall? call, int context)
    {
        for (; call is not null && context >= 0; context = this.DefaultContexts![context].Parent)
        {
            call = this.Function.CodeContext.Compilation.Binding.InstantiateDefaultCall(call, this.DefaultContexts![context].Call);
        }

        return call;
    }
}
