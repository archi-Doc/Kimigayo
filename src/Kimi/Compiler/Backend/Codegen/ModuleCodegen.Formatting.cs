// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The formatting wrappers of ModuleCodegen (R5 plan C9b; SPEC 22.9.2, 22.10.1; DECISIONS R5-M30, R5-M130): a user or library formatter, the
// Body target of a kind-bearing formatting row's call (WriterWrite, TextToString, TextTryFormat; K package 4.5 row 2), runs through the
// row's wrapper over that target (FunctionCodegen.Formatting), one per target and row, named after the target item (__kimi_write<item>,
// __kimi_tostring<item>, __kimi_tryformat<item>), kept across Writes and defined once in each Write that requests it, after the items,
// with the formatting runtime its row declares. The write wrapper takes the call's result slot, adapter and value (the old planner's ABI;
// no format witness takes a location, since none is caller-location) and is written by FunctionCodegen (WriteWrapper: SPEC 22.10.1's frame
// around the witness's call through BodyRun); a conversion takes its row's routine's parameters less the kind Context, which the target
// replaces, and is the old generator's text over the target's write wrapper (LegacyRuntimeHelpers.WriteFormattingConversion, PD2), which a
// conversion's request also requests.
internal sealed partial class ModuleCodegen
{
    // The kind-bearing formatting rows in wrapper order, the write first (a conversion calls it), and their wrappers' name prefixes.
    private static readonly CompilerFunctionKind[] Wrapped =
        [CompilerFunctionKind.WriterWrite, CompilerFunctionKind.TextToString, CompilerFunctionKind.TextTryFormat];

    private static readonly string[] WrapperPrefixes = ["__kimi_write", "__kimi_tostring", "__kimi_tryformat"];

    // The write wrapper's parameters: the result slot, the adapter and the value at their logical inputs.
    private static readonly AbiParameter[] WriteParameters =
        [new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "self", LogicalIndex: 0), new("ptr", "value", LogicalIndex: 1)];

    private readonly List<int> wrapped = []; // The latest Write's requested wrappers by index (Wrapped.Length per target item), in request order.
    private FunctionAbi?[] wrappers = []; // By index, the kept wrapper.
    private int[] wrappedIn = []; // By index, the Write that requested it.

    /// <summary>Gets the wrapper that runs a kind-bearing formatting row's call over its Body target, requested for this Write: a
    /// conversion's request also requests the target's write wrapper, which it calls.</summary>
    /// <param name="target">The Body target, the formatter's item.</param>
    /// <param name="routine">The row's routine (LegacyRuntimeHelpers.FormattingRow), which names the row.</param>
    /// <returns>The wrapper's signature.</returns>
    internal FunctionAbi Wrapper(int target, FunctionAbi routine)
    {
        var row = 0;
        while (row < Wrapped.Length && !ReferenceEquals(LegacyRuntimeHelpers.FormattingRow(Wrapped[row])!.Abi, routine))
        {
            row++;
        }

        var index = row < Wrapped.Length ? (Wrapped.Length * target) + row
            : throw new InvalidOperationException("Only a kind-bearing formatting row runs a wrapper over its target.");
        if (index >= this.wrappers.Length)
        {
            Array.Resize(ref this.wrappers, Math.Max(index + Wrapped.Length, 2 * this.wrappers.Length));
            Array.Resize(ref this.wrappedIn, this.wrappers.Length);
        }

        if (this.wrappedIn[index] != this.writes)
        {
            this.wrappedIn[index] = this.writes;
            this.wrappers[index] ??= WrapperSignature(target, row, routine);
            this.Request(LegacyRuntimeHelpers.FormattingRow(Wrapped[row])!.Needs);
            this.wrapped.Add(index);
            if (row > 0)
            {
                _ = this.Wrapper(target, LegacyRuntimeHelpers.FormattingRow(Wrapped[0])!.Abi);
            }
        }

        return this.wrappers[index]!;
    }

    // A wrapper's signature, named after its target item: the write wrapper's parameters, or a conversion's row routine's less its kind
    // Context (the parameter without an operand, which names none).
    private static FunctionAbi WrapperSignature(int target, int row, FunctionAbi routine)
    {
        var name = WrapperPrefixes[row] + target.ToString(CultureInfo.InvariantCulture);
        if (row == 0)
        {
            return new(name, "void", WriteParameters) { ResultSlot = true };
        }

        var parameters = new List<AbiParameter>(routine.Parameters.Length);
        foreach (var parameter in routine.Parameters)
        {
            if (parameter is not { Kind: AbiParameterKind.Context, LogicalIndex: < 0 })
            {
                parameters.Add(parameter);
            }
        }

        return new(name, "void", [.. parameters]) { ResultSlot = true };
    }

    // The definitions of the latest Write's requested wrappers, in request order: a write wrapper by FunctionCodegen, a conversion the old
    // generator's text over its target's write wrapper.
    private void WriteWrappers(InstanceCollector collector)
    {
        for (var i = 0; i < this.wrapped.Count; i++)
        {
            var (target, row) = Math.DivRem(this.wrapped[i], Wrapped.Length);
            var wrapper = this.wrappers[this.wrapped[i]]!;
            if (row == 0)
            {
                this.bodies.WriteWrapper(collector, target, wrapper, this.functions);
            }
            else
            {
                var write = this.wrappers[Wrapped.Length * target]!;
                LegacyRuntimeHelpers.WriteFormattingConversion(this.functions, wrapper, write, Wrapped[row] == CompilerFunctionKind.TextTryFormat);
            }
        }
    }
}
