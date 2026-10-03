// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Reusable ordinal/signature records containing no syntax or Symbol references.</summary>
internal sealed class FunctionAbiPool
{
    private readonly List<Signature> signatures = new();

    /// <summary>
    /// Builds the physical signature of a closed, non-closure entry from its substituted parameter and
    /// result Types with the same parameter kinds and slot rules as ordinary functions (SPEC 21.3.1:
    /// generic entries and concrete functions share one ABI rule).
    /// </summary>
    /// <param name="name">The unique physical name.</param>
    /// <param name="result">The closed result Type.</param>
    /// <param name="parameters">The closed parameter Types in declaration order.</param>
    /// <param name="resultSlot">Whether the result is returned through a caller-provided slot.</param>
    /// <param name="layouts">The layout pool for aggregate Types.</param>
    /// <param name="callerLocation">Whether this standard source entry forwards its caller's diagnostic location.</param>
    /// <returns>The physical signature; zero-sized parameters have no physical slot.</returns>
    internal static FunctionAbi Build(string name, BoundType result, ReadOnlySpan<BoundType> parameters, bool resultSlot, AggregateLayoutPool? layouts, bool callerLocation = false)
    {
        var physical = new List<AbiParameter>(parameters.Length + 1 + (callerLocation ? 2 : 0));
        if (resultSlot)
        {
            physical.Add(new("ptr", "ret", AbiParameterKind.ResultSlot));
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = Shape(parameters[i], layouts);
            if (shape.Type is not null)
            {
                physical.Add(new(shape.Type, "a" + i.ToString(CultureInfo.InvariantCulture), shape.Kind, i));
            }
        }

        if (callerLocation)
        {
            physical.Add(new("ptr", "location", AbiParameterKind.Location));
            physical.Add(new("i64", "location_length", AbiParameterKind.LocationLength));
        }

        return new FunctionAbi(name, FunctionAbi.ResultType(result, layouts)!, physical.ToArray(), ReferenceEquals(result, BoundType.Never), resultSlot, callerLocation);
    }

    /// <summary>Whether an ABI made by <see cref="Build"/> still describes the signature under the current layouts.</summary>
    /// <param name="abi">A previously built ABI.</param>
    /// <param name="result">The result Type.</param>
    /// <param name="parameters">The substituted parameter Types.</param>
    /// <param name="resultSlot">Whether the result is returned through a slot.</param>
    /// <param name="layouts">The current aggregate layouts.</param>
    /// <param name="callerLocation">Whether the source entry forwards the caller's location.</param>
    /// <returns>Whether <see cref="Build"/> would produce the same physical signature.</returns>
    internal static bool Matches(FunctionAbi abi, BoundType result, ReadOnlySpan<BoundType> parameters, bool resultSlot, AggregateLayoutPool? layouts, bool callerLocation = false)
    {
        if (abi.CallerLocation != callerLocation || abi.ResultSlot != resultSlot || abi.NoReturn != ReferenceEquals(result, BoundType.Never) || abi.Result != FunctionAbi.ResultType(result, layouts))
        {
            return false;
        }

        var physical = resultSlot ? 1 : 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = Shape(parameters[i], layouts);
            if (shape.Type is null)
            {
                continue;
            }

            if (physical >= abi.Parameters.Length || abi.Parameters[physical].Type != shape.Type || abi.Parameters[physical].Kind != shape.Kind || abi.Parameters[physical].LogicalIndex != i)
            {
                return false;
            }

            physical++;
        }

        return physical + (callerLocation ? 2 : 0) == abi.Parameters.Length;
    }

    internal FunctionAbi Get(int ordinal, FunctionKoto function, AggregateLayoutPool? layouts = null)
    {
        var result = function.BoundSymbol!.Type!;
        if (ordinal < this.signatures.Count && this.signatures[ordinal].Matches(function, result, layouts))
        {
            return this.signatures[ordinal].Abi;
        }

        var logical = new ParameterShape[function.Parameters.Count];
        var resultSlot = FunctionAbi.HasResultSlot(result, layouts) || function.IsConstructor || function.IsDestructor;
        var callerLocation = KimiLibraryCatalog.RequiresCallerLocation(function.BoundSymbol);
        var count = (resultSlot ? 1 : 0) + (function.IsAnonymous ? 2 : 0) + (callerLocation ? 2 : 0);
        for (var i = 0; i < logical.Length; i++)
        {
            logical[i] = Shape(function.Parameters[i].Type.BoundType!, layouts);
            count += logical[i].Type is null ? 0 : 1;
        }

        var parameters = new AbiParameter[count];
        var physical = 0;

        // A closure body is the entry of a common Function value: the environment word comes first and the result slot
        // second, as every common call and erasure adapter passes them (LlvmModuleWriter.WriteValueCall).
        if (function.IsAnonymous)
        {
            parameters[physical++] = new(function.BoundClosure?.EnvironmentType is null ? "i64" : "ptr", "environment", AbiParameterKind.Environment);
        }

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

        if (function.IsAnonymous)
        {
            parameters[physical++] = new("ptr", "context", AbiParameterKind.Context);
        }

        var never = ReferenceEquals(result, BoundType.Never);
        if (callerLocation)
        {
            parameters[physical++] = new("ptr", "location", AbiParameterKind.Location);
            parameters[physical++] = new("i64", "location_length", AbiParameterKind.LocationLength);
        }

        var abi = new FunctionAbi("__kimi_f" + ordinal.ToString(CultureInfo.InvariantCulture), FunctionAbi.ResultType(result, layouts)!, parameters, never, resultSlot, callerLocation);
        var signature = new Signature(FunctionAbi.ResultType(result, layouts)!, resultSlot, never, function.IsAnonymous, function.BoundClosure?.EnvironmentType is not null, logical, abi);
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

    private sealed record Signature(string Result, bool ResultSlot, bool NoReturn, bool Closure, bool Concrete, ParameterShape[] Parameters, FunctionAbi Abi)
    {
        internal bool Matches(FunctionKoto function, BoundType result, AggregateLayoutPool? layouts)
        {
            if (this.Closure != function.IsAnonymous || this.Concrete != (function.BoundClosure?.EnvironmentType is not null) || this.Result != FunctionAbi.ResultType(result, layouts) || this.ResultSlot != (FunctionAbi.HasResultSlot(result, layouts) || function.IsConstructor || function.IsDestructor) ||
                this.NoReturn != ReferenceEquals(result, BoundType.Never) || this.Parameters.Length != function.Parameters.Count ||
                this.Abi.CallerLocation != KimiLibraryCatalog.RequiresCallerLocation(function.BoundSymbol))
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
