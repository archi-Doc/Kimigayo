// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    internal readonly record struct CallArgumentMap(bool InputsValid, bool Valid, int DefaultsUsed);

    // Mapping depends only on syntax and the declaration, never on inferred Types. Keep every
    // independently mappable position even after a bad label; group contracts use those facts too.
    internal static CallArgumentMap MapCallArguments(InvocationKoto call, FunctionKoto function, bool receiver, Span<int> mapping, Span<bool> used)
    {
        used[..function.Parameters.Count].Clear();
        var valid = true;
        if (receiver)
        {
            var slot = function.BoundSymbol!.ReceiverIndex;
            if (slot < 0)
            {
                valid = false;
            }
            else
            {
                used[slot] = true;
            }
        }

        var next = 0;
        var named = false;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var mapped = function.TryMapArgument(call.GetArgumentLabel(i), ref next, ref named, used, out var slot);
            mapping[i] = mapped ? slot : -1;
            valid &= mapped;
        }

        var inputsValid = valid;
        var defaults = 0;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (!used[i])
            {
                valid &= function.Parameters[i].DefaultValue is not null;
                defaults++;
            }
        }

        return new(inputsValid, valid, defaults);
    }
}
