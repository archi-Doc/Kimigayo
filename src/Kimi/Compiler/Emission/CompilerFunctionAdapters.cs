// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// One physical entry per closed compiler Function Item. Entries forward existing primitives; no source algorithm
// or captured call-site location is duplicated. Only physical signature records survive a preparation.
internal sealed class CompilerFunctionAdapters
{
    private readonly Dictionary<BoundType, FunctionAbi> entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionAbi> signatures = new();
    private readonly List<string> names = new();
    private readonly List<EmissionOperand> operands = new();
    private Compilation? compilation;
    private GenericStoragePlan? generics;
    private ObjectGenerationPlan? objects;
    private BodyLowering? lowering;
    private Binding? binding;
    private EmissionModule? module;
    private AggregateLayoutPool? layouts;

    internal void Begin(Compilation compilation, EmissionModule module, BodyLowering lowering, GenericStoragePlan generics, ObjectGenerationPlan objects)
    {
        this.Clear();
        this.compilation = compilation;
        this.binding = compilation.Binding;
        this.module = module;
        this.lowering = lowering;
        this.layouts = lowering.AggregateLayouts;
        this.generics = generics;
        this.objects = objects;
    }

    internal void Clear()
    {
        this.entries.Clear();
        this.compilation = null;
        this.generics = null;
        this.objects = null;
        this.lowering = null;
        this.binding = null;
        this.module = null;
        this.layouts = null;
        this.operands.Clear();
    }

    internal FunctionAbi? Get(BoundType item)
    {
        if (this.entries.TryGetValue(item, out var existing))
        {
            return existing;
        }

        if (this.binding is null || this.module is null || this.layouts is null ||
            this.binding.FunctionItemSignature(item) is not { } signature || item.Symbol is not { } symbol)
        {
            return null;
        }

        var inputs = (BoundType[])signature.Components[0].Components;
        var result = signature.Components[1];
        var context = this.binding.FunctionItemContext(item);
        if (context is null || !this.generics!.PrepareFormatting(this.compilation!, this.module, this.layouts, context, out _) ||
            !this.generics.PrepareComparison(this.compilation!, this.module, this.layouts, context, out _, includeScalar: true) ||
            !this.objects!.AddCall(this.compilation!, this.module, this.layouts, context, out _))
        {
            return null;
        }

        symbol = context.Target;
        var kind = symbol.CompilerFunction;

        this.operands.Clear();
        var array = KimiLibraryCatalog.IsArrayOperation(kind);
        var runtime = array ? this.lowering!.CompilerArrayEntry(kind, inputs, result, this.operands) :
            kind == CompilerFunctionKind.RawAllocate ? WindowsLowering.RawAllocate :
            kind == CompilerFunctionKind.Clone ? ObjectTypes.HandleMode(result)?.Counting switch
            {
                ObjectCountingStep.NonAtomic => WindowsLowering.CloneRc,
                ObjectCountingStep.Atomic => WindowsLowering.CloneArc,
                _ => null,
            } : this.generics.ComparisonCalls.GetValueOrDefault(context) ?? this.objects.Calls.GetValueOrDefault(context).Physical?.Abi ??
                (kind is CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat or CompilerFunctionKind.WriterWrite ? this.generics.FormattingCalls.GetValueOrDefault(context) : null) ??
                WindowsLowering.GetCompilerFunction(kind);
        var update = kind is CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap;
        var arithmetic = kind == CompilerFunctionKind.BuiltinArithmetic;
        if ((!update && !arithmetic && runtime is null) || !FunctionAbi.Supports(result, this.layouts))
        {
            return null;
        }

        foreach (var input in inputs)
        {
            if (!FunctionAbi.Supports(input, this.layouts))
            {
                return null;
            }
        }

        var ordinal = this.entries.Count;
        var slot = FunctionAbi.HasResultSlot(result, this.layouts);
        FunctionAbi abi;
        if (ordinal < this.signatures.Count && FunctionAbiPool.Matches(this.signatures[ordinal], result, inputs, slot, this.layouts, callerLocation: true))
        {
            abi = this.signatures[ordinal];
        }
        else
        {
            if (ordinal == this.names.Count)
            {
                this.names.Add("__kimi_compiler_item" + ordinal.ToString(CultureInfo.InvariantCulture));
            }

            abi = FunctionAbiPool.Build(this.names[ordinal], result, inputs, slot, this.layouts, callerLocation: true);
            if (ordinal == this.signatures.Count)
            {
                this.signatures.Add(abi);
            }
            else
            {
                this.signatures[ordinal] = abi;
            }
        }

        this.entries.Add(item, abi);
        var function = this.module.AddFunction(abi, exported: false);
        if (symbol.CompilerFunction == CompilerFunctionKind.RawAllocate)
        {
            if (result.Components.Count != 1 || FunctionAbi.GetValue(result.Components[0], this.layouts) is not { } element || element.Layout.Alignment > 16)
            {
                return null;
            }

            this.operands.Add(new(EmissionOperandKind.Argument, 0));
            this.operands.Add(new(EmissionOperandKind.Integer, element.Layout.Stride));
            this.operands.Add(new(EmissionOperandKind.CallerLocation, 0));
            this.operands.Add(new(EmissionOperandKind.CallerLocationLength, 0));
        }
        else if (!update && !array && !arithmetic)
        {
            foreach (var physical in runtime!.Parameters)
            {
                if (physical.Kind == AbiParameterKind.Context)
                {
                    if (!this.ContextOperand(context, function, physical, inputs, result, out var operand))
                    {
                        return null;
                    }

                    this.operands.Add(operand);
                    continue;
                }

                if (physical.Kind is not (AbiParameterKind.ResultSlot or AbiParameterKind.Location or AbiParameterKind.LocationLength) && (uint)physical.LogicalIndex >= (uint)inputs.Length)
                {
                    return null;
                }

                this.operands.Add(physical.Kind switch
                {
                    AbiParameterKind.ResultSlot => new(EmissionOperandKind.ReturnAddress, 0),
                    AbiParameterKind.Location => new(EmissionOperandKind.CallerLocation, 0),
                    AbiParameterKind.LocationLength => new(EmissionOperandKind.CallerLocationLength, 0),
                    _ => new(EmissionOperandKind.Argument, physical.LogicalIndex),
                });
            }
        }

        if (arithmetic)
        {
            if (context.ConformingType is not { } self || ArithmeticContracts.Identity(symbol.Scope.Owner.BoundSymbol) is not { } identity ||
                !ArithmeticContracts.Supports(self, identity) || !NumericArithmetic.EmitBorrowed(function, self, ArithmeticContracts.Operator(identity), 0, 1, 3, -1, new(EmissionOperandKind.Argument, 0), new(EmissionOperandKind.Argument, 1)))
            {
                return null;
            }
        }
        else if (update)
        {
            if (!this.lowering!.LowerCompilerUpdate(function, kind, inputs))
            {
                return null;
            }
        }
        else
        {
            function.AddCall(0, runtime!, CollectionsMarshal.AsSpan(this.operands));
        }

        if (abi.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, 0);
        }
        else if (abi.Result == "void")
        {
            function.Add(EmissionOpcode.ReturnVoid, 1);
        }
        else
        {
            function.AddScalar(EmissionOpcode.ReturnScalar, 1, [new(EmissionOperandKind.Value, 0)], abi.Result);
        }

