// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Checked call contexts and syntax-free physical object records.

internal sealed record ObjectCreation(int Id, int TypeKey, ValueLowering Payload, string? Destroy, bool Copy, FunctionAbi Abi, int TypeToken, int[] BaseTokens);

internal readonly record struct ObjectCall(BoundType Payload, BoundType Result, ObjectCreation Physical);

internal sealed class ObjectGenerationPlan
{
    private static readonly Dictionary<SemanticsKind, string> SemanticsNames = Enum.GetValues<SemanticsKind>().ToDictionary(static x => x, static x => x.ToString());
    private static readonly Dictionary<BoundTypeKind, string> KindNames = Enum.GetValues<BoundTypeKind>().ToDictionary(static x => x, static x => x.ToString());
    private readonly Dictionary<BoundCall, ObjectCall> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundType, int> runtimeTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (BoundType Type, ValueLowering Value, int Drop, bool Copy)> definitions = new(StringComparer.Ordinal);
    private readonly List<(BoundCall Call, BoundType Type, string Key)> requests = new();
    private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<BoundType, string> typeKeys = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ObjectCreation> entries = new(StringComparer.Ordinal);
    private readonly List<string> sortedKeys = new();
    private readonly List<int> bases = new();
    private readonly StringBuilder keyBuffer = new();
    private readonly List<string> keyTexts = new();
    private readonly List<(int Drop, ObjectCreation Entry)> physical = new();

    internal IReadOnlyDictionary<BoundCall, ObjectCall> Calls => this.calls;

    internal IReadOnlyDictionary<BoundType, int> RuntimeTypes => this.runtimeTypes;

