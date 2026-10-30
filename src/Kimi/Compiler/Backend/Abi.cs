// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Abi (IMPL 21.3.1, SPEC 22.6; plan P8; DECISIONS R5-M58): the physical signature of a closed function instance for every generator (PD2:
// the old pool, the old generic and compiler entries, value calls and imports call it, never a copy).
// Responsibility: the parameter order [environment] [ret] a0..an [context] [location, location_length]; under the internal convention
// each parameter's kind (a string reference SharedReference, a slot Type OwnedSlot, else Value), the omission of zero-sized parameters,
// the result slot (a slot Type of nonzero size, or a constructor's or destructor's) and the result Type (void for a slot Type, Unit or
// Never); under #LibraryImport's C convention (SPEC 22.6) each parameter is a Value of its argument Type, nothing is omitted and the
// result is never a slot.
// Inputs: the compilation's Layout; the convention and flags; the closed result Type and the closed parameter Types in slot order, each
// laid out by Layout (Ok); a C signature is one Binding validated (SPEC 22.6's table), whose every parameter has an argument
// representation, and takes no flags. The kinds read the root Type predicates SlotTypes and ReferenceTypes.
// Outputs: Of, a FunctionAbi without a name (empty), which its definition names (FunctionAbi.Named: the old pool __kimi_f<n>, the old
// imports their external symbol, ModuleCodegen its items); Matches, whether an existing FunctionAbi is that signature, so a caller
// reuses it without allocating; HasResultSlot and ResultType, the internal convention's result rules over a record, which the old
// pool's gate also answers with.
// Invariants: Of is a function of its inputs; Matches(Of(x), x) holds and allocates nothing.
// Must not gate (the old pool keeps FunctionAbiPool.Supports until R6-gen) or map a Layout outcome other than Ok, flags under C or a C
// parameter without an argument representation (broken contracts, which throw),
// read syntax, Origins, HIR, MIR or the old Emission's scalar-value gate (WindowsLowering), name a function, keep state or use LINQ.

using System.Globalization;

namespace Kimi.Compiler.Backend;

/// <summary>The physical signature of a closed function instance (IMPL 21.3.1, SPEC 22.6).</summary>
internal static class Abi
{
    /// <summary>A function's calling convention.</summary>
    internal enum Convention : byte
    {
        /// <summary>The profile's own convention for Kimigayo functions (IMPL 21.3.1).</summary>
        Internal,

        /// <summary>The Windows x64 C convention of a <c>#LibraryImport</c> declaration (SPEC 22.6).</summary>
        C,
    }

    /// <summary>The facts of a signature that its Types do not give.</summary>
    [System.Flags]
    internal enum Flags : byte
    {
        /// <summary>None of the facts.</summary>
        None = 0,

        /// <summary>A closure body whose environment is erased: an i64 environment word first, the call context last.</summary>
        ErasedClosure = 1,

        /// <summary>A closure body with a concrete environment: an environment pointer first, the call context last.</summary>
        ConcreteClosure = 2,

        /// <summary>A constructor's or destructor's result slot, whatever its result Type.</summary>
        ResultSlot = 4,

        /// <summary>A source entry that receives its caller's location (SPEC 22.5.1).</summary>
        CallerLocation = 8,
    }

    private static AbiParameter Ret => new("ptr", "ret", AbiParameterKind.ResultSlot);

    private static AbiParameter Context => new("ptr", "context", AbiParameterKind.Context);

    /// <summary>Gets the physical signature of a closed function instance.</summary>
    /// <param name="layout">The compilation's layout.</param>
    /// <param name="convention">The calling convention.</param>
    /// <param name="flags">The signature's facts beyond its Types.</param>
    /// <param name="result">The closed result Type.</param>
    /// <param name="parameters">The closed parameter Types in slot order.</param>
    /// <returns>The signature, without a name.</returns>
    internal static FunctionAbi Of(Layout layout, Convention convention, Flags flags, BoundType result, ReadOnlySpan<BoundType> parameters)
    {
        var value = Value(layout, result);
        var slot = HasSlot(convention, flags, result, value);
        var closure = (flags & (Flags.ErasedClosure | Flags.ConcreteClosure)) != 0;
        var location = (flags & Flags.CallerLocation) != 0;
        var count = (closure ? 2 : 0) + (slot ? 1 : 0) + (location ? 2 : 0);
        for (var i = 0; i < parameters.Length; i++)
        {
            count += ArgumentType(layout, convention, parameters[i]) is null ? 0 : 1;
        }

        var physical = new AbiParameter[count];
        var n = 0;
        if (closure)
        {
            physical[n++] = Environment(flags);
        }

        if (slot)
        {
            physical[n++] = Ret;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (ArgumentType(layout, convention, parameters[i]) is { } type)
            {
                physical[n++] = new(type, "a" + i.ToString(CultureInfo.InvariantCulture), Kind(convention, parameters[i]), i);
            }
        }

        if (closure)
        {
            physical[n++] = Context;
        }

        if (location)
        {
            physical[n++] = RuntimeAbi.Location;
            physical[n++] = RuntimeAbi.LocationLength;
        }

        return new(string.Empty, Result(convention, result, value), physical)
        {
            NoReturn = ReferenceEquals(result, BoundType.Never), ResultSlot = slot, CallerLocation = location,
        };
    }

