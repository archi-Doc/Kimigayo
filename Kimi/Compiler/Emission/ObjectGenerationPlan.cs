// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Checked call contexts and syntax-free physical object records.

internal sealed record ObjectCreation(int Id, int TypeKey, ValueLowering Payload, string? Destroy, bool Copy, FunctionAbi Abi);

internal sealed record ObjectCall(BoundType Payload, BoundType Result, ObjectCreation Physical);

internal sealed class ObjectGenerationPlan
{
    private readonly Dictionary<BoundCall, ObjectCall> calls = new(ReferenceEqualityComparer.Instance);

    internal IReadOnlyDictionary<BoundCall, ObjectCall> Calls => this.calls;

    internal void Clear() => this.calls.Clear();

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, out string? failure)
    {
        this.calls.Clear();
        failure = null;
        var hasObjects = false;
        for (var b = 0; b < compilation.Ownership.Bodies.Count && !hasObjects; b++)
        {
            var operations = compilation.Ownership.Bodies[b].Operations;
            for (var i = 0; i < operations.Count; i++)
            {
                if (operations[i].Kind == OwnershipOperationKind.Call && operations[i].Source is InvocationKoto { BoundCall: { } call } &&
                    ReferenceEquals(call.Target, compilation.Library.MakeObj))
                {
                    hasObjects = true;
                    break;
                }
            }
        }

        if (!hasObjects)
        {
            return true;
        }

        var definitions = new SortedDictionary<string, (BoundType Type, ValueLowering Value, string? Drop, bool Copy)>(StringComparer.Ordinal);
        var requests = new List<(BoundCall Call, BoundType Type, string Key)>();
        foreach (var body in compilation.Ownership.Bodies)
        {
            foreach (var operation in body.Operations)
            {
                if (operation.Kind != OwnershipOperationKind.Call || operation.Source is not InvocationKoto { BoundCall: { } call } ||
                    !ReferenceEquals(call.Target, compilation.Library.MakeObj))
                {
                    continue;
                }

                if (GenericStoragePlan.IsGeneric(body.Function) || call.TypeArguments.Length != 1 || call.TypeArguments[0] is not { } payload ||
                    !ObjectTypes.IsOwner(call.ReturnType) || !ReferenceEquals(call.ReturnType.Components[0], payload) ||
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
                var drop = ReferenceEquals(payload, BoundType.String) ? "__kimi_destroy_string" :
                    aggregate?.NeedsDestruction == true ? "__kimi_drop_aggregate" + aggregate.Id : null;
                var key = Key(payload, compilation.Project.Directory);
                definitions.TryAdd(key, (payload, value, drop, copy == ConstraintProof.Proven));
                requests.Add((call, payload, key));
            }
        }

        var entries = new Dictionary<string, ObjectCreation>(StringComparer.Ordinal);
        foreach (var pair in definitions)
        {
            var value = pair.Value.Value;
            var parameters = new List<AbiParameter> { new("ptr", "ret", AbiParameterKind.ResultSlot) };
            if (value.Layout.Size != 0)
            {
                parameters.Add(new(value.ArgumentType!, "a0", SlotTypes.IsResult(pair.Value.Type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value, 0));
            }

            parameters.Add(new("ptr", "location", AbiParameterKind.Location));
            parameters.Add(new("i64", "length", AbiParameterKind.LocationLength));
            var id = module.Objects.Count;
            var abi = new FunctionAbi("__kimi_make_object" + id, "void", parameters.ToArray(), resultSlot: true);
            var entry = new ObjectCreation(id, module.Constants.Intern(pair.Key, LlvmConstantKind.Text), value, pair.Value.Drop, pair.Value.Copy, abi);
            module.Objects.Add(entry);
            entries.Add(pair.Key, entry);
        }

        foreach (var request in requests)
        {
            this.calls.Add(request.Call, new(request.Type, request.Call.ReturnType, entries[request.Key]));
        }

        return true;
    }

    private static string Key(BoundType type, string directory)
    {
        var identity = type.Name;
        if (type.Symbol is { } symbol)
        {
            var names = new List<string>();
            for (var node = symbol.Declaration; node is not null; node = node.Parent)
            {
                if (node is DeclarationContainerKoto container)
                {
                    names.Add(container.Name);
                }
            }

            names.Reverse();
            var module = symbol.Declaration.CodeContext.Kotonoha;
            var package = ReferenceEquals(module, module.Compilation.Kotonoha)
                ? module.Compilation.Project.ProjectFile.PackageId + "@" + module.Compilation.Project.ProjectFile.PackageVersion : string.Empty;
            // Imported module names already contain the resolved dependency key.
            identity = package + ":" + module.Name + ":" + string.Join("/", names) + ":" + symbol.Name;
            if (type.Kind == BoundTypeKind.Closure)
            {
                var path = symbol.Declaration.CodeContext.SourceDocument?.Path ?? string.Empty;
                path = Path.IsPathRooted(path) ? Path.GetRelativePath(directory, path) : path;
                identity += ":" + path.Replace('\\', '/') + ":" + symbol.Declaration.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Origins carry static dependencies, never Runtime Type Identity.
        return type.Semantics + ":" + type.Kind + ":" + identity + ":" + type.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "<" + string.Join(",", type.Components.Select(x => Key(x, directory))) + ">";
    }
}
