// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class GenericStoragePlan
{
    private readonly Dictionary<BoundCall, string> destructorNames = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BoundCall> requestedSourceCalls = new(ReferenceEqualityComparer.Instance);
    private readonly List<(BoundCall Call, CallEntry? Parent)> sourceCallQueue = new();
    private int preparedSourceCalls;

    internal bool HasPendingSourceCalls => this.preparedSourceCalls < this.sourceCallQueue.Count;

    internal BoundCall RequireSourceCall(BoundCall call)
    {
        if (this.requestedSourceCalls.Add(call))
        {
            this.sourceCallQueue.Add((call, this.ExpansionParent));
        }

        return call;
    }

    // Layout discovery reserves the name only. Preparing the body here would recursively request the
    // same layout before its fields are complete; the emitter drains these requests between bodies.
    internal string? RequireDestructor(Binding binding, BoundType type)
    {
        if (binding.DestructionCall(type) is not { } call)
        {
            return null;
        }

        if (!this.destructorNames.TryGetValue(call, out var name))
        {
            name = this.EntryName(this.entryNames++);
            this.destructorNames.Add(call, name);
            this.RequireSourceCall(call);
        }

        return name;
    }

    internal bool PrepareSourceCalls(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, out string? failure)
    {
        failure = null;
        while (this.HasPendingSourceCalls)
        {
            var (call, parent) = this.sourceCallQueue[this.preparedSourceCalls++];
            var previous = this.ExpansionParent;
            this.ExpansionParent = parent;
            try
            {
                if (!this.PrepareSourceEntry(compilation, module, layouts, call, out var abi, out failure))
                {
                    return false;
                }

                module.SourceCalls.Add(call, abi!);
            }
            finally
            {
                this.ExpansionParent = previous;
            }
        }

        return true;
    }

    // Descriptor-selected implementations enter the same bounded dependency graph as direct calls and destructors.
    internal bool PrepareSourceEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, out FunctionAbi? abi, out string? failure, bool implementationBody = true)
    {
        abi = null;
        if (call.Target.Declaration is not Parsing.FunctionKoto function || !this.templates.TryGetValue(function, out var template))
        {
            return Fail("Compiler-requested source call requires a universally verified body.", out failure);
        }

        if (!this.PrepareEntry(compilation, module, layouts, call, template, out var entry, out failure, implementationBody: implementationBody))
        {
            return false;
        }

        abi = entry!.Selected ?? entry.Abi;
        return true;
    }
}