    /// <summary>Gets whether a signature, under any name, is the one <see cref="Of"/> gives for these inputs.</summary>
    /// <param name="abi">A signature.</param>
    /// <param name="layout">The compilation's layout.</param>
    /// <param name="convention">The calling convention.</param>
    /// <param name="flags">The signature's facts beyond its Types.</param>
    /// <param name="result">The closed result Type.</param>
    /// <param name="parameters">The closed parameter Types in slot order.</param>
    /// <returns>Whether its parameters, result and flags are <see cref="Of"/>'s.</returns>
    internal static bool Matches(FunctionAbi abi, Layout layout, Convention convention, Flags flags, BoundType result, ReadOnlySpan<BoundType> parameters)
    {
        var value = Value(layout, result);
        var slot = HasSlot(convention, flags, result, value);
        var closure = (flags & (Flags.ErasedClosure | Flags.ConcreteClosure)) != 0;
        var location = (flags & Flags.CallerLocation) != 0;
        if (abi.ResultSlot != slot || abi.CallerLocation != location || abi.NoReturn != ReferenceEquals(result, BoundType.Never) ||
            abi.Result != Result(convention, result, value))
        {
            return false;
        }

        // Of's order; a logical parameter is compared without its name, which its slot gives.
        var physical = abi.Parameters;
        var n = 0;
        if ((closure && !Next(physical, ref n, Environment(flags))) || (slot && !Next(physical, ref n, Ret)))
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (ArgumentType(layout, convention, parameters[i]) is { } type && !Logical(physical, ref n, type, Kind(convention, parameters[i]), i))
            {
                return false;
            }
        }

        return (!closure || Next(physical, ref n, Context)) &&
            (!location || (Next(physical, ref n, RuntimeAbi.Location) && Next(physical, ref n, RuntimeAbi.LocationLength))) && n == physical.Length;
    }

    /// <summary>Gets whether the internal convention returns a Type through a result slot: a slot Type of nonzero size.</summary>
    /// <param name="type">The closed result Type.</param>
    /// <param name="value">Its record.</param>
    /// <returns>Whether the result has a slot.</returns>
    internal static bool HasResultSlot(BoundType type, ValueLowering value) => SlotTypes.IsResult(type) && value.Layout.Size > 0;

    /// <summary>Gets the internal convention's result Type: void for a slot Type, else the computation Type (void for Unit and Never).</summary>
    /// <param name="type">The closed result Type.</param>
    /// <param name="value">Its record.</param>
    /// <returns>The LLVM result Type.</returns>
    internal static string ResultType(BoundType type, ValueLowering value) => SlotTypes.IsResult(type) ? "void" : value.ComputationType;

    /// <summary>Gets how a parameter of a Type passes: a string by a shared reference, a slot Type (an owned result) by pointer to the
    /// caller's storage, any other Type by value; every Type by value under the C convention.</summary>
    /// <param name="convention">The convention.</param>
    /// <param name="type">The closed parameter Type.</param>
    /// <returns>The kind.</returns>
    internal static AbiParameterKind Kind(Convention convention, BoundType type)
    {
        if (convention == Convention.C)
        {
            return AbiParameterKind.Value;
        }

        if (ReferenceTypes.IsString(type))
        {
            return AbiParameterKind.SharedReference;
        }

        return SlotTypes.IsResult(type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value;
    }

    private static AbiParameter Environment(Flags flags)
        => new((flags & Flags.ConcreteClosure) != 0 ? "ptr" : "i64", "environment", AbiParameterKind.Environment);

    // Under the internal convention a constructor's or destructor's result slot, or a slot Type's of nonzero size; never under C, which
    // takes no flags.
    private static bool HasSlot(Convention convention, Flags flags, BoundType result, ValueLowering value)
    {
        if (convention == Convention.C)
        {
            return flags == Flags.None ? false : throw new InvalidOperationException("A C signature (SPEC 22.6) takes no flags.");
        }

        return (flags & Flags.ResultSlot) != 0 || HasResultSlot(result, value);
    }

    private static string Result(Convention convention, BoundType result, ValueLowering value)
        => convention == Convention.C ? value.ComputationType : ResultType(result, value);

    // A parameter's argument Type; under the internal convention a zero-sized parameter has none (no physical slot), while under C nothing
    // is omitted (SPEC 22.6's table admits no parameter without one).
    private static string? ArgumentType(Layout layout, Convention convention, BoundType type)
    {
        var value = Value(layout, type);
        if (convention == Convention.Internal)
        {
            return value.Layout.Size == 0 ? null : value.ArgumentType;
        }

        return value.ArgumentType ??
            throw new InvalidOperationException("A C signature (SPEC 22.6) reached the parameter Type " + type.Name + ", which has no argument value.");
    }

    private static bool Next(AbiParameter[] physical, ref int n, AbiParameter expected) => n < physical.Length && physical[n++] == expected;

    private static bool Logical(AbiParameter[] physical, ref int n, string type, AbiParameterKind kind, int index)
        => n < physical.Length && Next(physical, ref n, new(type, physical[n].Name, kind, index));

    private static ValueLowering Value(Layout layout, BoundType type)
        => layout.Of(type).Value ?? throw new InvalidOperationException("Abi reached the Type " + type.Name + ", which Layout does not lay out.");
}
