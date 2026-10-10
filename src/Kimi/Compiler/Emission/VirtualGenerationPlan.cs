// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Physical layouts for verified shared slots. Declaration shape fixes indices; closed Type bindings select
// ordinary entries. Semantic slot correspondence remains in Binding, including direct base selection.
internal sealed class VirtualGenerationPlan
{
    private readonly List<FunctionKoto> declarations = new();
    private readonly Dictionary<BindingSymbol, List<FunctionKoto>> declarationsByType = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<FunctionKoto>> declarationPool = new();
    private readonly Dictionary<BoundType, Layout> layouts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, int> shapes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Layout> layoutPool = new();
    private readonly Dictionary<FunctionKoto, int> slots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionAbi, FunctionAbi> addresses = new(ReferenceEqualityComparer.Instance);
    private readonly List<(FunctionAbi Body, FunctionAbi Entry)> entries = new();
    private readonly List<EmissionOperand> operands = new();
    private Binding? binding;
    private EmissionModule? module;
    private Dictionary<FunctionKoto, FunctionAbi>? functions;
    private Compilation? compilation;
    private GenericStoragePlan? generics;
    private AggregateLayoutPool? aggregateLayouts;

    internal string? Failure { get; private set; }

    internal bool Begin(Compilation compilation, EmissionModule module, Dictionary<FunctionKoto, FunctionAbi> functions, GenericStoragePlan generics, AggregateLayoutPool aggregateLayouts)
    {
        this.Clear();
        this.compilation = compilation;
        this.binding = compilation.Binding;
        this.generics = generics;
        this.aggregateLayouts = aggregateLayouts;
        this.module = module;
        this.functions = functions;
        for (var i = 0; i < compilation.Ownership.Bodies.Count; i++)
        {
            var function = compilation.Ownership.Bodies[i].Function;
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
            if (this.Shape(receiver.Components[0]) < 0)
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

        foreach (var layout in this.layoutPool)
        {
            layout.Choices.Clear();
            layout.Prepared = false;
        }

        this.declarations.Clear();
        this.declarationsByType.Clear();
        this.layouts.Clear();
        this.shapes.Clear();
        this.slots.Clear();
        this.addresses.Clear();
        this.operands.Clear();
        this.binding = null;
        this.module = null;
        this.functions = null;
        this.compilation = null;
        this.generics = null;
        this.aggregateLayouts = null;
        this.Failure = null;
    }

    internal void Complete()
    {
        this.layoutPool.RemoveRange(this.layouts.Count, this.layoutPool.Count - this.layouts.Count);
        this.declarationPool.RemoveRange(this.declarationsByType.Count, this.declarationPool.Count - this.declarationsByType.Count);
        this.entries.RemoveRange(this.addresses.Count, this.entries.Count - this.addresses.Count);
    }

    internal bool TrySlot(FunctionKoto original, FunctionAbi? abi, out int slot, out int receiver)
    {
        receiver = -1;
        if (!this.slots.TryGetValue(original, out slot) || abi is null)
        {
            return false;
        }

        // Substitution can erase a Unit input or introduce a hidden aggregate result before self.
        for (var i = 0; i < abi.Parameters.Length; i++)
        {
            if (abi.Parameters[i].LogicalIndex == original.BoundSymbol!.ReceiverIndex && abi.Parameters[i].Kind == AbiParameterKind.Value && abi.Parameters[i].Type == "ptr")
            {
                receiver = i;
                return true;
            }
        }

        return false;
    }

    internal FunctionAbi? Entry(FunctionKoto function, BoundType? declaring, bool implementationBody = true)
    {
        if (!GenericStoragePlan.IsGeneric(function))
        {
            return this.functions!.GetValueOrDefault(function);
        }

        if (declaring is null || this.binding!.ImplementationContext(function, declaring) is not { } context)
        {
            this.Failure = "A selected virtual implementation requires a closed declaring Type.";
            return null;
        }

        if (!this.generics!.PrepareSourceEntry(this.compilation!, this.module!, this.aggregateLayouts!, context, out var abi, out var failure, implementationBody))
        {
            this.Failure = failure;
            return null;
        }

        return abi;
    }

    internal FunctionAbi?[]? GetTable(BoundType type)
    {
        if (this.declarations.Count == 0 || type.Symbol is not { Declaration: StructKoto })
        {
            return [];
        }

        var layout = this.GetLayout(type);
        if (layout is null)
        {
            return null;
        }

        if (layout.Prepared)
        {
            return layout.Table;
        }

        // Published physical records retain their table. Copy only on the first changed entry,
        // preserving old records without a second retained list or an unchanged-table scan.
        FunctionAbi?[]? changed = layout.Table.Length == layout.Choices.Count ? null : new FunctionAbi?[layout.Choices.Count];
        for (var i = 0; i < layout.Choices.Count; i++)
        {
            var choice = layout.Choices[i];
            var proof = this.binding!.VirtualApplicability(choice.Slot);
            FunctionAbi? abi = null;
            if (proof != ConstraintProof.Refuted)
            {
                if (proof != ConstraintProof.Proven)
                {
                    this.Failure = "A closed virtual slot requires a completed applicability proof.";
                    return null;
                }

                if ((abi = this.Entry(choice.Function, choice.Declaring)) is null ||
                    this.Entry(choice.Slot.Original, choice.Slot.DeclaringType, implementationBody: false) is not { } contract || !SameAbi(contract, abi))
                {
                    return null;
                }
            }

            if (changed is null && !ReferenceEquals(layout.Table[i], abi))
            {
                changed = layout.Table.AsSpan().ToArray();
            }

            if (changed is not null)
            {
                changed[i] = abi;
            }
        }

        layout.Table = changed ?? layout.Table;
        layout.Prepared = true;
        return layout.Table;
    }

    // Only erasure needs an address. Known Item calls use the same indexed instruction as member calls.
    internal FunctionAbi? Address(FunctionKoto original, FunctionAbi? body = null)
    {
        body ??= this.functions!.GetValueOrDefault(original);
        if (body is null)
        {
            return null;
        }

        if (this.addresses.TryGetValue(body, out var known))
        {
            return known;
        }

        if (!this.TrySlot(original, body, out var slot, out var receiver))
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

        this.addresses.Add(body, entry);
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
        => original.Result == implementation.Result && original.ResultSlot == implementation.ResultSlot && original.NoReturn == implementation.NoReturn &&
            original.CallerLocation == implementation.CallerLocation && original.Parameters.AsSpan().SequenceEqual(implementation.Parameters);

    // Bind all replacements before requesting bodies. An overridden base body is not a dependency
    // merely because its signature contributes to the inherited table prefix.
    private Layout? GetLayout(BoundType type)
    {
        if (this.layouts.TryGetValue(type, out var known))
        {
            return known;
        }

        var parent = AdtDef.Base(type);
        var inherited = parent is null ? null : this.GetLayout(parent);
        if (parent is not null && inherited is null)
        {
            return null;
        }

        var ordinal = this.layouts.Count;
        if (ordinal == this.layoutPool.Count)
        {
            this.layoutPool.Add(new());
        }

        var layout = this.layoutPool[ordinal];
        layout.Choices.Clear();
        if (inherited is not null)
        {
            layout.Choices.AddRange(inherited.Choices);
        }

        var members = this.declarationsByType.GetValueOrDefault(type.Symbol!);
        for (var d = 0; members is not null && d < members.Count; d++)
        {
            var function = members[d];
            if (function.IsVirtual)
            {
                if (!this.slots.TryGetValue(function, out var slot) || slot != layout.Choices.Count)
                {
                    return null;
                }

                layout.Choices.Add(new(function, type, new(function, type)));
            }
            else
            {
                if (!this.binding!.TryGetVirtualOverride(function, out var implementation) ||
                    !this.slots.TryGetValue(implementation.Slot.Original, out var slot) || slot >= layout.Choices.Count)
                {
                    return null;
                }

                layout.Choices[slot] = layout.Choices[slot] with { Function = function, Declaring = type };
            }
        }

        this.layouts.Add(type, layout);
        return layout;
    }

    private int Shape(BoundType type)
    {
        if (type.Symbol is not { Declaration: StructKoto } symbol)
        {
            return -1;
        }

        if (this.shapes.TryGetValue(symbol, out var count))
        {
            return count;
        }

        count = AdtDef.Base(type) is { } parent ? this.Shape(parent) : 0;
        if (count < 0)
        {
            return -1;
        }

        var members = this.declarationsByType.GetValueOrDefault(symbol);
        for (var i = 0; members is not null && i < members.Count; i++)
        {
            if (members[i].IsVirtual)
            {
                this.slots.Add(members[i], count++);
            }
        }

        this.shapes.Add(symbol, count);
        return count;
    }

    private sealed class Layout
    {
        internal List<Choice> Choices { get; } = new();

        internal bool Prepared { get; set; }

        internal FunctionAbi?[] Table { get; set; } = [];
    }

    private readonly record struct Choice(FunctionKoto Function, BoundType Declaring, Binding.VirtualSlot Slot);
}
