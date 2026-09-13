// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Reusable ordinal/signature records containing no syntax or Symbol references.</summary>
internal sealed class FunctionAbiPool
{
    private readonly List<Signature> signatures = new();

    internal FunctionAbi Get(int ordinal, FunctionKoto function)
    {
        var result = function.BoundSymbol!.Type!;
        if (ordinal < this.signatures.Count && this.signatures[ordinal].Matches(function, result))
        {
            return this.signatures[ordinal].Abi;
        }

        var logical = new ParameterShape[function.Parameters.Count];
        var resultSlot = ReferenceEquals(result, BoundType.String);
        var count = resultSlot ? 1 : 0;
        for (var i = 0; i < logical.Length; i++)
        {
            logical[i] = Shape(function.Parameters[i].Type.BoundType!);
            count += logical[i].Type is null ? 0 : 1;
        }

        var parameters = new AbiParameter[count];
        var physical = 0;
        if (resultSlot)
        {
            parameters[physical++] = new("ptr", "ret", AbiParameterKind.ResultSlot);
        }

        for (var i = 0; i < logical.Length; i++)
        {
            if (logical[i].Type is not null)
            {
                parameters[physical++] = new(logical[i].Type!, "a" + i.ToString(CultureInfo.InvariantCulture), logical[i].Kind, i);
            }
        }

        var never = ReferenceEquals(result, BoundType.Never);
        var abi = new FunctionAbi("__kimi_f" + ordinal.ToString(CultureInfo.InvariantCulture), FunctionAbi.ResultType(result)!, parameters, never, resultSlot);
        var signature = new Signature(FunctionAbi.ResultType(result)!, resultSlot, never, logical, abi);
        if (ordinal == this.signatures.Count)
        {
            this.signatures.Add(signature);
        }
        else
        {
            this.signatures[ordinal] = signature;
        }

        return abi;
    }

    // Only physical facts survive reparse. Complete Types and Origins remain in the current call plans.
    private static ParameterShape Shape(BoundType type) => new(WindowsLowering.GetValue(type)!.ArgumentType, ReferenceTypes.IsString(type) ? AbiParameterKind.SharedReference : ReferenceEquals(type, BoundType.String) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value);

    private readonly record struct ParameterShape(string? Type, AbiParameterKind Kind);

    private sealed record Signature(string Result, bool ResultSlot, bool NoReturn, ParameterShape[] Parameters, FunctionAbi Abi)
    {
        internal bool Matches(FunctionKoto function, BoundType result)
        {
            if (this.Result != FunctionAbi.ResultType(result) || this.ResultSlot != ReferenceEquals(result, BoundType.String) ||
                this.NoReturn != ReferenceEquals(result, BoundType.Never) || this.Parameters.Length != function.Parameters.Count)
            {
                return false;
            }

            for (var i = 0; i < this.Parameters.Length; i++)
            {
                if (this.Parameters[i] != Shape(function.Parameters[i].Type.BoundType!))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
