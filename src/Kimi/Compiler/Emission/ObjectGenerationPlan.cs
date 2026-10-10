// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Checked call contexts and syntax-free physical object records.

internal sealed record ObjectCreation(int Id, int TypeKey, ValueLowering Payload, string? Destroy, bool Copy, FunctionAbi Abi, int TypeToken, int[] BaseTokens, FunctionAbi?[] VirtualSlots);

internal readonly record struct ObjectCall(BoundType Payload, BoundType Result, ObjectCreation Physical);

internal sealed class ObjectGenerationPlan
{
    private static readonly Dictionary<SemanticsKind, string> SemanticsNames = Enum.GetValues<SemanticsKind>().ToDictionary(static x => x, static x => x.ToString());
    private static readonly Dictionary<BoundTypeKind, string> KindNames = Enum.GetValues<BoundTypeKind>().ToDictionary(static x => x, static x => x.ToString());
    private readonly Dictionary<CallPlan, ObjectCall> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundType, int> runtimeTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<BoundType, string> typeKeys = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ObjectCreation> entries = new(StringComparer.Ordinal);
    private readonly List<int> bases = new();
    private readonly StringBuilder keyBuffer = new();
    private readonly List<string> keyTexts = new();
    private readonly List<(int Drop, ObjectCreation Entry)> physical = new();
    private int preparedEntries;

    internal IReadOnlyDictionary<CallPlan, ObjectCall> Calls => this.calls;

    internal IReadOnlyDictionary<BoundType, int> RuntimeTypes => this.runtimeTypes;

    internal VirtualGenerationPlan? Virtuals { get; set; }

    internal bool PrepareDefault(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, GenericStoragePlan.CallEntry entry, out string? failure)
    {
        if (!this.PrepareBodyCalls(compilation, module, layouts, entry.Template.Body, out failure))
        {
            return false;
        }

        for (var i = 0; entry.ConcreteCalls is { } calls && i < calls.Length; i++)
        {
            if (!this.AddCall(compilation, module, layouts, calls[i], out failure))
            {
                return false;
            }
        }

        return true;
    }

    internal void Clear()
    {
        this.calls.Clear();
        this.runtimeTypes.Clear();
        this.identities.Clear();
        this.typeKeys.Clear();
        this.entries.Clear();
        this.bases.Clear();
        this.preparedEntries = 0;
    }

    internal void Complete()
    {
        this.physical.RemoveRange(this.entries.Count, this.physical.Count - this.entries.Count);
        this.keyTexts.RemoveRange(this.typeKeys.Count, this.keyTexts.Count - this.typeKeys.Count);
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, GenericStoragePlan generics, out string? failure)
    {
        this.Clear();
        failure = null;
        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            if (GenericStoragePlan.IsGeneric(body.Function) || compilation.Binding.IsInapplicableVirtualBody(body.Function))
            {
                continue;
            }

            if (!this.PrepareBodyCalls(compilation, module, layouts, body, out failure))
            {
                return false;
            }
        }