    internal void Clear()
    {
        this.calls.Clear();
        this.runtimeTypes.Clear();
        this.definitions.Clear();
        this.requests.Clear();
        this.identities.Clear();
        this.typeKeys.Clear();
        this.entries.Clear();
        this.sortedKeys.Clear();
        this.bases.Clear();
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, out string? failure)
    {
        this.Clear();
        failure = null;
        var hasObjects = false;
        for (var b = 0; b < compilation.Ownership.Bodies.Count && !hasObjects; b++)
        {
            var operations = compilation.Ownership.Bodies[b].Operations;
            for (var i = 0; i < operations.Count; i++)
            {
                if ((operations[i].Kind == OwnershipOperationKind.Call && operations[i].Source is InvocationKoto { BoundCall: { } call } &&
                    ReferenceEquals(call.Target, compilation.Library.MakeObj)) || operations[i].Source is IsKoto { BoundRuntimeTest: not null })
                {
                    hasObjects = true;
                    break;
                }
            }
        }

        if (!hasObjects)
        {
            this.physical.Clear();
            this.keyTexts.Clear();
            return true;
        }

        var definitions = this.definitions;
        var requests = this.requests;
        var identities = this.identities;
        var typeKeys = this.typeKeys;
        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            for (var i = 0; i < body.Operations.Count; i++)
            {
                var operation = body.Operations[i];
                if (operation.Source is IsKoto { BoundRuntimeTest: { } test })
                {
                    RegisterType(test.TargetType);
                }

                if (operation.Kind != OwnershipOperationKind.Call || operation.Source is not InvocationKoto { BoundCall: { } call } ||
                    !ReferenceEquals(call.Target, compilation.Library.MakeObj))
                {
                    continue;
                }

                if (GenericStoragePlan.IsGeneric(body.Function) || call.TypeArguments.Length != 1 || call.TypeArguments[0] is not { } payload ||
                    ObjectTypes.HandleMode(call.ReturnType) is not { Counting: ObjectCountingStep.None } || !ReferenceEquals(call.ReturnType.Components[0], payload) ||
                    call.ArgumentOperations.Length != 1 || call.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead) ||
                    !ReferenceEquals(call.ArgumentOperations[0].ParameterType, payload) ||
                    FunctionAbi.GetValue(payload, layouts) is not { } value || value.Layout.Alignment > 16 || value.Layout.Stride != value.Layout.Size)
                {
                    failure = "Object creation requires a checked concrete payload acquisition and supported layout.";
                    return false;
                }

                var copy = compilation.Binding.ProveCopy(payload, operation.Source);
                if (copy is not (ConstraintProof.Proven or ConstraintProof.Refuted))
                {
                    failure = "Object payload acquisition lacks a concrete Copy/Move proof.";
                    return false;
                }

                var aggregate = layouts.Get(payload);
                var drop = ReferenceEquals(payload, BoundType.String) ? -2 : aggregate?.NeedsDestruction == true ? aggregate.Id : -1;
                RegisterType(payload);
                var key = typeKeys[payload];
                definitions.TryAdd(key, (payload, value, drop, copy == ConstraintProof.Proven));
                requests.Add((call, payload, key));
            }
        }

        var keys = this.sortedKeys;
        keys.AddRange(identities.Keys);
        keys.Sort(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            identities[keys[i]] = i + 1;
        }

        foreach (var pair in typeKeys)
        {
            this.runtimeTypes.Add(pair.Key, identities[pair.Value]);
        }

        module.NeedsObjectRuntime = true;
        var entries = this.entries;
        keys.Clear();
        keys.AddRange(definitions.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var definition = definitions[key];
            var value = definition.Value;
            var id = module.Objects.Count;
            var bases = this.bases;
            bases.Clear();
            for (var parent = Base(definition.Type); parent is not null; parent = Base(parent))
            {
                bases.Add(this.runtimeTypes[parent]);
            }

            var typeKey = module.Constants.Intern(key, LlvmConstantKind.Text);
            var token = identities[key];
            var parameterKind = SlotTypes.IsResult(definition.Type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value;
            var entry = id < this.physical.Count ? this.physical[id].Entry : null;
            // Recompute every semantic decision; retain only matching syntax-free physical records.
            if (entry is null || entry.TypeKey != typeKey || entry.TypeToken != token || entry.Payload != value || entry.Copy != definition.Copy ||
                !entry.BaseTokens.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(bases)) ||
                this.physical[id].Drop != definition.Drop ||
                (value.Layout.Size != 0 && entry.Abi.Parameters[1].Kind != parameterKind))
            {
                var parameters = new List<AbiParameter> { new("ptr", "ret", AbiParameterKind.ResultSlot) };
                if (value.Layout.Size != 0)
                {
                    parameters.Add(new(value.ArgumentType!, "a0", parameterKind, 0));
                }

                parameters.Add(new("ptr", "location", AbiParameterKind.Location));
                parameters.Add(new("i64", "length", AbiParameterKind.LocationLength));
                var abi = new FunctionAbi("__kimi_make_object" + id, "void", parameters.ToArray(), resultSlot: true);
                var drop = definition.Drop == -1 ? null : definition.Drop == -2 ? "__kimi_destroy_string" : "__kimi_drop_aggregate" + definition.Drop;
                entry = new(id, typeKey, value, drop, definition.Copy, abi, token, bases.ToArray());
                if (id == this.physical.Count)
                {
                    this.physical.Add((definition.Drop, entry));
                }
                else
                {
                    this.physical[id] = (definition.Drop, entry);
                }
            }

            module.Objects.Add(entry);
            entries.Add(key, entry);
        }

        foreach (var request in requests)
        {
            this.calls.Add(request.Call, new(request.Type, request.Call.ReturnType, entries[request.Key]));
        }

        this.physical.RemoveRange(module.Objects.Count, this.physical.Count - module.Objects.Count);
        this.keyTexts.RemoveRange(typeKeys.Count, this.keyTexts.Count - typeKeys.Count);
        return true;

        BoundType? Base(BoundType type) => type.Symbol?.Declaration is StructKoto { Bases.Count: 1 } structure ? compilation.Binding.StoredType(structure.Bases[0], type) : null;

        void RegisterType(BoundType type)
        {
            if (typeKeys.ContainsKey(type))
            {
                return;
            }

            this.keyBuffer.Clear();
            WriteKey(this.keyBuffer, type, compilation.Project.Directory);
            var ordinal = typeKeys.Count;
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

            typeKeys.Add(type, key);
            identities.TryAdd(key, 0);
            if (Base(type) is { } parent)
            {
                RegisterType(parent);
            }
        }
    }

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
}
