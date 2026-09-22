// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>
/// Per-substitution entries of universally verified generic bodies (SPEC 21.3.1). Each closed call
/// context receives a caller-facing entry ABI and the calls its body forwards under that substitution;
/// <c>LlvmEmitter.LowerInstances</c> analyzes and lowers every entry as its own concrete instance.
/// </summary>
internal sealed class GenericStoragePlan
{
    // SPEC 21.3.5: a growing substitution key (T -> Box<T>) re-enters one template with ever new keys;
    // finite recursion reuses its registered entry long before this bound.
    private const int GrowingKeyLimit = 16;
    private const int ContextDepthLimit = 128;

    private readonly Dictionary<FunctionKoto, Template> templates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundCall, CallEntry> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionKoto, int> chainCounts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionKoto, int> entryCounts = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<FunctionKoto, FunctionAbi>? functions;
    private int entryNames;

    /// <summary>Gets or sets the mandatory bound on distinct closed contexts one generic body may generate (SPEC 21.3.5).</summary>
    internal static int SubstitutionSetLimit { get; set; } = 1024;

    internal IReadOnlyDictionary<BoundCall, CallEntry> Calls => this.calls;

    /// <summary>Gets a value indicating whether the last failure exceeded a mandatory generation resource limit (SPEC 21.3.5).</summary>
    internal bool ResourceLimitExceeded { get; private set; }

