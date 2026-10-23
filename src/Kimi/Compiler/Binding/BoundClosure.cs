// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The published closure definition (ClosureDef, plan 5.3): its environment, signature and minimum call receiver, and each capture's
// source and environment binding, which Binding fills as it binds the closure (Binding.Closures.cs) and the MIR builder reads.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // The retained closure definition and its capture records.

public readonly record struct BoundCapture(BindingSymbol Source, BindingSymbol Environment)
{
    // How the entry initializes its environment binding (SPEC 7.7.2), as the HIR Adjust column states acquisitions.
    internal HirAdjust Adjust => HirAdjust.Of(this.Environment.CaptureAcquisition);
}

/// <summary>A retained capture environment, signature and minimum call receiver.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType Signature { get; internal set; } = null!;

    public BoundType? EnvironmentType { get; internal set; }

    public SemanticsKind Receiver { get; internal set; } = SemanticsKind.Ref;

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
