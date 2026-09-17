// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Reusable ordinal/signature records containing no syntax or Symbol references.</summary>
internal sealed class FunctionAbiPool
{
    private readonly List<Signature> signatures = new();

    internal FunctionAbi Get(int ordinal, FunctionKoto function, AggregateLayoutPool? layouts = null)
    {
        var result = function.BoundSymbol!.Type!;
        if (ordinal < this.signatures.Count && this.signatures[ordinal].Matches(function, result, layouts))
        {
            return this.signatures[ordinal].Abi;
        }

        var logical = new ParameterShape[function.Parameters.Count];
        var resultSlot = FunctionAbi.HasResultSlot(result, layouts) || function.IsConstructor || function.IsDestructor;
        var count = (resultSlot ? 1 : 0) + (function.IsAnonymous ? 2 : 0);
        for (var i = 0; i < logical.Length; i++)
        {
            logical[i] = Shape(function.Parameters[i].Type.BoundType!, layouts);
            count += logical[i].Type is null ? 0 : 1;
        }

        var parameters = new AbiParameter[count];
        var physical = 0;
        if (resultSlot)
        {
            parameters[physical++] = new("ptr", "ret", AbiParameterKind.ResultSlot);
        }

        if (function.IsAnonymous)
        {
            parameters[physical++] = new("i64", "environment", AbiParameterKind.Environment);
        }

        for (var i = 0; i < logical.Length; i++)
        {
            if (logical[i].Type is not null)
            {
                parameters[physical++] = new(logical[i].Type!, "a" + i.ToString(CultureInfo.InvariantCulture), logical[i].Kind, i);
            }
        }

        if (function.IsAnonymous)
        {
            parameters[physical++] = new("ptr", "context", AbiParameterKind.Context);
        }

        var never = ReferenceEquals(result, BoundType.Never);
        var abi = new FunctionAbi("__kimi_f" + ordinal.ToString(CultureInfo.InvariantCulture), FunctionAbi.ResultType(result, layouts)!, parameters, never, resultSlot);
        var signature = new Signature(FunctionAbi.ResultType(result, layouts)!, resultSlot, never, function.IsAnonymous, logical, abi);
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
    private static ParameterShape Shape(BoundType type, AggregateLayoutPool? layouts)
    {
        var value = FunctionAbi.GetValue(type, layouts)!;
        var kind = ReferenceTypes.IsString(type) ? AbiParameterKind.SharedReference : SlotTypes.IsResult(type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value;
        return new(value.Layout.Size == 0 ? null : value.ArgumentType, kind);
    }

    private readonly record struct ParameterShape(string? Type, AbiParameterKind Kind);

    private sealed record Signature(string Result, bool ResultSlot, bool NoReturn, bool Closure, ParameterShape[] Parameters, FunctionAbi Abi)
    {
        internal bool Matches(FunctionKoto function, BoundType result, AggregateLayoutPool? layouts)
        {
            if (this.Closure != function.IsAnonymous || this.Result != FunctionAbi.ResultType(result, layouts) || this.ResultSlot != (FunctionAbi.HasResultSlot(result, layouts) || function.IsConstructor || function.IsDestructor) ||
                this.NoReturn != ReferenceEquals(result, BoundType.Never) || this.Parameters.Length != function.Parameters.Count)
            {
                return false;
            }

            for (var i = 0; i < this.Parameters.Length; i++)
            {
                if (this.Parameters[i] != Shape(function.Parameters[i].Type.BoundType!, layouts))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
