// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private List<BoundCall>? resolvedCallStorage;
    private Dictionary<int, BoundCall>? resolvedCalls;
    private int resolvedCallCount;

    // A case-dependent operation keeps its selected call in this body's context, never in the shared syntax.
    // The call's arrays are retained across analysis requests; Reset releases its previous source and Type references.
    internal BoundCall NextResolvedCall()
    {
        var storage = this.resolvedCallStorage ??= new();
        if (this.resolvedCallCount == storage.Count)
        {
            storage.Add(new());
        }

        return storage[this.resolvedCallCount++];
    }

    // The selected call already includes this operation's default and case/instance substitution.
    internal void RecordResolvedCall(int operation, BoundCall call)
        => (this.resolvedCalls ??= new())[operation] = call;

    private void ResetResolvedCalls()
    {
        this.resolvedCalls?.Clear();
        for (var i = 0; i < this.resolvedCallCount; i++)
        {
            this.resolvedCallStorage![i].Clear();
        }

        this.resolvedCallCount = 0;
    }
}
