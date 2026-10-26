// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Backend's statement of SPEC 13.3.3's table (DECISIONS R5-M126): the MIR operation an arithmetic Contract's built-in witness on a
// numeric Type is, the one relation the instance collector (the arithmetic shim's capability check, K 4.7) and code generation (the shim's
// operator row, FunctionCodegen.Shims) read, as both read a statement's operation.
// Input: an arithmetic shim's Contract, by its declaration identity. Output: its MIR operator, a BinaryOp's or UnaryOp's kind and operator.
// Invariants: a row per Contract with a built-in witness (ArithmeticContracts.Supports), the operator the MIR builder makes for the token
// Binding's ArithmeticContracts.Operator gives it (a test ties them); any other Contract is a broken contract.
// Must not: decide which Types have the witness (Binding's), or restate the operator table's entries or the capability table's rows.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>The MIR operations of the arithmetic Contracts' built-in witnesses (SPEC 13.3.3).</summary>
internal static class BuiltinArithmetic
{
    /// <summary>Gets the MIR operation of an arithmetic Contract's built-in witness.</summary>
    /// <param name="contract">The Contract's declaration identity.</param>
    /// <returns>Its rvalue kind and operator.</returns>
    internal static (MirRvalueKind Kind, byte Op) Operation(KimiDeclarationId? contract) => contract switch
    {
        KimiDeclarationId.Addable => (MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Add),
        KimiDeclarationId.Subtractable => (MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Sub),
        KimiDeclarationId.Multipliable => (MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Mul),
        KimiDeclarationId.Dividable => (MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Div),
        KimiDeclarationId.RemainderProvider => (MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Rem),
        KimiDeclarationId.Negatable => (MirRvalueKind.UnaryOp, (byte)MirUnaryOp.Neg),
        _ => throw new InvalidOperationException($"The Contract {contract} has no built-in arithmetic witness."),
    };
}
