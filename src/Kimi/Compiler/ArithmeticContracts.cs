// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The root Type statement of the arithmetic Contracts (SPEC 13.3.3).
// Responsibility: which Types conform to each arithmetic Contract by a built-in witness (Supports), and which standard declaration
// identities are arithmetic Contracts and their Left forms.
// Inputs: a Type and a standard declaration identity. Outputs: the facts alone; Binding reads every one, MIR Supports (MirTypes.Operates).
// Invariants: every fact is a function of its arguments, independent of a bound Contract's own Symbol and of user spelling.
// Must not: read syntax or a phase's Symbols (Binding's pairing with tokens, methods and declared Contracts is ArithmeticContractSyntax),
// prove a generic Type's conformance (Binding's), or name another phase's operators (MIR's pairing is MirTypes.ArithmeticOperations).
internal static class ArithmeticContracts
{
    // SPEC 13.3.3: the built-in conformances, whose witnesses behave as SPEC 13.3.1's operators: the operator table's domain of + - * / %
    // and unary - (MirTypes.Operates) and Binding's conformance.
    internal static bool Supports(BoundType type, KimiDeclarationId id)
        => type.IsNumeric && (id == KimiDeclarationId.Negatable ? !type.IsUnsignedInteger
            : id is >= KimiDeclarationId.Addable and <= KimiDeclarationId.RemainderProvider && (id != KimiDeclarationId.RemainderProvider || !type.IsFloatingPoint));

    internal static bool IsArithmetic(KimiDeclarationId? id) => id is >= KimiDeclarationId.Addable and <= KimiDeclarationId.Negatable;

    internal static bool IsLeft(KimiDeclarationId id) => id is >= KimiDeclarationId.LeftAddable and <= KimiDeclarationId.LeftRemainderProvider;
}
