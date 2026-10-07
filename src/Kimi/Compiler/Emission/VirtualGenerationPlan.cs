// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Physical layouts for verified, nongeneric shared slots. Semantic slot correspondence remains in Binding;
// the emitter rejects generic virtual declarations before entering this plan.
internal sealed class VirtualGenerationPlan
{
    private readonly List<FunctionKoto> declarations = new();
    private readonly Dictionary<BindingSymbol, List<FunctionKoto>> declarationsByType = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<FunctionKoto>> declarationPool = new();
    private readonly Dictionary<BindingSymbol, Layout> layouts = new(ReferenceEqualityComparer.Instance);
    private readonly List<Layout> layoutPool = new();
    private readonly Dictionary<FunctionKoto, (int Slot, int Receiver)> slots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionKoto, FunctionAbi> addresses = new(ReferenceEqualityComparer.Instance);
    private readonly List<(FunctionAbi Body, FunctionAbi Entry)> entries = new();
    private readonly List<EmissionOperand> operands = new();
    private Binding? binding;
    private EmissionModule? module;
    private Dictionary<FunctionKoto, FunctionAbi>? functions;

    internal bool Begin(Binding binding, EmissionModule module, Dictionary<FunctionKoto, FunctionAbi> functions)
    {
        this.Clear();
        this.binding = binding;
        this.module = module;
        this.functions = functions;
        foreach (var function in functions.Keys)
        {
            if (function.IsVirtual || function.IsOverride)
            {
                this.declarations.Add(function);
            }
        }

        this.declarations.Sort(static (a, b) =>
        {
            var order = string.CompareOrdinal(a.CodeContext.SourceDocument?.Path, b.CodeContext.SourceDocument?.Path);
            return order != 0 ? order : a.Span.Start.CompareTo(b.Span.Start);
        });
        foreach (var function in this.declarations)
        {
            var symbol = function.BoundSymbol!.Scope.Owner.BoundSymbol!;
            if (!this.declarationsByType.TryGetValue(symbol, out var members))
            {
                var ordinal = this.declarationsByType.Count;
                if (ordinal == this.declarationPool.Count)
                {
                    this.declarationPool.Add(new());
                }

                members = this.declarationPool[ordinal];
                this.declarationsByType.Add(symbol, members);
            }

            members.Add(function);
        }

        foreach (var function in this.declarations)
        {
            if (!function.IsVirtual)
            {
                continue; // An override table is needed only when its descriptor is retained.
            }

            var receiver = function.Parameters[function.BoundSymbol!.ReceiverIndex].Type.BoundType!;
            if (this.GetTable(receiver.Components[0]) is null)
            {
                return false;
            }
        }

        return true;
    }

    internal void Clear()
    {
        foreach (var members in this.declarationPool)
        {
            members.Clear();
        }

        this.declarations.Clear();
        this.declarationsByType.Clear();
        this.layouts.Clear();
        this.slots.Clear();
        this.addresses.Clear();
        this.operands.Clear();
        this.binding = null;
        this.module = null;
        this.functions = null;
    }

    internal void Complete()
    {
        this.layoutPool.RemoveRange(this.layouts.Count, this.layoutPool.Count - this.layouts.Count);
        this.declarationPool.RemoveRange(this.declarationsByType.Count, this.declarationPool.Count - this.declarationsByType.Count);
        this.entries.RemoveRange(this.addresses.Count, this.entries.Count - this.addresses.Count);
    }

    internal bool TrySlot(FunctionKoto original, out int slot, out int receiver)
    {
        var found = this.slots.TryGetValue(original, out var selected);
        (slot, receiver) = found ? selected : (-1, -1);
        return found;
    }

