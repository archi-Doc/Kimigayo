// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>Per-substitution plans of universally verified generic bodies (SPEC 21.3.1 step 3).</summary>
public sealed partial class OwnershipAnalysis
{
    private readonly List<OwnershipBody> instancePool = new();
    private int instanceCount;
    private OwnershipBody? instanceBody;
    private BoundCall? instance;
    private bool instanceFailed;

    /// <summary>Releases the instance plans of the previous generation request.</summary>
    internal void ClearInstances() => this.instanceCount = 0;

    /// <summary>
    /// Rebuilds the ownership plan of a verified generic body under one closed call substitution. The
    /// universal verification remains the acceptance proof; the instance plan only fixes acquisitions,
    /// value flow and cleanup for concrete lowering, and a refused instance returns null.
    /// </summary>
    /// <param name="generic">The universally verified generic body.</param>
    /// <param name="call">A closed call whose substitution selects the instance.</param>
    /// <returns>The verified instance plan, valid until <see cref="ClearInstances"/>.</returns>
    internal OwnershipBody? AnalyzeInstance(OwnershipBody generic, BoundCall call)
    {
        if (!generic.IsVerified || this.flow is null || this.instance is not null)
        {
            return null;
        }

        if (this.instanceCount == this.instancePool.Count)
        {
            this.instancePool.Add(new());
        }

        var saved = this.body;
        var issueCount = this.issues.Count;
        var target = this.instancePool[this.instanceCount];
        this.instanceBody = target;
        this.instance = call;
        this.instanceFailed = false;
        try
        {
            this.Build(generic.Function);
            if (this.instanceFailed || target.IssueStorage.Count != 0 || !target.IsVerified)
            {
                target.IsVerified = false;
                return null;
            }

            // Validations that run later, during lowering, see the instance's substitution through the body itself.
            target.Instance = call;
            target.InstanceBinding = this.compilation.Binding;
            this.instanceCount++;
            return target;
        }
        finally
        {
            this.issues.RemoveRange(issueCount, this.issues.Count - issueCount);
            this.body = saved;
            this.instanceBody = null;
            this.instance = null;
        }
    }

    // Declared Types of the analyzed body; an instance sees its closed substitution.
    private BoundType? Concrete(BoundType? type)
    {
        if (type is null || this.instance is not { } call)
        {
            return type;
        }

        if (this.compilation.Binding.InstantiateStorageType(type, call) is { } concrete)
        {
            return concrete;
        }

        this.instanceFailed = true;
        return type;
    }
}
