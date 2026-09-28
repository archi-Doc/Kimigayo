// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>
/// Per-substitution entries of universally verified generic bodies (SPEC 21.3.1). Each closed call
/// context receives a caller-facing entry ABI and the calls its body forwards under that substitution;
/// <c>LlvmEmitter.LowerInstances</c> analyzes and lowers every entry as its own concrete instance.
/// </summary>
internal sealed partial class GenericStoragePlan
{
    // SPEC 21.3.5: a growing substitution key (T -> Box<T>) re-enters one template with ever new keys;
    // finite recursion reuses its registered entry long before this bound.
    private const int GrowingKeyLimit = 16;
    private const int ContextDepthLimit = 128;
    private const int DefaultSubstitutionSetLimit = 1024;

    // A lowered bound set on the current thread only, so that concurrent compilations keep the default.
    [ThreadStatic]
    private static int substitutionSetLimitOverride;

    // Warm emission reuses the previous emission's templates and entries when they are unchanged, so an unchanged program
    // rebuilds no entry, entry name or physical signature; the current and previous sets rotate at each Clear.
    private readonly List<BoundCall> directScratch = new();
    private readonly List<string> entryNameCache = new();
    private readonly Dictionary<BoundCall, CallEntry> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundCall, FunctionAbi> formattingCalls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionAbi, FunctionAbi> formattingWrites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(FunctionAbi Write, CompilerFunctionKind Kind), FunctionAbi> formattingConversions = new();
    private readonly Dictionary<FunctionKoto, int> chainCounts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionKoto, int> entryCounts = new(ReferenceEqualityComparer.Instance);
    private Dictionary<FunctionKoto, Template> templates = new(ReferenceEqualityComparer.Instance);
    private Dictionary<FunctionKoto, Template> previousTemplates = new(ReferenceEqualityComparer.Instance);
    private List<CallEntry> entries = new();
    private List<CallEntry> previousEntries = new();
    private BoundType[] parameterScratch = new BoundType[8];
    private IReadOnlyDictionary<FunctionKoto, FunctionAbi>? functions;
    private int entryNames;

    /// <summary>Gets or sets the mandatory bound on distinct closed contexts one generic body may generate (SPEC 21.3.5) for
    /// compilations on the current thread.</summary>
    internal static int SubstitutionSetLimit
    {
        get => substitutionSetLimitOverride > 0 ? substitutionSetLimitOverride : DefaultSubstitutionSetLimit;
        set => substitutionSetLimitOverride = value;
    }

    internal Dictionary<BoundCall, CallEntry> Calls => this.calls; // The concrete type enumerates without allocation.

    internal IReadOnlyDictionary<BoundCall, FunctionAbi> FormattingCalls => this.formattingCalls;

    /// <summary>Gets a value indicating whether the last failure exceeded a mandatory generation resource limit (SPEC 21.3.5).</summary>
    internal bool ResourceLimitExceeded { get; private set; }

