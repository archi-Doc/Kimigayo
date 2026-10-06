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
    Unbound,

    // SPEC 5.2.2: the fresh anchor of a raw Place borrow; Binder is the borrow. It has no upper bound, so it fits every
    // destination like static, while ownership analysis checks the borrow's Loan and those derived from it under the anchor.
    Anchor,
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
        this.InputIndex = slot;
    }

    public static BoundOrigin Static { get; } = new(OriginKind.Static, name: "static");

    public OriginKind Kind { get; }

    public Koto? Binder { get; }

    public int Slot { get; }

    public string Name { get; }

    public IReadOnlyList<BoundOrigin> Operands { get; }

    /// <summary>Gets the value input carrying this Origin; distinct from its inference slot.</summary>
    public int InputIndex { get; internal init; }

    internal Koto? Occurrence { get; init; }

    internal int TargetSlot { get; init; }

    // SPEC 10.8, 15.3.6: an inferred local region that stands for a known call signature's own Origin in a slot solution. It holds no
    // Loans and is never displayed; Binder is the argument whose signature supplied it, so each call site has its own region.
    internal bool Open { get; init; }

    internal BindingSymbol? BorrowCondition { get; set; }
}

/// <summary>A retained semantic requirement, independent of successful name resolution. An <see cref="BindingObligationKind.OriginOutlives"/>
/// obligation with <c>Equality</c> requires <c>Longer == Shorter</c>, the relation at an invariant position (SPEC 15.6.1); one with a
/// <c>Clause</c> is that clause of a Type substituted at the Type occurrence <c>Use</c>, a declared relation (SPEC 15.3.3); one with
/// <c>WellFormed</c> is a callee's result premise substituted at the call <c>Use</c>, the well-formedness of its result (SPEC 15.3.7).</summary>
public readonly record struct BindingObligation(BindingObligationKind Kind, Koto Use, BindingDeadline Deadline, BoundType? Type = null, BoundOrigin? Longer = null, BoundOrigin? Shorter = null, BoundLength? Length = null, bool Equality = false, Koto? Clause = null, bool WellFormed = false);

internal enum TypePosition : byte
{
    Explicit,
    Parameter,
    Result,
    Local,
    InstanceStorage,
    StaticStorage,
}

// SPEC 10.7, 15.3.7: one Callable comparison of an implementation signature (Actual) with a required one (Expected). The Origins that
// the implementation binds per call are instantiated at the required inputs in the same positions; the required per-call Origins are
// rigid symbols of the comparison, and every other Origin is compared as written.
internal readonly struct CallableInstance
{
    private readonly BoundType? actual;
    private readonly Koto? actualBinder;
    private readonly BoundType? expected;
    private readonly Koto? expectedBinder;

    internal CallableInstance(BoundType? actual, Koto? actualBinder, BoundType? expected, Koto? expectedBinder)
    {
        this.actual = actual;
        this.actualBinder = actualBinder;
        this.expected = expected;
        this.expectedBinder = expectedBinder;
    }

    internal BoundType? Actual => this.actual;

    // A direct input whose outer Origin the implementation binds at that input's own position.
    internal bool IsQuantifiedInput(BoundType input, int position)
        => this.actualBinder is not null && input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, OriginArguments.Count: 0, Origin: { Kind: OriginKind.Input, Occurrence: null } origin } &&
            ReferenceEquals(origin.Binder, this.actualBinder) && origin.Slot == position;

    internal BoundOrigin Instantiate(BoundOrigin origin)
    {
        if (this.actual is null || this.actualBinder is null || origin is not { Kind: OriginKind.Input, Occurrence: null } || !ReferenceEquals(origin.Binder, this.actualBinder) ||
            (uint)origin.Slot >= (uint)this.actual.Components[0].Components.Count || this.expected is null ||
            (uint)origin.Slot >= (uint)this.expected.Components[0].Components.Count)
        {
            return origin;
        }

        var input = this.actual.Components[0].Components[origin.Slot];
        return ReferenceEquals(input.Origin, origin) && this.IsQuantifiedInput(input, origin.Slot) && this.expected.Components[0].Components[origin.Slot].Origin is { } required ? required : origin;
    }

    // A per-call Origin of the required signature: the outer Origin of a direct required input, bound by that signature at its slot.
    internal bool IsRequiredSlot(BoundOrigin origin)
        => this.expected is not null && this.expectedBinder is not null && origin is { Kind: OriginKind.Input, Occurrence: null } && ReferenceEquals(origin.Binder, this.expectedBinder) &&
            (uint)origin.Slot < (uint)this.expected.Components[0].Components.Count && ReferenceEquals(this.expected.Components[0].Components[origin.Slot].Origin, origin);

    // SPEC 10.7, 15.3.6: a universal Origin of the implementation that none of its inputs mentions, such as the result-only `s` of
    // `constant() -> ref/i32 during s`, is a call-time Origin that each call instantiates; it is instantiated to an open region of the
    // call, a local region that holds no Loans and is solved by its uses.
    internal bool IsResultOnlyOrigin(BoundOrigin origin)
        => this.actual is not null && this.actualBinder is FunctionKoto && origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, this.actualBinder) &&
            !Mentions(this.actual.Components[0], origin, 0);

    private static bool Mentions(BoundType type, BoundOrigin origin, int depth)
    {
        if (depth > 64 || !type.CarriesOrigin)
        {
            return depth > 64;
        }

        if (Names(type.Origin, origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Names(type.OriginArguments[i], origin))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (Mentions(type.Components[i], origin, depth + 1))
            {
                return true;
            }
        }

        return false;

        static bool Names(BoundOrigin? candidate, BoundOrigin origin)
        {
            if (candidate is null)
            {
                return false;
            }

            if (ReferenceEquals(candidate, origin) || (candidate.Kind == OriginKind.Parameter && ReferenceEquals(candidate.Binder, origin.Binder) && candidate.Slot == origin.Slot))
            {
                return true;
            }

            for (var i = 0; i < candidate.Operands.Count; i++)
            {
                if (Names(candidate.Operands[i], origin))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

// One closure call whose result is bound to its receiver (SPEC 15.8.2): the closure, its environment plan, the receiver's Place Origin
// (none for a Consuming call), whether the result is Copy, and the call.
internal readonly record struct ReceiverContext(FunctionKoto Function, BoundClosure Closure, BoundOrigin? Receiver, bool Copy, Koto Use);

// CallQualifier: the Type expression qualifies a called member in an expression (SPEC 15.4.4), whose omitted Origin slots the call
// infers; Member judges them once the member is known.
internal readonly record struct TypeBindingContext(TypePosition Position, Koto Owner, int Slot = 0, bool Direct = false, bool SuppressOuter = false, bool CallQualifier = false)
{
    internal TypeBindingContext Nested => this with { Direct = false, SuppressOuter = false };
}