    internal FunctionAbi[]? GetTable(BoundType type)
    {
        if (this.declarations.Count == 0 || type.Symbol is not { Declaration: StructKoto } symbol)
        {
            return [];
        }

        if (this.layouts.TryGetValue(symbol, out var known))
        {
            return known.Table;
        }

        var inherited = type.StoredBase is { } parent ? this.GetTable(parent) : [];
        if (inherited is null)
        {
            return null;
        }

        var ordinal = this.layouts.Count;
        if (ordinal == this.layoutPool.Count)
        {
            this.layoutPool.Add(new());
        }

        var layout = this.layoutPool[ordinal];
        layout.Entries.Clear();
        layout.Entries.AddRange(inherited);
        var members = this.declarationsByType.GetValueOrDefault(symbol);
        for (var d = 0; members is not null && d < members.Count; d++)
        {
            var function = members[d];
            var abi = this.functions![function];
            if (function.IsVirtual)
            {
                var receiver = -1;
                for (var i = 0; i < abi.Parameters.Length; i++)
                {
                    if (abi.Parameters[i].LogicalIndex == function.BoundSymbol!.ReceiverIndex && abi.Parameters[i].Kind == AbiParameterKind.Value && abi.Parameters[i].Type == "ptr")
                    {
                        receiver = i;
                        break;
                    }
                }

                if (receiver < 0)
                {
                    return null;
                }

                this.slots.Add(function, (layout.Entries.Count, receiver));
                layout.Entries.Add(abi);
            }
            else
            {
                if (!this.binding!.TryGetVirtualOverride(function, out var implementation) ||
                    !this.slots.TryGetValue(implementation.Slot.Original, out var slot) || slot.Slot >= layout.Entries.Count ||
                    !SameAbi(layout.Entries[slot.Slot], abi))
                {
                    return null;
                }

                layout.Entries[slot.Slot] = abi;
            }
        }

        if (!layout.Table.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(layout.Entries)))
        {
            layout.Table = layout.Entries.ToArray();
        }

        this.layouts.Add(symbol, layout);
        return layout.Table;
    }

    // Only erasure needs an address. Known Item calls use the same indexed instruction as member calls.
    internal FunctionAbi? Address(FunctionKoto original)
    {
        if (this.addresses.TryGetValue(original, out var known))
        {
            return known;
        }

        if (!this.TrySlot(original, out var slot, out var receiver) || this.functions!.GetValueOrDefault(original) is not { } body)
        {
            return null;
        }

        var ordinal = this.addresses.Count;
        FunctionAbi entry;
        if (ordinal < this.entries.Count && ReferenceEquals(this.entries[ordinal].Body, body))
        {
            entry = this.entries[ordinal].Entry;
        }
        else
        {
            entry = new("__kimi_virtual_item" + ordinal.ToString(CultureInfo.InvariantCulture), body.Result, body.Parameters, body.NoReturn, body.ResultSlot, body.CallerLocation);
            if (ordinal == this.entries.Count)
            {
                this.entries.Add((body, entry));
            }
            else
            {
                this.entries[ordinal] = (body, entry);
            }
        }

        this.addresses.Add(original, entry);
        this.operands.Clear();
        foreach (var parameter in body.Parameters)
        {
            this.operands.Add(parameter.Kind switch
            {
                AbiParameterKind.ResultSlot => new(EmissionOperandKind.ReturnAddress, 0),
                AbiParameterKind.Location => new(EmissionOperandKind.CallerLocation, 0),
                AbiParameterKind.LocationLength => new(EmissionOperandKind.CallerLocationLength, 0),
                _ => new(EmissionOperandKind.Argument, parameter.LogicalIndex),
            });
        }

        var function = this.module!.AddFunction(entry, exported: false);
        function.AddCall(0, body, CollectionsMarshal.AsSpan(this.operands), slot, receiver);
        if (entry.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, 0);
        }
        else if (entry.Result == "void")
        {
            function.Add(EmissionOpcode.ReturnVoid, 1);
        }
        else
        {
            function.AddScalar(EmissionOpcode.ReturnScalar, 1, [new(EmissionOperandKind.Value, 0)], entry.Result);
        }

        return entry;
    }

    private static bool SameAbi(FunctionAbi original, FunctionAbi implementation)
    {
        if (original.Result != implementation.Result || original.ResultSlot != implementation.ResultSlot || original.NoReturn != implementation.NoReturn ||
            original.CallerLocation != implementation.CallerLocation || original.Parameters.Length != implementation.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < original.Parameters.Length; i++)
        {
            if (original.Parameters[i] != implementation.Parameters[i])
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Layout
    {
        internal List<FunctionAbi> Entries { get; } = new();

        internal FunctionAbi[] Table { get; set; } = [];
    }
}