    internal static bool IsGeneric(FunctionKoto function)
        => !function.IsSpecialization && (function.GenericArguments.Count != 0 || function.BoundSymbol?.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 });

    internal void Clear()
    {
        if (this.templates.Count != 0)
        {
            (this.templates, this.previousTemplates) = (this.previousTemplates, this.templates);
        }

        if (this.entries.Count != 0)
        {
            (this.entries, this.previousEntries) = (this.previousEntries, this.entries);
        }

        this.templates.Clear();
        this.entries.Clear();
        this.calls.Clear();
        this.formattingCalls.Clear();
        this.formattingWrites.Clear();
        this.formattingConversions.Clear();
        this.comparisonCalls.Clear();
        this.comparisonHelpers.Clear();
        this.chainCounts.Clear();
        this.entryCounts.Clear();
        this.ResourceLimitExceeded = false;
        this.functions = null;
        this.entryNames = 0;
        this.destructorNames.Clear();
        this.destructorQueue.Clear();
        this.preparedDestructors = 0;
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, IReadOnlyDictionary<FunctionKoto, FunctionAbi> functions, out string? failure)
    {
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

            this.templates.Add(body.Function, this.GetTemplate(body));
        }

        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            if (IsGeneric(body.Function))
            {
                continue; // Dependent calls receive a concrete context from their caller's entry.
            }

            if (!this.PrepareDictionaryProjections(compilation, module, layouts, body, null, out failure))
            {
                return false;
            }

            for (var i = 0; i < body.Operations.Count; i++)
            {
                var operation = body.Operations[i];
                if (operation.Kind != OwnershipOperationKind.Call || operation.Source is not InvocationKoto { BoundCall: { } call })
                {
                    continue;
                }

                if (!this.PrepareFormatting(compilation, module, layouts, call, out failure) || !this.PrepareComparison(compilation, module, layouts, call, out failure))
                {
                    return false;
                }

                if (call.Target.Declaration is not FunctionKoto target || !this.templates.TryGetValue(target, out var template) || this.calls.ContainsKey(call))
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

    private static bool IsFormattingCallback(BoundCall call)
        => Binding.HasFormattingCallback(call);

    // The template records the calls its instances forward; each instance resolves them under its substitution. The previous
    // emission's template is reused when its body and forwarded calls are unchanged.
    private Template GetTemplate(OwnershipBody body)
    {
        var calls = this.directScratch;
        calls.Clear();
        for (var i = 0; i < body.Operations.Count; i++)
        {
            var operation = body.Operations[i];
            if (operation.Kind == OwnershipOperationKind.Call && operation.Source is InvocationKoto { BoundCall: { } call } &&
                (call.Target.CompilerFunction == CompilerFunctionKind.None || IsFormattingCallback(call) || KimiLibraryCatalog.IsDictionaryOperation(call.Target.CompilerFunction) || call.Target.CompilerFunction is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare) && !calls.Contains(call))
            {
                calls.Add(call);
            }
        }

        return this.previousTemplates.TryGetValue(body.Function, out var previous) && ReferenceEquals(previous.Body, body) &&
            previous.DirectCalls.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(calls)) ? previous : new(body, calls.ToArray());
    }

    private string EntryName(int index)
    {
        while (this.entryNameCache.Count <= index)
        {
            this.entryNameCache.Add("__kimi_generic_entry" + this.entryNameCache.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return this.entryNameCache[index];
    }

    private bool PrepareEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, out CallEntry? entry, out string? failure, int depth = 0)
    {
        failure = null;
        if (this.calls.TryGetValue(call, out entry))
        {
            return true; // An in-progress entry already has its complete physical signature.
        }

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
        if (this.parameterScratch.Length < target.Parameters.Count)
        {
            this.parameterScratch = new BoundType[Math.Max(target.Parameters.Count, this.parameterScratch.Length * 2)];
        }

        // The scratch is read only until the entry exists; nested entries prepared below reuse it.
        var parameters = this.parameterScratch.AsSpan(0, target.Parameters.Count);
        var result = call.ReturnType;
        var noReturn = ReferenceEquals(result, BoundType.Never);
        if (FunctionAbi.GetValue(result, layouts) is null && !noReturn)
        {
            return Fail("Generic entry result has no concrete representation.", out failure);
        }

        var resultSlot = target.IsConstructor || target.IsDestructor || FunctionAbi.HasResultSlot(result, layouts);
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = binding.InstantiateStorageType(target.Parameters[i].Type.BoundType!, call);
            var borrowedStorage = ReferenceTypes.IsStorage(type) || ReferenceTypes.IsString(type);
            if (type is null || FunctionAbi.GetValue(type, layouts) is null ||
                (borrowedStorage && FunctionAbi.GetValue(type.Components[0], layouts) is null))
            {
                return Fail("Generic entry parameter requires a concrete storage representation.", out failure);
            }

            parameters[i] = type;
        }

        foreach (var existing in this.calls.Values)
        {
            if (ReferenceEquals(existing.Template, template) && ReferenceEquals(existing.Result, result) &&
                existing.Parameters.AsSpan().SequenceEqual((ReadOnlySpan<BoundType>)parameters) && ReferenceEquals(existing.DeclaringType, call.DeclaringType) && existing.Arguments.AsSpan().SequenceEqual(call.TypeArguments) && existing.Lengths.AsSpan().SequenceEqual(call.LengthArguments))
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
        var selected = binding.SelectSpecialization(call);
        var selectedAbi = selected is null ? null : this.functions!.GetValueOrDefault(selected);
        if (selected is not null && selectedAbi is null)
        {
            return Fail("Selected specialization has no verified implementation ABI.", out failure);
        }

        // The entry's physical signature follows the ordinary function rule (FunctionAbiPool), so callers pass every argument alike.
        var name = this.destructorNames.TryGetValue(call, out var reserved) ? reserved : this.EntryName(this.entryNames++);
        entry = this.PreviousEntry(name, template, parameters, result, resultSlot, call, selectedAbi, layouts) ??
            new(template, FunctionAbiPool.Build(name, result, parameters, resultSlot, layouts), selectedAbi, parameters.ToArray(), result, call.DeclaringType, call.TypeArguments.ToArray(), call.LengthArguments.ToArray(), new CallEntry?[template.DirectCalls.Length])
            {
                ConcreteCalls = new BoundCall[template.DirectCalls.Length],
            };
        this.entries.Add(entry);
        this.calls.Add(call, entry);
        if (selected is not null)
        {
            // SPEC 21.3.4: the selected explicit specialization is the implementation; callers call its
            // ordinary ABI directly, so the generic body is not instantiated for this call.
            return true;
        }

        module.PendingEntries.Add(entry);
        if (!this.PrepareDictionaryProjections(compilation, module, layouts, template.Body, call, out failure, depth + 1))
        {
            return false;
        }

        for (var i = 0; i < template.DirectCalls.Length; i++)
        {
            entry.Direct[i] = null;
            var inner = binding.InstantiateForwardedCall(template.DirectCalls[i], call, entry.ConcreteCalls![i]);
            if (inner?.Target.Declaration is not FunctionKoto innerTarget)
            {
                return Fail("Concrete requirement call lacks a verified implementation mapping.", out failure);
            }

            entry.ConcreteCalls[i] = inner;
            if (inner.Target.CompilerFunction != CompilerFunctionKind.None)
            {
                if (!this.PrepareFormatting(compilation, module, layouts, inner, out failure, depth + 1) || !this.PrepareComparison(compilation, module, layouts, inner, out failure, depth + 1))
                {
                    return false;
                }

                continue; // A verified builtin requirement is lowered through its runtime ABI.
            }

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

    private bool PrepareFormatting(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall site, out string? failure, int depth = 0)
    {
        failure = null;
        if (!IsFormattingCallback(site) || this.formattingCalls.ContainsKey(site))
        {
            return true;
        }

        if (!compilation.Binding.TryResolveFormattingCallback(site, out var call))
        {
            return Fail("Formatting callback has no verified conformance witness.", out failure);
        }

        var writer = site.Target.CompilerFunction == CompilerFunctionKind.TextWriter;
        if (call is null)
        {
            return true;
        }

        if (call.Target.Declaration is not FunctionKoto target)
        {
            return Fail("Formatting callback has no verified conformance witness.", out failure);
        }

        FunctionAbi? abi;
        if (IsGeneric(target))
        {
            if (!this.templates.TryGetValue(target, out var template) ||
                !this.PrepareEntry(compilation, module, layouts, call, template, out var entry, out failure, depth + 1))
            {
                return Fail(failure ?? "Formatting callback has no verified generic body.", out failure);
            }

            abi = entry!.Selected ?? entry.Abi;
        }
        else
        {
            abi = this.functions!.GetValueOrDefault(target);
        }

        if (abi is not { Result: "void", NoReturn: false, ResultSlot: true, Parameters.Length: 3 } ||
            abi.Parameters[0].Kind != AbiParameterKind.ResultSlot || abi.Parameters[1].Type != "ptr" || abi.Parameters[2].Type != (writer ? "i64" : "ptr"))
        {
            return Fail("Formatting callback has no verified physical signature.", out failure);
        }

        if (!writer)
        {
            if (!this.formattingWrites.TryGetValue(abi, out var wrapper))
            {
                wrapper = new("__kimi_writer_write_user" + this.formattingWrites.Count, "void", [new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "self", LogicalIndex: 0), new("ptr", "value", LogicalIndex: 1)], resultSlot: true);
                this.formattingWrites.Add(abi, wrapper);
                module.FormattingWrites.Add(new(wrapper, abi));
            }

            abi = wrapper;
            if (site.Target.CompilerFunction is CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat)
            {
                abi = this.PrepareFormattingConversion(module, abi, site.Target.CompilerFunction);
            }
        }

        this.formattingCalls.Add(site, abi);
        return true;
    }

    private FunctionAbi PrepareFormattingConversion(EmissionModule module, FunctionAbi write, CompilerFunctionKind kind)
    {
        if (this.formattingConversions.TryGetValue((write, kind), out var result))
        {
            return result;
        }

        var fixedBuffer = kind == CompilerFunctionKind.TextTryFormat;
        var ret = new AbiParameter("ptr", "ret", AbiParameterKind.ResultSlot);
        var value = new AbiParameter("ptr", "value", LogicalIndex: 0);
        var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
        var length = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
        AbiParameter[] parameters = fixedBuffer ? [ret, value, new("ptr", "destination", LogicalIndex: 1), new("i64", "capacity", AbiParameterKind.Context), location, length] : [ret, value, location, length];
        result = new("__kimi_format_conversion" + this.formattingConversions.Count, "void", parameters, resultSlot: true);
        this.formattingConversions.Add((write, kind), result);
        module.FormattingConversions.Add((result, write, fixedBuffer));
        return result;
    }

    /// <summary>A universally verified generic body and the calls its instances forward.</summary>
    // The previous emission's entry of the same name, template and substitution whose physical signature still holds.
    private CallEntry? PreviousEntry(string name, Template template, ReadOnlySpan<BoundType> parameters, BoundType result, bool resultSlot, BoundCall call, FunctionAbi? selected, AggregateLayoutPool layouts)
    {
        foreach (var previous in this.previousEntries)
        {
            if (ReferenceEquals(previous.Abi.Name, name) && ReferenceEquals(previous.Template, template) && ReferenceEquals(previous.Result, result) &&
                ReferenceEquals(previous.Selected, selected) && ReferenceEquals(previous.DeclaringType, call.DeclaringType) &&
                previous.Parameters.AsSpan().SequenceEqual(parameters) && previous.Arguments.AsSpan().SequenceEqual(call.TypeArguments) &&
                previous.Lengths.AsSpan().SequenceEqual(call.LengthArguments) && FunctionAbiPool.Matches(previous.Abi, result, parameters, resultSlot, layouts))
            {
                return previous;
            }
        }

        return null;
    }

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
