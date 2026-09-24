// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class GenericStoragePlan
{
    private readonly Dictionary<BoundCall, FunctionAbi> comparisonCalls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundComparison, FunctionAbi> comparisonHelpers = new(ReferenceEqualityComparer.Instance);

    internal IReadOnlyDictionary<BoundCall, FunctionAbi> ComparisonCalls => this.comparisonCalls;

    internal IReadOnlyDictionary<BoundComparison, FunctionAbi> ComparisonHelpers => this.comparisonHelpers;

    private bool PrepareDictionaryProjections(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, OwnershipBody body, BoundCall? context, out string? failure, int depth = 0)
    {
        failure = null;
        foreach (var projection in body.Projections)
        {
            if (body.Operations[projection.Operation].Source is not IndexKoto { Left.BoundType.Kind: BoundTypeKind.Dictionary } index)
            {
                continue;
            }

            var dictionary = context is null ? index.Left.BoundType : compilation.Binding.InstantiateStorageType(index.Left.BoundType, context);
            if (dictionary is null || compilation.Binding.DictionaryComparison(dictionary) is not { } comparison ||
                !this.PrepareComparisonHelper(compilation, module, layouts, comparison, out _, out failure, depth))
            {
                return Fail(failure ?? "Dictionary indexing requires a finalized equality witness.", out failure);
            }
        }

        return true;
    }

    private bool PrepareComparison(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall site, out string? failure, int depth = 0)
    {
        failure = null;
        if ((!Binding.HasDictionarySearch(site) && (site.Target.CompilerFunction is not (CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare) ||
            !ComparisonTypes.IsComposite(site.ConformingType))) || this.comparisonCalls.ContainsKey(site))
        {
            return true;
        }

        if (compilation.Binding.ComparisonPlan(site) is not { } plan || !this.PrepareComparisonHelper(compilation, module, layouts, plan, out var abi, out failure, depth))
        {
            return Fail(failure ?? "Composite comparison requires a verified recursive witness plan.", out failure);
        }

        this.comparisonCalls.TryAdd(site, abi!);
        return true;
    }

    private bool PrepareComparisonHelper(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundComparison plan, out FunctionAbi? abi, out string? failure, int depth)
    {
        failure = null;
        if (this.comparisonHelpers.TryGetValue(plan, out abi))
        {
            return true;
        }

        if (depth > ContextDepthLimit)
        {
            this.ResourceLimitExceeded = true;
            return Fail("Comparison witness composition exceeds the generation depth limit.", out failure);
        }

        // Publish helper signatures before traversing implementations and children.
        // A generic leaf may recursively call this same composite comparison.
        if (plan.Implementation is null || (!plan.Equality && plan.Operators))
        {
            abi = new("__kimi_comparison" + this.comparisonHelpers.Count, plan.Equality ? "i1" : "i32", [new("ptr", "a0", LogicalIndex: 0), new("ptr", "a1", LogicalIndex: 1)]);
            this.comparisonHelpers.Add(plan, abi);
        }

        FunctionAbi? implementation = null;
        if (plan.Implementation is { Target.Declaration: FunctionKoto target } call)
        {
            if (IsGeneric(target))
            {
                if (!this.templates.TryGetValue(target, out var template) || !this.PrepareEntry(compilation, module, layouts, call, template, out var entry, out failure, depth + 1))
                {
                    return Fail(failure ?? "Comparison witness requires a verified generic implementation.", out failure);
                }

                implementation = entry!.Selected ?? entry.Abi;
            }
            else
            {
                implementation = this.functions!.GetValueOrDefault(target);
            }

            if (implementation is not { Parameters.Length: 2, ResultSlot: false, NoReturn: false } ||
                implementation.Result != (plan.Equality ? "i1" : "i32") || implementation.Parameters[0].Type != "ptr" || implementation.Parameters[1].Type != "ptr")
            {
                return Fail("Comparison witness has no verified physical signature.", out failure);
            }

            if (plan.Equality || !plan.Operators)
            {
                this.comparisonHelpers.TryAdd(plan, implementation);
                abi = this.comparisonHelpers[plan];
                return true;
            }
        }

        var children = new FunctionAbi[plan.Parts.Length];
        for (var i = 0; i < children.Length; i++)
        {
            if (!this.PrepareComparisonHelper(compilation, module, layouts, plan.Parts[i], out var child, out failure, depth + 1))
            {
                return false;
            }

            children[i] = child!;
        }

        var function = module.AddFunction(abi!, false);
        var next = 0;
        EmissionOperand left = new(EmissionOperandKind.Argument, 0);
        EmissionOperand right = new(EmissionOperandKind.Argument, 1);
        if (plan.Type.Kind == BoundTypeKind.Tuple)
        {
            if (layouts.Get(plan.Type) is not { } layout)
            {
                return Fail("Tuple comparison has no concrete layout.", out failure);
            }

            for (var i = 0; i < children.Length; i++)
            {
                var a = Address(left, layout.Offset(i), layout.Fields[i]);
                var b = Address(right, layout.Offset(i), layout.Fields[i]);
                var value = next++;
                function.AddCall(value, children[i], [a, b]);
                var condition = value;
                if (!plan.Equality)
                {
                    condition = next++;
                    function.AddScalar(EmissionOpcode.Scalar, condition, [new(EmissionOperandKind.Value, value), new(EmissionOperandKind.Integer, 0)], "i32", "eq", comparison: true);
                }

                var proceed = next++;
                var finish = next++;
                function.AddScalar(EmissionOpcode.ConditionalBranch, next++, [new(EmissionOperandKind.Value, condition), new(EmissionOperandKind.Block, proceed), new(EmissionOperandKind.Block, finish)]);
                function.Add(EmissionOpcode.Label, finish);
                function.AddScalar(EmissionOpcode.ReturnScalar, next++, [new(EmissionOperandKind.Value, value)], abi!.Result);
                function.Add(EmissionOpcode.Label, proceed);
            }

            function.AddScalar(EmissionOpcode.ReturnScalar, next++, [new(EmissionOperandKind.Integer, plan.Equality ? 1 : 0)], abi!.Result);
            return true;
        }

        var result = next++;
        if (children.Length == 1)
        {
            var a = next++;
            var b = next++;
            function.AddScalar(EmissionOpcode.LoadPointer, a, [left], "ptr", representation: WindowsLowering.StringReference);
            function.AddScalar(EmissionOpcode.LoadPointer, b, [right], "ptr", representation: WindowsLowering.StringReference);
            function.AddCall(result, children[0], [new(EmissionOperandKind.Value, a), new(EmissionOperandKind.Value, b)]);
        }
        else if (implementation is not null)
        {
            // Built-in Tuple ordering reserves 2 for unordered; arbitrary user return magnitudes
            // therefore become their sign before being composed with IEEE floating elements.
            var comparison = next++;
            function.AddCall(comparison, implementation, [left, right]);
            var less = next++;
            var greater = next++;
            function.AddScalar(EmissionOpcode.Scalar, less, [new(EmissionOperandKind.Value, comparison), new(EmissionOperandKind.Integer, 0)], "i32", "slt", comparison: true);
            function.AddScalar(EmissionOpcode.Scalar, greater, [new(EmissionOperandKind.Value, comparison), new(EmissionOperandKind.Integer, 0)], "i32", "sgt", comparison: true);
            var negative = next++;
            var positive = next++;
            function.AddScalar(EmissionOpcode.Convert, negative, [new(EmissionOperandKind.Value, less)], "i32", "zext", representation: WindowsLowering.GetValue(BoundType.Boolean));
            function.AddScalar(EmissionOpcode.Convert, positive, [new(EmissionOperandKind.Value, greater)], "i32", "zext", representation: WindowsLowering.GetValue(BoundType.Boolean));
            function.AddScalar(EmissionOpcode.Scalar, result, [new(EmissionOperandKind.Value, positive), new(EmissionOperandKind.Value, negative)], "i32", "sub");
        }
        else if (ReferenceEquals(plan.Type, BoundType.String))
        {
            module.NeedsStringComparison = true;
            function.AddScalar(plan.Equality ? EmissionOpcode.StringEquals : EmissionOpcode.StringCompare, result, [left, right], op: plan.Equality ? "eq" : "order");
        }
        else if (WindowsLowering.GetValue(plan.Type) is { } scalar)
        {
            var op = plan.Equality ? plan.Operators ? "ieee" : "equals" : scalar.ComputationType is "float" or "double" ? "float" : ScalarTypes.Signed(plan.Type) ? "s" : "u";
            function.AddScalar(EmissionOpcode.BuiltinComparison, result, ReferenceEquals(plan.Type, BoundType.Unit) ? [] : [left, right], scalar.Layout.StorageType, op, representation: scalar);
        }
        else
        {
            return Fail("Comparison leaf has no concrete representation.", out failure);
        }

        function.AddScalar(EmissionOpcode.ReturnScalar, next, [new(EmissionOperandKind.Value, result)], abi!.Result);
        return true;

        EmissionOperand Address(EmissionOperand owner, int offset, ValueLowering field)
        {
            if (field.Layout.Size == 0)
            {
                return owner;
            }

            var id = next++;
            function.AddScalar(EmissionOpcode.ElementAddress, id, [owner, new(EmissionOperandKind.Integer, offset)], representation: field);
            return new(EmissionOperandKind.ElementAddress, id);
        }
    }
}
