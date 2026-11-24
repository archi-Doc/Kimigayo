// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The published closure definition (C2; ClosureDef, plan 5.3; DECISIONS R4-M126, R4-M145).
// - Responsibility: what a binding of an anonymous function publishes: each capture's source and environment binding (Storage), the
//   enclosing regions its environment carries (Carried), the environment Type, the signature, whether the body
//   infers the result (InfersResult), and the call receiver facts (SPEC 7.7.3): the strongest requirement over every admitted case
//   (Receiver), the requirement every case has (CaseFreeReceiver), each capture whose acquisition by value Reborrows by case
//   (BoundCapture.BareReborrows), the first Exclusive cause, and whether this binding has classified them (Classified).
// - Inputs: Binding.Closures (the literal's binding: Signature, EnvironmentType, InfersResult and the resets), Binding.Captures
//   (Storage and Carried), Binding.ClosureReceivers (the receiver facts). The evaluating body is the literal's symbol's
//   (BindingSymbol.DeclaringBody, written at the header scope, ClosureHeaderScope).
// - Outputs: InfersResult for Binding's discard rule (Binding.DiscardsFunctionBody); Receiver for acquisition and every receiver
//   reader (value calls, candidate selection and Callable proofs, effects and the MIR builder); the cause for the write record; for a
//   view of one admitted case, that case's
//   receiver: CaseFreeReceiver unless it is Shared, else Exclusive where a capture with BareReborrows has an exclusive layer as its
//   case Type, else Shared.
// - Access (DECISIONS R5-M63 (1), R4-M152, R5-M193; agreed with BU, the Binding owner): two accessors with two meanings, so each
//   reader chooses. Of is the definition, the plan once the closure's latest binding bound its header and body, classified its
//   receiver and published it (Published, set by Binding.PublishClosure on both of its paths), whatever the creation's outcome (DECISIONS
//   R5-M63 (1) as amended by PLAN G1110). Its Signature is null exactly where a parameter's or the result's slot failed (the
//   failed-slot fact, Binding.Stated); such a definition reaches the Type route only as its own body's receiver, since its creation is
//   Error and forms no value of its Closure Type, and a reader of a Closure value's Signature that meets one throws (a broken contract
//   that ends the check faulted, DECISIONS BU-102), never a fallback. Every reader
//   that needs the closure's definition (its signature, parts or body) reads it, through a Closure Type, a signature or an Origin's
//   binder (the Type route, which answers only for a Closure Type) or through the literal (the literal route: R5-M63 (1)'s HIR
//   publisher and MIR builder, and Binding's judgments of a literal's parts, which read it as FunctionKoto.ClosureDefinition until R7).
//   FunctionKoto.ClosureAsCreated is the closure as created,
//   that definition while the literal's creation is Resolved and null when the creation failed: only a reader that asks whether the
//   creation at its position succeeded reads it (control flow's created closure).
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
    // rule takes it from its consumer, a discarded Place included: ClosureEffects.TakenByCase) or by a nested Closure's bare
    // entry, Reborrows in some admitted case, so the call is Exclusive exactly in the cases where the binding's case Type is exclusive.
    internal bool BareReborrows { get; init; }
}

/// <summary>A retained capture environment, signature and call receiver.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType? Signature { get; internal set; }

    public BoundType? EnvironmentType { get; internal set; }

    public SemanticsKind Receiver { get; internal set; } = SemanticsKind.Ref;

    // Whether this binding published the definition (Binding.PublishClosure, on both of its paths; cleared where BindClosure clears the
    // Signature): the one gate of Of. Not Classified, which is set before JudgePendingRelations and PublishedResult, where the
    // EnvironmentType is still the preliminary one that TyCtxt.ClosureEnvironment would read (DECISIONS R5-M63 (1) as amended).
    internal bool Published { get; set; }

    // The requirement of every admitted case: Receiver without the captures' bare Reborrows by case (SPEC 7.7.3).
    internal SemanticsKind CaseFreeReceiver { get; set; } = SemanticsKind.Ref;

    // Whether the current binding has classified the receiver facts, from which on its receiver Origin and its environment
    // bindings' Origins are no longer pending (DECISIONS R4-M126).
    internal bool Classified { get; set; }

    // The first capture use that made the call Exclusive, and whether it Reborrows a captured exclusive reference (SPEC 7.7.3).
    internal Koto? ExclusiveUse { get; set; }

    internal bool ExclusiveReborrow { get; set; }

    // Whether this binding's body infers the result, neither a written result nor a fixed expectation having fixed it before the body
    // (SPEC 10.5.4; DECISIONS BU-109). Written before the body is bound; MIR never reads it.
    internal bool InfersResult { get; set; }

    internal List<BoundCapture> Storage { get; } = new();

    // SPEC 15.6.5 (R7-O2 A1+A2, O2-H): the region slots the entries' Types name, the Closure's own entries only, with no test of whether
    // they are carried (no candidate is read while the body is bound); a written list's own entries recorded before the header (and an
    // enclosing omitted list's forwarded entries before the nested header), an omitted list's own entries at each use; each kept once by
    // Binding.Captures (CarryOrigin), read by Binding.CarriedRegion and CarriedRegions, which admit them (AdmittedEntry). MIR never reads it.
    internal List<BoundOrigin> Carried { get; } = new();

    // SPEC 15.6.5, 15.3.6 (R7-O2 A1+A2; the owner's O-471 (a)): the regions the body carries, in turn included (Binding.CarriedRegions),
    // formed once no body is being bound and once per region-graph version (WalkVersion; -1 when not formed, reset with Carried when the
    // literal is bound again); read by Binding.CarriedRegion and the body's oracle (Binding.CarriedWalk). MIR never reads it.
    internal List<BoundOrigin> Walk { get; } = new();

    internal int WalkVersion { get; set; } = -1;

    internal List<BindingSymbol> SymbolPool { get; } = new();

    // The Type route to a closure's definition (ClosureDef, plan 5.3; DECISIONS R5-M63 (1), R4-M152): a Closure Type's literal's
    // definition (the literal route), whatever its creation's outcome (Binding.BindClosure clears the signature first; its substitution
    // is TyCtxt.SubstOfClosure of the Type); null for any other Type.
    internal static BoundClosure? Of(BoundType? closure)
        => closure is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } ? Of(function) : null;

    // The literal route to a closure's definition (the file's Access paragraph: R5-M63 (1) as amended, R4-M152): the literal's plan once
    // its latest binding published it (Published); null for a named function, an unbound literal and until the definition is published.
    internal static BoundClosure? Of(FunctionKoto literal)
        => HirTables.PlanOf(literal).Plan is BoundClosure { Published: true } plan ? plan : null;
}
