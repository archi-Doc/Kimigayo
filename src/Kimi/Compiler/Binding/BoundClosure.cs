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
//   MIR builder and the old analysis); the cause for the write record; for a view of one admitted case, that case's
//   receiver: CaseFreeReceiver unless it is Shared, else Exclusive where a capture with BareReborrows has an exclusive layer as its
//   case Type, else Shared.
// - Access (DECISIONS R5-M63 (1), R4-M152; agreed with BU, the Binding owner): two accessors with two meanings, so each reader
//   chooses. FunctionKoto.ClosureDefinition is the definition, the binding's plan once its signature is bound, whatever the creation's
//   outcome: every reader that needs the closure's definition (its signature, parts or body) reads it, through a Closure Type, a
//   signature or an Origin's binder (Of, the Type route, answers only for a Closure Type) or through the literal (R5-M63 (1)'s HIR
//   publisher and MIR builder, and Binding's judgments of a literal's parts). FunctionKoto.ClosureAsCreated is the closure as created,
//   that definition while the literal's creation is Resolved and null when the creation failed: only a reader that asks whether the
//   creation at its position succeeded reads it (the old Analysis per creation, control flow's created closure).
// - Invariants: Receiver is CaseFreeReceiver raised to Exclusive when a capture has BareReborrows, so Consuming never depends on a
//   case; the receiver facts are the current binding's exactly when Classified; a failed creation keeps its definition.
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

    // The Type route to a closure's definition (ClosureDef, plan 5.3; DECISIONS R5-M63 (1), R4-M152): a Closure Type's literal's
    // FunctionKoto.ClosureDefinition, whatever its creation's outcome (Binding.BindClosure clears the signature first; its substitution
    // is TyCtxt.SubstOfClosure of the Type); null for any other Type.
    internal static BoundClosure? Of(BoundType? closure)
        => closure is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } ? function.ClosureDefinition : null;
}
