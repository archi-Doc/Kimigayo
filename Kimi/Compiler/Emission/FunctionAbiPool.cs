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

        var logical = new BoundType[function.Parameters.Count];
        var count = 0;
        for (var i = 0; i < logical.Length; i++)
        {
            logical[i] = function.Parameters[i].Type.BoundType!;
            count += ReferenceEquals(logical[i], BoundType.Unit) ? 0 : 1;
        }

        var parameters = new AbiParameter[count];
        var physical = 0;
        for (var i = 0; i < logical.Length; i++)
        {
            if (!ReferenceEquals(logical[i], BoundType.Unit))
            {
                parameters[physical++] = new(WindowsLowering.GetValue(logical[i])!.ComputationType, "a" + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        var never = ReferenceEquals(result, BoundType.Never);
        var abi = new FunctionAbi("__kimi_f" + ordinal.ToString(CultureInfo.InvariantCulture), never ? "void" : WindowsLowering.GetValue(result)!.ComputationType, parameters, never);
        var signature = new Signature(result, logical, abi);
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

    private sealed record Signature(BoundType Result, BoundType[] Parameters, FunctionAbi Abi)
    {
        internal bool Matches(FunctionKoto function, BoundType result)
        {
            if (!ReferenceEquals(this.Result, result) || this.Parameters.Length != function.Parameters.Count)
            {
                return false;
            }

            for (var i = 0; i < this.Parameters.Length; i++)
            {
                if (!ReferenceEquals(this.Parameters[i], function.Parameters[i].Type.BoundType))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
