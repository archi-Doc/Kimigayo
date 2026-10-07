// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The standard declaration identities, independent of a bound Contract's own Symbol and of user spelling.
internal static class ArithmeticContracts
{
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
