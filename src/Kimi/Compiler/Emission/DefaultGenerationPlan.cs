// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 7.2.3: recursive omitted evaluation has one entry per declaration/default and closed substitution. Prepared inputs
// remain owned by the pending call. Registration precedes body generation, so finite recursive keys reuse their entry.
internal sealed class DefaultGenerationPlan
{
    private readonly List<Entry> pool = new();
    private readonly List<string> names = new();
    private BoundType[] parameterScratch = [];
    private int count;
    private int lowered;

    internal Entry? Parent { get; set; }

    internal bool ResourceLimitExceeded { get; private set; }

    internal bool HasPending => this.lowered < this.count;

    internal void Clear()
    {
        this.count = this.lowered = 0;
        this.Parent = null;
        this.ResourceLimitExceeded = false;
    }

    internal Entry Next() => this.pool[this.lowered++];

    internal Entry? Get(OwnershipBody body, OwnershipBody.DefaultEvaluation evaluation, AggregateLayoutPool layouts, out string? failure)
    {
        failure = null;
        var binding = body.Function.CodeContext.Compilation.Binding;
        var call = body.Instance is { } outer ? binding.InstantiateDefaultCall(evaluation.Call, outer) : evaluation.Call;
        if (call?.Target.Declaration is not FunctionKoto function)
        {
            failure = "Default evaluator requires its resolved declaration.";
            return null;
        }

        if (this.parameterScratch.Length < evaluation.Parameter)
        {
            Array.Resize(ref this.parameterScratch, Math.Max(evaluation.Parameter, this.parameterScratch.Length * 2));
        }

        var parameters = this.parameterScratch.AsSpan(0, evaluation.Parameter);
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = binding.InstantiateStorageType(function.Parameters[i].Type.BoundType!, call);
            if (type is null || !FunctionAbi.Supports(type, layouts))
            {
                failure = "Default evaluator input has no concrete representation.";
                return null;
            }

            parameters[i] = type;
        }

        var result = binding.InstantiateStorageType(function.Parameters[evaluation.Parameter].Type.BoundType!, call);
        if (result is null || !FunctionAbi.Supports(result, layouts))
        {
            failure = "Default evaluator result has no concrete representation.";
            return null;
        }

        var sameDeclaration = 0;
        for (var i = 0; i < this.count; i++)
        {
            var candidate = this.pool[i];
            if (ReferenceEquals(candidate.Function, function) && candidate.Parameter == evaluation.Parameter)
            {
                sameDeclaration++;
                if (Binding.SameModuloOpenOrigins(candidate.Parameters, parameters) && Binding.SameModuloOpenOrigins(candidate.Result, result) &&
                    Binding.SameModuloOpenOrigins(candidate.Call.TypeArguments, call.TypeArguments) && candidate.Call.LengthArguments.SequenceEqual(call.LengthArguments) &&
                    ReferenceEquals(candidate.Call.DeclaringType, call.DeclaringType))
                {
                    return candidate;
                }
            }
        }

        var depth = 0;
        for (var parent = this.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent.Function, function) && parent.Parameter == evaluation.Parameter)
            {
                depth++;
            }
        }

        if (sameDeclaration >= GenericStoragePlan.SubstitutionSetLimit || depth >= 16)
        {
            this.ResourceLimitExceeded = true;
            failure = "Recursive default evaluation exceeds the bounded set of closed substitutions.";
            return null;
        }

        while (this.names.Count <= this.count)
        {
            this.names.Add("__kimi_default" + this.names.Count.ToString(CultureInfo.InvariantCulture));
        }

        var slot = FunctionAbi.HasResultSlot(result, layouts);
        var previous = this.count < this.pool.Count ? this.pool[this.count] : null;
        Entry entry;
        if (previous is not null && ReferenceEquals(previous.Function, function) && previous.Parameter == evaluation.Parameter &&
            previous.Parameters.AsSpan().SequenceEqual(parameters) && ReferenceEquals(previous.Result, result) &&
            previous.Abi.ResultSlot == slot && previous.Abi.Result == FunctionAbi.ResultType(result, layouts))
        {
            entry = previous;
            entry.Call = call;
        }
        else
        {
            entry = new(function, evaluation.Parameter, call, parameters.ToArray(), result, BuildAbi(this.names[this.count], result, parameters.Length, slot, layouts));
        }

        entry.Parent = this.Parent;
        if (this.count == this.pool.Count)
        {
            this.pool.Add(entry);
        }
        else
        {
            this.pool[this.count] = entry;
        }

        this.count++;
        return entry;
    }

    // Every input denotes the original prepared slot, including scalar/reference and zero-sized slots. A value copy would
    // change observable address identity between defaults (SPEC 5.4, 7.2.3). The evaluator never acquires these parameters.
    private static FunctionAbi BuildAbi(string name, BoundType result, int count, bool resultSlot, AggregateLayoutPool layouts)
    {
        var parameters = new AbiParameter[count + (resultSlot ? 1 : 0)];
        var offset = 0;
        if (resultSlot)
        {
            parameters[offset++] = new("ptr", "ret", AbiParameterKind.ResultSlot);
        }

        for (var i = 0; i < count; i++)
        {
            parameters[offset + i] = new("ptr", "a" + i.ToString(CultureInfo.InvariantCulture), AbiParameterKind.PreparedSlot, i);
        }

        return new(name, FunctionAbi.ResultType(result, layouts)!, parameters, resultSlot: resultSlot);
    }

    internal sealed class Entry(FunctionKoto function, int parameter, BoundCall call, BoundType[] parameters, BoundType result, FunctionAbi abi)
    {
        internal FunctionKoto Function { get; } = function;

        internal int Parameter { get; } = parameter;

        internal BoundCall Call { get; set; } = call;

        internal BoundType[] Parameters { get; } = parameters;

        internal BoundType Result { get; } = result;

        internal FunctionAbi Abi { get; } = abi;

        internal GenericStoragePlan.CallEntry? Context { get; set; }

        internal Entry? Parent { get; set; }
    }
}