        return this.PrepareInstances(compilation, module, layouts, generics, out failure);
    }

    // A generic adaptation may create only in some cases and select a different factory in each. Read the verified
    // concrete body's chosen calls, not the first definition case. Ordinary forwarded calls keep their existing map.
    internal bool PrepareBodyCalls(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, OwnershipBody body, out string? failure)
    {
        failure = null;
        var declaredCalls = !GenericStoragePlan.IsGeneric(body.Function);
        for (var i = 0; i < body.Operations.Count; i++)
        {
            var operation = body.Operations[i];
            if (operation.Source is IsKoto { BoundRuntimeTest: { } test })
            {
                this.RegisterType(compilation, body.Resolve(test.TargetType, body.ContextAt(i))!);
                module.NeedsObjectRuntime = true;
            }

            if (operation.Kind != OwnershipOperationKind.Call || body.CallAt(i) is not { } call ||
                call.Target.CompilerFunction is not (CompilerFunctionKind.MakeObj or CompilerFunctionKind.MakeRc or CompilerFunctionKind.MakeArc or CompilerFunctionKind.Clone))
            {
                continue;
            }

            module.NeedsObjectRuntime = true;
            if ((declaredCalls || call.AdaptationSource is not null) && !this.AddCall(compilation, module, layouts, call, out failure))
            {
                return false;
            }
        }

        return true;
    }

    // Append each concrete entry once, including entries discovered by destruction. Existing factory and Type IDs never
    // change after a body has used them. The generic planner's bounded destructor queue closes this dependency graph.
    internal bool PrepareInstances(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, GenericStoragePlan generics, out string? failure)
    {
        failure = null;
        while (this.preparedEntries < generics.Entries.Count)
        {
            var entry = generics.Entries[this.preparedEntries++];
            if (entry.Selected is not null)
            {
                continue;
            }

            var parent = generics.ExpansionParent;
            generics.ExpansionParent = entry;
            try
            {
                for (var i = 0; entry.ConcreteCalls is { } concrete && i < concrete.Length; i++)
                {
                    if (!this.AddCall(compilation, module, layouts, concrete[i], out failure))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                generics.ExpansionParent = parent;
            }
        }

        return true;
    }

    internal bool AddCall(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, CallPlan call, out string? failure)
    {
        failure = null;
        if (call.Target.CompilerFunction is not (CompilerFunctionKind.MakeObj or CompilerFunctionKind.MakeRc or CompilerFunctionKind.MakeArc) || this.calls.ContainsKey(call))
        {
            return true;
        }

        if (call.TypeArguments.Length != 1 || call.TypeArguments[0] is not { } payload ||
            call.ReturnType.HandleMode is null || !ReferenceEquals(call.ReturnType.Components[0], payload) ||
            call.ReturnType.Semantics != call.Target.CompilerFunction switch { CompilerFunctionKind.MakeObj => SemanticsKind.Obj, CompilerFunctionKind.MakeRc => SemanticsKind.Rc, _ => SemanticsKind.Arc } ||
            call.ArgumentOperations.Length != 1 || call.ArgumentOperations[0] is not { Kind: ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead, Source: { } source } argument ||
            !ReferenceEquals(argument.ParameterType, payload) ||
            FunctionAbi.GetValue(payload, layouts) is not { } value || value.Layout.Alignment > 16 || value.Layout.Stride != value.Layout.Size)
        {
            failure = "Object creation requires a checked concrete payload acquisition and supported layout.";
            return false;
        }

        var copy = compilation.Binding.ProveCopy(payload, source);
        if (copy is not (ConstraintProof.Proven or ConstraintProof.Refuted))
        {
            failure = "Object payload acquisition lacks a concrete Copy/Move proof.";
            return false;
        }

        this.RegisterType(compilation, payload);
        var slots = this.Virtuals?.GetTable(payload) ?? (this.Virtuals is null ? [] : null);
        if (slots is null)
        {
            failure = "An object descriptor requires a verified virtual table with matching entry ABIs.";
            return false;
        }

        var key = this.typeKeys[payload];
        if (!this.entries.TryGetValue(key, out var entry))
        {
            var aggregate = layouts.Get(payload);
            var drop = ReferenceEquals(payload, BoundType.String) ? -2 : aggregate?.NeedsDestruction == true ? aggregate.Id : -1;
            var id = module.Objects.Count;
            this.bases.Clear();
            for (var parent = Base(compilation, payload); parent is not null; parent = Base(compilation, parent))
            {
                this.bases.Add(this.runtimeTypes[parent]);
            }

            var typeKey = module.Constants.Intern(key, LlvmConstantKind.Text);
            var token = this.runtimeTypes[payload];
            var parameterKind = SlotTypes.IsResult(payload) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value;
            entry = id < this.physical.Count ? this.physical[id].Entry : null;
            // Recompute every semantic decision; retain only matching syntax-free physical records.
            if (entry is null || entry.TypeKey != typeKey || entry.TypeToken != token || entry.Payload != value || entry.Copy != (copy == ConstraintProof.Proven) ||
                !entry.BaseTokens.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(this.bases)) || !entry.VirtualSlots.AsSpan().SequenceEqual(slots) || this.physical[id].Drop != drop ||
                (value.Layout.Size != 0 && entry.Abi.Parameters[1].Kind != parameterKind))
            {
                var parameters = new List<AbiParameter> { new("ptr", "ret", AbiParameterKind.ResultSlot) };
                if (value.Layout.Size != 0)
                {
                    parameters.Add(new(value.ArgumentType!, "a0", parameterKind, 0));
                }

                parameters.Add(new("i64", "controlValue", AbiParameterKind.Context));
                parameters.Add(new("ptr", "location", AbiParameterKind.Location));
                parameters.Add(new("i64", "length", AbiParameterKind.LocationLength));
                var abi = new FunctionAbi("__kimi_make_object" + id, "void", parameters.ToArray(), resultSlot: true);
                var destroy = drop == -1 ? null : drop == -2 ? "__kimi_destroy_string" : "__kimi_drop_aggregate" + drop;
                entry = new(id, typeKey, value, destroy, copy == ConstraintProof.Proven, abi, token, this.bases.ToArray(), slots);
                if (id == this.physical.Count)
                {
                    this.physical.Add((drop, entry));
                }
                else
                {
                    this.physical[id] = (drop, entry);
                }
            }

            module.Objects.Add(entry);
            this.entries.Add(key, entry);
        }

        module.NeedsObjectRuntime = true;
        this.calls.Add(call, new(payload, call.ReturnType, entry));
        return true;
    }

    private static BoundType? Base(Compilation compilation, BoundType type)
        => type.Symbol?.Declaration is StructKoto { Bases.Count: 1 } structure ? compilation.Binding.StoredType(structure.Bases[0], type) : null;

    private static void WriteKey(StringBuilder output, BoundType type, string directory)
    {
        output.Append(SemanticsNames[type.Semantics]).Append(':').Append(KindNames[type.Kind]).Append(':');
        if (type.Symbol is { } symbol)
        {
            var module = symbol.Declaration.CodeContext.Kotonoha;
            if (ReferenceEquals(module, module.Compilation.Kotonoha))
            {
                output.Append(module.Compilation.Project.ProjectFile.PackageId).Append('@').Append(module.Compilation.Project.ProjectFile.PackageVersion);
            }

            // Imported module names already contain the resolved dependency key.
            output.Append(':').Append(module.Name).Append(':');
            WriteContainers(output, symbol.Declaration);
            output.Append(':').Append(symbol.Name);
            if (type.Kind == BoundTypeKind.Closure)
            {
                var path = symbol.Declaration.CodeContext.SourceDocument?.Path ?? string.Empty;
                path = Path.IsPathRooted(path) ? Path.GetRelativePath(directory, path) : path;
                output.Append(':').Append(path.Replace('\\', '/')).Append(':').Append(symbol.Declaration.Span.Start);
            }
        }
        else
        {
            output.Append(type.Name);
        }

        // Origins carry static dependencies, never Runtime Type Identity.
        output.Append(':').Append(type.Length).Append('<');
        for (var i = 0; i < type.Components.Count; i++)
        {
            if (i != 0)
            {
                output.Append(',');
            }

            WriteKey(output, type.Components[i], directory);
        }

        output.Append('>');
    }

    private static bool WriteContainers(StringBuilder output, Koto? node)
    {
        if (node is null)
        {
            return false;
        }

        var written = WriteContainers(output, node.Parent);
        if (node is DeclarationContainerKoto container)
        {
            if (written)
            {
                output.Append('/');
            }

            output.Append(container.Name);
            return true;
        }

        return written;
    }

    private void RegisterType(Compilation compilation, BoundType type)
    {
        if (this.typeKeys.ContainsKey(type))
        {
            return;
        }

        this.keyBuffer.Clear();
        WriteKey(this.keyBuffer, type, compilation.Project.Directory);
        var ordinal = this.typeKeys.Count;
        var key = ordinal < this.keyTexts.Count ? this.keyTexts[ordinal] : null;
        if (key is null || !this.keyBuffer.Equals(key.AsSpan()))
        {
            key = this.keyBuffer.ToString();
            if (ordinal == this.keyTexts.Count)
            {
                this.keyTexts.Add(key);
            }
            else
            {
                this.keyTexts[ordinal] = key;
            }
        }

        this.typeKeys.Add(type, key);
        if (!this.identities.TryGetValue(key, out var token))
        {
            this.identities.Add(key, token = this.identities.Count + 1);
        }

        this.runtimeTypes.Add(type, token);
        if (Base(compilation, type) is { } parent)
        {
            this.RegisterType(compilation, parent);
        }
    }
}
