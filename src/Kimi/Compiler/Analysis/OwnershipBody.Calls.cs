// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler;

internal enum CallResultSource : byte
{
    None,
    Input,
    InputOrigins,
    ReceiverOrigins,
    Environment,
    PreparedOrigins,
}

internal readonly record struct OwnershipCallInput(int Entry, int Parameter, BoundType Type, bool IsDefault);

internal readonly record struct OwnershipCall(int Start, int Count, BoundCall? Target, CallResultSource ResultSource, int ResultInput = -1, bool RetainsEnvironment = false);

public sealed partial class OwnershipBody
{
    private readonly List<OwnershipCall> calls = new();
    private readonly List<OwnershipCallInput> callInputs = new();
    private List<BoundCall>? resolvedCallStorage;
    private int resolvedCallCount;

    internal int CallInputCount => this.callInputs.Count;

    internal ReadOnlySpan<OwnershipCallInput> CallInputStorage => CollectionsMarshal.AsSpan(this.callInputs);

    internal OwnershipCall CallInfo(int operation) => this.calls[this.OperationSteps[operation]];

    internal ReadOnlySpan<OwnershipCallInput> CallInputs(int operation)
    {
        var call = this.CallInfo(operation);
        return this.CallInputStorage.Slice(call.Start, call.Count);
    }

    internal BoundCall? CallAt(int operation) => this.CallInfo(operation).Target;

    internal void RecordCall(int operation, OwnershipCall call)
    {
        this.OperationSteps[operation] = this.calls.Count;
        this.calls.Add(call);
    }

    // The acquired Place keeps its actual Origins; the input records the verified parameter contract separately.
    internal void RecordCallInput(int entry, int parameter, BoundType? contract, bool isDefault = false)
    {
        var actual = this.Places[this.Operations[entry].Place].Type;
        if (contract is { CarriesOrigin: true, Kind: not (BoundTypeKind.Function or BoundTypeKind.FunctionItem) } && !ReferenceEquals(contract, actual) &&
            this.Function.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(actual, contract, this.Operations[entry].Source))
        {
            actual = contract;
        }

        this.OperationSteps[entry] = this.callInputs.Count;
        this.callInputs.Add(new(entry, parameter, actual, isDefault));
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

    internal int ResultArgument(int call)
    {
        var plan = this.CallInfo(call);
        if (plan.ResultSource == CallResultSource.Input)
        {
            return plan.ResultInput;
        }

        if (plan.ResultSource == CallResultSource.None || this.Operations[call].Place < 0)
        {
            return -1;
        }

        var result = this.Operations[call].Place;
        if (plan.ResultSource == CallResultSource.PreparedOrigins)
        {
            var sole = -1;
            foreach (var input in this.CallInputs(call))
            {
                if (this.NamesResultOrigin(this.Places[result].Type, input.Type))
                {
                    if (sole >= 0)
                    {
                        return -1;
                    }

                    sole = input.Entry;
                }
            }

            return sole;
        }

        if (this.SoleResultInput(call, this.Places[result].Type) is >= 0 and var argument)
        {
            return argument;
        }

        // SPEC 15.6.3, 15.8.2: a result bound to a callable environment descends from its recorded read;
        // a result naming only the receiver Type's Origins descends from its entry if no other input names them.
        return plan.ResultInput >= 0 && (plan.ResultSource == CallResultSource.Environment
            ? this.borrowDependencies[(result * this.Places.Count) + this.Operations[plan.ResultInput].Place] != LoanRequirement.None
            : plan.ResultSource == CallResultSource.ReceiverOrigins && !this.OtherInputNamesResult(call, plan.ResultInput)) ? plan.ResultInput : -1;
    }

    private BoundType CallInputType(int entry) => this.callInputs[this.OperationSteps[entry]].Type;

    private void ResetResolvedCalls()
    {
        this.callInputs.Clear();
        this.calls.Clear();
        for (var i = 0; i < this.resolvedCallCount; i++)
        {
            this.resolvedCallStorage![i].Clear();
        }

        this.resolvedCallCount = 0;
    }

    // SPEC 15.6.3: a result whose instantiated Origins are all named by one input's Type, such as the
    // `Option<(K, V)>` that `Dictionary.remove` returns from its receiver, descends from that input, because
    // values carrying those Origins reach the result only from it. When another input also names one of them,
    // as `insertOrReplace`'s `value: V` does, the result may come from either and descends from neither.
    private int SoleResultInput(int call, BoundType result)
    {
        if (this.Operations[call].Input is >= 0 and var receiver && this.NamesResultOrigin(result, this.Places[receiver].Type, includeBounds: true))
        {
            return -1; // The stored environment is another possible source, independent of this invocation's arguments.
        }

        var sole = -1;
        foreach (var input in this.CallInputs(call))
        {
            var type = input.Type;
            if (this.ResultOriginsFromInput(result, type, out var retained) && retained)
            {
                if (sole >= 0)
                {
                    return -1;
                }

                sole = input.Entry;
            }
            else if (this.NamesResultOrigin(result, type, includeBounds: true))
            {
                return -1;
            }
        }

        return sole;
    }

    // Whether an input of the call other than its receiver entry names an Origin of the call's result.
    private bool OtherInputNamesResult(int call, int receiver)
    {
        if (this.Operations[call].Place < 0)
        {
            return false;
        }

        var result = this.Places[this.Operations[call].Place].Type;
        foreach (var input in this.CallInputs(call))
        {
            if (input.Entry != receiver && this.NamesResultOrigin(result, input.Type, includeBounds: true))
            {
                return true;
            }
        }

        return false;
    }

    private bool ResultOriginsFromInput(BoundType result, BoundType input, out bool retained)
    {
        retained = false;
        return Visit(result, ref retained);

        bool Visit(BoundType type, ref bool found)
        {
            if (type.Origin is { Kind: not OriginKind.Static } origin)
            {
                found = true;
                if (this.NamedOriginRequirement(input, origin) == LoanRequirement.None)
                {
                    return false;
                }
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (type.OriginArguments[i].Kind != OriginKind.Static)
                {
                    found = true;
                    if (this.NamedOriginRequirement(input, type.OriginArguments[i]) == LoanRequirement.None)
                    {
                        return false;
                    }
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!Visit(type.Components[i], ref found))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
