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
            var mapped = TryMapArgument(function, call.GetArgumentLabel(i), ref next, ref named, used, out var slot);
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

    // SPEC 7.2.1, 7.3.3: an argument without a label may supply parameter `index`: the receiver's, or one before the `!` boundary (any
    // without one).
    internal static bool AllowsPositionalArgument(FunctionKoto function, int index)
        => index == function.BoundSymbol?.ReceiverIndex || function.NameBoundaryIndex < 0 || index < function.NameBoundaryIndex;

    // SPEC 10.1: maps one argument to its parameter slot, by its label or by position, and marks the slot used; false for an unknown
    // label, a slot already supplied, and a positional argument after a named one, past the last parameter or past the `!` boundary.
    internal static bool TryMapArgument(FunctionKoto function, string? label, ref int next, ref bool named, Span<bool> used, out int slot)
    {
        if (label is not null)
        {
            named = true;
            slot = function.FindParameter(label);
        }
        else
        {
            slot = -1;
            if (named)
            {
                return false;
            }

            // Only a receiver can already be supplied during the positional prefix.
            while (next < function.Parameters.Count && used[next])
            {
                next++;
            }

            slot = next++;
        }

        if ((uint)slot >= (uint)function.Parameters.Count || used[slot] ||
            (label is null && !AllowsPositionalArgument(function, slot)))
        {
            return false;
        }

        used[slot] = true;
        return true;
    }

    // SPEC 10.1 step 2, 10.1.1: the defaults a candidate uses, its parameter slots that neither a written argument (`mapping`) nor the
    // receiver (`receiverSlot`, -1 for none) supplies; none for a function reference's call, which passes every argument.
    private static void DefaultSlots(FunctionKoto function, ReadOnlySpan<int> mapping, int receiverSlot, ref SlotSet defaults)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (i != receiverSlot && !mapping.Contains(i))
            {
                defaults.Add(i);
            }
        }
    }
}
