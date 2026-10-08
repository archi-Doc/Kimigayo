// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private List<BoundCall>? resolvedCallStorage;
    private Dictionary<int, BoundCall>? resolvedCalls;
    private int resolvedCallCount;
    private Dictionary<int, BoundType>? callInputContracts;

    // The acquired Place keeps its actual Origins. Only a different instantiated parameter Type needs a handoff record.
    internal void RecordCallInput(int entry, BoundType? contract)
    {
        var actual = this.Places[this.Operations[entry].Place].Type;
        if (contract is { CarriesOrigin: true, Kind: not (BoundTypeKind.Function or BoundTypeKind.FunctionItem) } && !ReferenceEquals(contract, actual) &&
            this.Function.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(actual, contract, this.Operations[entry].Source))
        {
            (this.callInputContracts ??= new())[entry] = contract;
        }
    }

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

    private BoundType CallInputType(int entry)
        => this.callInputContracts?.TryGetValue(entry, out var contract) == true ? contract : this.Places[this.Operations[entry].Place].Type;

    private void ResetResolvedCalls()
    {
        this.callInputContracts?.Clear();
        this.resolvedCalls?.Clear();
        for (var i = 0; i < this.resolvedCallCount; i++)
        {
            this.resolvedCallStorage![i].Clear();
        }

        this.resolvedCallCount = 0;
    }
}