        this.module.NeedsObjectRuntime |= kind == CompilerFunctionKind.Clone;
        this.module.NeedsFormattingRuntime |= symbol.CompilerFunction is >= CompilerFunctionKind.TextFixed and <= CompilerFunctionKind.BuiltinFormat;
        return abi;
    }

    private bool ContextOperand(BoundCall call, EmissionFunction function, AbiParameter physical, BoundType[] inputs, BoundType result, out EmissionOperand operand)
    {
        operand = default;
        var kind = call.Target.CompilerFunction;
        if (kind is CompilerFunctionKind.MakeObj or CompilerFunctionKind.MakeRc or CompilerFunctionKind.MakeArc)
        {
            operand = new(EmissionOperandKind.Integer, result.Semantics == SemanticsKind.Obj ? 0 : 2);
            return true;
        }

        if (physical.Type == "i32" && kind is CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat)
        {
            var input = kind == CompilerFunctionKind.WriterWrite ? 1 : 0;
            var format = inputs[input].Components.Count == 1 ? BodyLowering.BuiltinFormatKind(inputs[input].Components[0]) : -1;
            operand = new(EmissionOperandKind.Integer, format);
            return format >= 0;
        }

        if (kind is CompilerFunctionKind.TextFixed or CompilerFunctionKind.TextTryFormat)
        {
            var input = kind == CompilerFunctionKind.TextFixed ? 0 : 1;
            if (inputs[input].Components is [{ Kind: BoundTypeKind.FixedArray, Length: >= 0 } array])
            {
                operand = new(EmissionOperandKind.Integer, array.Length);
                return true;
            }
        }

        if (kind == CompilerFunctionKind.TextWriter &&
            this.lowering!.PrepareWriterDispatch(call, function, result, out var writer, out var dispatch, out _))
        {
            operand = physical.Type == "i64" ? new(EmissionOperandKind.Integer, writer) : dispatch;
            return true;
        }

        return false;
    }
}
