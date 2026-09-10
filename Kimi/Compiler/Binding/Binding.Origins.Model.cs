// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared semantic vocabulary.

public enum GenericSlotKind : byte
{
    Type,
    Pair,
    Length,
}

public enum OriginKind : byte
{
    Static,
    Parameter,
    Input,
    Inference,
    Intersection,
    Projection,
}

public enum OriginVariance : byte
{
    Unused,
    Covariant,
    Contravariant,
    Invariant,
}

public enum LoanRequirement : byte
{
    None,
    Ref,
    Uniq,
}

public enum BindingObligationKind : byte
{
    OriginOutlives,
    OriginInference,
    TypeRole,
    TypeFormation,
    Loan,
    StaticStorage,
}

public enum BindingDeadline : byte
{
    Definition,
    BodyOrigins,
    Instantiation,
}

/// <summary>A declared slot; a pair consumes one argument and retains its whole type.</summary>
public sealed class GenericSlot(GenericSlotKind kind, BindingSymbol symbol, BindingSymbol? semantics)
{
    public GenericSlotKind Kind { get; } = kind;

    public BindingSymbol Symbol { get; } = symbol;

    public BindingSymbol? Semantics { get; } = semantics;

    public OriginVariance OriginVariance { get; internal set; }
}

/// <summary>Declaration-bound Origin identity and inferred declaration requirements.</summary>
public sealed class OriginParameter(string name, int slot, BoundOrigin origin, SourceSpan span)
{
    public string Name { get; } = name;

    public int Slot { get; } = slot;

    public BoundOrigin Origin { get; } = origin;

    public SourceSpan Span { get; } = span;

    public OriginVariance Variance { get; internal set; }

    public LoanRequirement LoanRequirement { get; internal set; }
}

/// <summary>Shared ordered generic and Origin binders for one declaration.</summary>
public sealed class DeclarationSchema(GenericSlot[] slots, OriginParameter[] origins)
{
    public IReadOnlyList<GenericSlot> GenericSlots { get; } = slots;

    public IReadOnlyList<OriginParameter> Origins { get; } = origins;
}

/// <summary>Immutable Origin expression. Identity belongs to its binder, never its spelling.</summary>
public sealed class BoundOrigin
{
    internal BoundOrigin(OriginKind kind, Koto? binder = null, int slot = 0, string? name = null, BoundOrigin[]? operands = null)
    {
        this.Kind = kind;
        this.Binder = binder;
        this.Slot = slot;
        this.Name = name ?? kind.ToString();
        this.Operands = operands ?? [];
    }

    public static BoundOrigin Static { get; } = new(OriginKind.Static, name: "static");

    public OriginKind Kind { get; }

    public Koto? Binder { get; }

    public int Slot { get; }

    public string Name { get; }

    public IReadOnlyList<BoundOrigin> Operands { get; }
}

/// <summary>A retained semantic requirement, independent of successful name resolution.</summary>
public readonly record struct BindingObligation(BindingObligationKind Kind, Koto Use, BindingDeadline Deadline, BoundType? Type = null, BoundOrigin? Longer = null, BoundOrigin? Shorter = null);

internal enum TypePosition : byte
{
    Explicit,
    Parameter,
    Result,
    Local,
    InstanceStorage,
    StaticStorage,
}

internal readonly record struct TypeBindingContext(TypePosition Position, Koto Owner, int Slot = 0, bool Direct = false, bool SuppressOuter = false)
{
    internal TypeBindingContext Nested => this with { Direct = false, SuppressOuter = false };
}
