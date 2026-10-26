// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The published closure definition (C2; ClosureDef, plan 5.3; DECISIONS R4-M126, R4-M145).
// - Responsibility: what a binding of an anonymous function publishes: each capture's source and environment binding (Storage), the
//   environment Type, the signature, the evaluating body, and the call receiver facts (SPEC 7.7.3): the strongest requirement over
//   every admitted case (Receiver), the requirement every case has (CaseFreeReceiver), each capture whose acquisition by value
//   Reborrows by case (BoundCapture.BareReborrows), the first Exclusive cause, and whether this binding has classified them
//   (Classified).
// - Inputs: Binding.Closures (the literal's binding: Signature, EnvironmentType, EvaluatingBody and the resets), Binding.Captures
//   (Storage), Binding.ClosureReceivers (the receiver facts).
// - Outputs: Receiver for acquisition and every receiver reader (value calls, candidate selection and Callable proofs, effects, the
//   MIR builder, the old analysis and Emission); the cause for the write record; for a view of one admitted case, that case's
//   receiver: CaseFreeReceiver unless it is Shared, else Exclusive where a capture with BareReborrows has an exclusive layer as its
//   case Type, else Shared.
// - Invariants: Receiver is CaseFreeReceiver raised to Exclusive when a capture has BareReborrows, so Consuming never depends on a
//   case; the receiver facts are the current binding's exactly when Classified; Of is the definition once the signature is bound.
// - Prohibitions: only ClosureEffects classifies the receiver facts, which the binding resets; no reader re-derives a receiver from the body.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // The retained closure definition and its capture records.

public readonly record struct BoundCapture(BindingSymbol Source, BindingSymbol Environment)
{
    // How the entry initializes its environment binding (SPEC 7.7.2), as the HIR Adjust column states acquisitions.
    internal HirAdjust Adjust => HirAdjust.Of(this.Environment.CaptureAcquisition);

    // SPEC 7.7.3, 8.14: an acquisition of the environment binding itself by value without an operator, in the body (as the Adjust
    // rule takes it from its consumer, a discarded Place included: ClosureEffects.ConsumedByValue) or by a nested Closure's bare
    // entry, Reborrows in some admitted case, so the call is Exclusive exactly in the cases where the binding's case Type is exclusive.
    internal bool BareReborrows { get; init; }
}

/// <summary>A retained capture environment, signature and call receiver.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType Signature { get; internal set; } = null!;

    public BoundType? EnvironmentType { get; internal set; }

    public SemanticsKind Receiver { get; internal set; } = SemanticsKind.Ref;

    // The requirement of every admitted case: Receiver without the captures' bare Reborrows by case (SPEC 7.7.3).
    internal SemanticsKind CaseFreeReceiver { get; set; } = SemanticsKind.Ref;

    // Whether the current binding has classified the receiver facts, from which on its receiver Origin and its environment
    // bindings' Origins are no longer pending (DECISIONS R4-M126).
    internal bool Classified { get; set; }

    // The first capture use that made the call Exclusive, and whether it Reborrows a captured exclusive reference (SPEC 7.7.3).
    internal Koto? ExclusiveUse { get; set; }

    internal bool ExclusiveReborrow { get; set; }

    // The body that evaluates the literal and binds its header and captures (SPEC 15.3.4; Binding.ExecutableBodies.cs). MIR never reads it.
    internal ExecutableBody EvaluatingBody { get; set; }

    internal List<BoundCapture> Storage { get; } = new();

    internal List<BindingSymbol> SymbolPool { get; } = new();

    // The closure definition (ClosureDef, plan 5.3) of an anonymous function's latest binding, once it bound the inputs, the result and the
    // environment, whatever its creation's outcome (DECISIONS R5-M63; Binding.BindClosure clears the signature first); its substitution is
    // TyCtxt.SubstOfClosure of its Type.
    internal static BoundClosure? Of(FunctionKoto function) => function.ClosureStorage is { Signature: not null } plan ? plan : null;
}
