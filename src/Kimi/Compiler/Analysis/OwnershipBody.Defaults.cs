// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // Preorder evaluation intervals preserve the declaration-to-call Type/Origin substitution at each replica of a default.
    // A nested default substitutes its own call first, then its enclosing defaults, then the body's case or instance.
    internal List<(int Start, int End, BoundCall Call, int Parent)>? DefaultContexts { get; set; }

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
}
