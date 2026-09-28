// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class GenericStoragePlan
{
    private readonly Dictionary<BoundCall, string> destructorNames = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundCall> destructorQueue = new();
    private int preparedDestructors;

    internal bool HasPendingDestructors => this.preparedDestructors < this.destructorQueue.Count;

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
            this.destructorQueue.Add(call);
        }

        return name;
    }

    internal bool PrepareDestructors(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, out string? failure)
    {
        failure = null;
        while (this.HasPendingDestructors)
        {
            var call = this.destructorQueue[this.preparedDestructors++];
            if (call.Target.Declaration is not Parsing.FunctionKoto function || !this.templates.TryGetValue(function, out var template))
            {
                return Fail("Generic destructor requires a universally verified body.", out failure);
            }

            if (!this.PrepareEntry(compilation, module, layouts, call, template, out _, out failure))
            {
                return false;
            }
        }

        return true;
    }
}