    internal static bool IsGeneric(FunctionKoto function)
        => !function.IsSpecialization && (function.GenericArguments.Count != 0 || (!function.IsDestructor && function.BoundSymbol?.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 }));

    internal void Clear()
    {
        this.templates.Clear();
        this.calls.Clear();
        this.chainCounts.Clear();
        this.entryCounts.Clear();
        this.ResourceLimitExceeded = false;
        this.functions = null;
        this.entryNames = 0;
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, IReadOnlyDictionary<FunctionKoto, FunctionAbi> functions, out string? failure)
    {
        this.Clear();
        this.functions = functions;
        failure = null;
        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            if (!IsGeneric(body.Function))
            {
                continue;
            }

            if (!body.IsVerified || body.Function.IsAnonymous || body.Function.AttributeChain is not null)
            {
                return Fail("Generic generation requires a verified ordinary definition without captures or declaration attributes.", out failure);
            }

            this.templates.Add(body.Function, CreateTemplate(body));
        }

        if (this.templates.Count == 0)
        {
            return true;
        }

        foreach (var body in compilation.Ownership.Bodies)
        {
            if (IsGeneric(body.Function))
            {
                continue; // Dependent calls receive a concrete context from their caller's entry.
            }

            foreach (var operation in body.Operations)
            {
                if (operation.Kind != OwnershipOperationKind.Call || operation.Source is not InvocationKoto { BoundCall: { } call } ||
                    call.Target.Declaration is not FunctionKoto target || !this.templates.TryGetValue(target, out var template) || this.calls.ContainsKey(call))
                {
                    continue;
                }

                if (!this.PrepareEntry(compilation, module, layouts, call, template, out _, out failure))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Fail(string reason, out string? failure)
    {
        failure = reason;
        return false;
    }

    // The template records the calls its instances forward; each instance resolves them under its substitution.
    private static Template CreateTemplate(OwnershipBody body)
    {
        var calls = new List<BoundCall>();
        foreach (var operation in body.Operations)
        {
            if (operation.Kind == OwnershipOperationKind.Call && operation.Source is InvocationKoto { BoundCall: { } call } &&
                call.Target.CompilerFunction == CompilerFunctionKind.None && !calls.Contains(call))
            {
                calls.Add(call);
            }
        }

        return new(body, calls.ToArray());
    }

    private bool PrepareEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, out CallEntry? entry, out string? failure, int depth = 0)
    {
        var function = template.Body.Function;
        this.chainCounts.TryGetValue(function, out var chain);
        if (chain >= GrowingKeyLimit)
        {
            entry = null;
            this.ResourceLimitExceeded = true;
            return Fail($"Generic instantiation of '{function.Name}' grows without bound: a substitution key such as T -> Box<T> re-enters it more than {GrowingKeyLimit} times.", out failure);
        }

        this.chainCounts[function] = chain + 1;
        try
        {
            return this.PrepareEntryCore(compilation, module, layouts, call, template, out entry, out failure, depth);
        }
        finally
        {
            this.chainCounts[function] = chain;
        }
    }

    private bool PrepareEntryCore(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, out CallEntry? entry, out string? failure, int depth)
    {
        entry = null;
        failure = null;
        if (depth > ContextDepthLimit)
        {
            this.ResourceLimitExceeded = true;
            return Fail($"Generic call context expansion exceeds the supported depth of {ContextDepthLimit}.", out failure);
        }

        var target = template.Body.Function;
        var binding = compilation.Binding;
        var parameters = new BoundType[target.Parameters.Count];
        var abiParameters = new List<AbiParameter>();
        var result = call.ReturnType;
        var noReturn = ReferenceEquals(result, BoundType.Never);
        if (FunctionAbi.GetValue(result, layouts) is null && !noReturn)
        {
            return Fail("Generic entry result has no concrete representation.", out failure);
        }

        var resultSlot = target.IsConstructor || FunctionAbi.HasResultSlot(result, layouts);
        if (resultSlot)
        {
            abiParameters.Add(new("ptr", "ret", AbiParameterKind.ResultSlot));
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var type = binding.InstantiateStorageType(target.Parameters[i].Type.BoundType!, call);
            var value = type is null ? null : FunctionAbi.GetValue(type, layouts);
            var borrowedStorage = (ReferenceTypes.IsStorage(type) || ReferenceTypes.IsString(type)) && type!.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && ReferenceTypes.IsStorage(target.Parameters[i].Type.BoundType);
            if (type is null || value is null || ((ReferenceTypes.IsString(type) || ReferenceTypes.IsStorage(type)) && !borrowedStorage) ||
                (borrowedStorage && FunctionAbi.GetValue(type.Components[0], layouts) is null))
            {
                return Fail("Generic entry parameter requires a concrete owned storage representation.", out failure);
            }

            parameters[i] = type;
            if (value.Layout.Size != 0)
            {
                // The same parameter kinds as concrete entries (FunctionAbiPool), so callers pass string references alike.
                abiParameters.Add(new(value.ArgumentType!, "a" + i, ReferenceTypes.IsString(type) ? AbiParameterKind.SharedReference : SlotTypes.IsResult(type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value, i));
            }
        }

        foreach (var existing in this.calls.Values)
        {
            if (ReferenceEquals(existing.Template, template) && ReferenceEquals(existing.Result, result) &&
                existing.Parameters.AsSpan().SequenceEqual(parameters) && ReferenceEquals(existing.DeclaringType, call.DeclaringType) && existing.Arguments.AsSpan().SequenceEqual(call.TypeArguments) && existing.Lengths.AsSpan().SequenceEqual(call.LengthArguments))
            {
                entry = existing;
                this.calls.Add(call, entry);
                return true;
            }
        }

        var function = template.Body.Function;
        this.entryCounts.TryGetValue(function, out var count);
        if (count >= SubstitutionSetLimit)
        {
            // SPEC 21.3.5: an oversized substitution set exceeds a mandatory generation limit.
            this.ResourceLimitExceeded = true;
            return Fail($"Generic instantiation of '{function.Name}' exceeds the substitution set limit of {SubstitutionSetLimit} distinct closed contexts.", out failure);
        }

        this.entryCounts[function] = count + 1;
        var abi = new FunctionAbi("__kimi_generic_entry" + this.entryNames++, FunctionAbi.ResultType(result, layouts)!, abiParameters.ToArray(), noReturn: noReturn, resultSlot: resultSlot);
        var selected = binding.SelectSpecialization(call);
        var selectedAbi = selected is null ? null : this.functions!.GetValueOrDefault(selected);
        if (selected is not null && selectedAbi is null)
        {
            return Fail("Selected specialization has no verified implementation ABI.", out failure);
        }

        entry = new(template, abi, selectedAbi, parameters, result, call.DeclaringType, call.TypeArguments.ToArray(), call.LengthArguments.ToArray(), new CallEntry?[template.DirectCalls.Length])
        {
            ConcreteCalls = new BoundCall[template.DirectCalls.Length],
        };
        this.calls.Add(call, entry);
        if (selected is not null)
        {
            // SPEC 21.3.4: the selected explicit specialization is the implementation; callers call its
            // ordinary ABI directly, so the generic body is not instantiated for this call.
            return true;
        }

        module.PendingEntries.Add(entry);
        for (var i = 0; i < template.DirectCalls.Length; i++)
        {
            var inner = binding.InstantiateForwardedCall(template.DirectCalls[i], call);
            if (inner?.Target.Declaration is not FunctionKoto innerTarget)
            {
                return Fail("Concrete requirement call lacks a verified implementation mapping.", out failure);
            }

            entry.ConcreteCalls[i] = inner;
            if (IsGeneric(innerTarget))
            {
                if (!this.templates.TryGetValue(innerTarget, out var innerTemplate) ||
                    !this.PrepareEntry(compilation, module, layouts, inner, innerTemplate, out entry.Direct[i], out failure, depth + 1))
                {
                    return Fail(failure ?? "Concrete requirement implementation has no verified generic body.", out failure);
                }
            }
            else if (this.functions!.GetValueOrDefault(innerTarget) is null)
            {
                return Fail("Concrete requirement implementation has no verified ABI.", out failure);
            }
        }

        return true;
    }

    /// <summary>A universally verified generic body and the calls its instances forward.</summary>
    internal sealed record Template(OwnershipBody Body, BoundCall[] DirectCalls);

    /// <summary>
    /// One closed call context: the caller-facing entry ABI (or the selected explicit specialization's ABI),
    /// the substituted signature, and Direct[i], the instance entry of Template.DirectCalls[i] under this
    /// substitution (null for concrete targets).
    /// </summary>
    internal sealed record CallEntry(Template Template, FunctionAbi Abi, FunctionAbi? Selected, BoundType[] Parameters, BoundType Result, BoundType? DeclaringType, BoundType?[] Arguments, BoundLength?[] Lengths, CallEntry?[] Direct)
    {
        internal BoundCall[]? ConcreteCalls { get; set; }
    }
}
