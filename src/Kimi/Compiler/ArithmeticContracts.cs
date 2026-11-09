// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The root statement of the arithmetic Contracts (SPEC 13.3.3): their standard declaration identities, independent of a bound Contract's own
// Symbol and of user spelling, and which Types conform to each by a built-in witness (Supports).
// Input: a Type, a declaration identity. Output: the facts; Binding reads every one, MIR and the Backend Supports alone (MirTypes.Operates).
// Must not: prove a generic Type's conformance (Binding's), or map MIR's operators (MirTypes' pairing).
internal static class ArithmeticContracts
{
    // SPEC 13.3.3: the built-in conformances, whose witnesses behave as SPEC 13.3.1's operators: the operator table's domain of + - * / % and
    // unary - (MirTypes.Operates) and Binding's conformance.
    internal static bool Supports(BoundType type, KimiDeclarationId id)
        => type.IsNumeric && (id == KimiDeclarationId.Negatable ? !type.IsUnsignedInteger
            : id is >= KimiDeclarationId.Addable and <= KimiDeclarationId.RemainderProvider && (id != KimiDeclarationId.RemainderProvider || !type.IsFloatingPoint));

    internal static Parsing.KotoKind Operator(KimiDeclarationId id) => id switch
    {
        KimiDeclarationId.Addable or KimiDeclarationId.LeftAddable => Parsing.KotoKind.Plus,
        KimiDeclarationId.Subtractable or KimiDeclarationId.LeftSubtractable => Parsing.KotoKind.Minus,
        KimiDeclarationId.Multipliable or KimiDeclarationId.LeftMultipliable => Parsing.KotoKind.Asterisk,
        KimiDeclarationId.Dividable or KimiDeclarationId.LeftDividable => Parsing.KotoKind.Slash,
        KimiDeclarationId.RemainderProvider or KimiDeclarationId.LeftRemainderProvider => Parsing.KotoKind.Percent,
        KimiDeclarationId.Negatable => Parsing.KotoKind.PrefixMinus,
        _ => Parsing.KotoKind.Invalid,
    };

    internal static bool IsArithmetic(KimiDeclarationId? id) => id is >= KimiDeclarationId.Addable and <= KimiDeclarationId.Negatable;

    internal static KimiDeclarationId? Identity(BindingSymbol? contract)
        => contract?.Declaration.BoundSymbol?.LibraryDeclaration is { } id && IsArithmetic(id) ? id : null;

    internal static bool IsLeft(KimiDeclarationId id) => id is >= KimiDeclarationId.LeftAddable and <= KimiDeclarationId.LeftRemainderProvider;

    internal static string Method(KimiDeclarationId id) => id switch
    {
        KimiDeclarationId.Addable => "added",
        KimiDeclarationId.Subtractable => "subtracted",
        KimiDeclarationId.Multipliable => "multiplied",
        KimiDeclarationId.Dividable => "divided",
        KimiDeclarationId.RemainderProvider => "remainder",
        KimiDeclarationId.LeftAddable => "addedFrom",
        KimiDeclarationId.LeftSubtractable => "subtractedFrom",
        KimiDeclarationId.LeftMultipliable => "multipliedFrom",
        KimiDeclarationId.LeftDividable => "dividedFrom",
        KimiDeclarationId.LeftRemainderProvider => "remainderFrom",
        KimiDeclarationId.Negatable => "negated",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
