// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding's pairing of the arithmetic Contracts (SPEC 13.3.3's table) with source: each Contract's operator token and requirement
// method, and the identity of a declared Contract's Symbol. The Types' facts are the root ArithmeticContracts'.
internal static class ArithmeticContractSyntax
{
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

    internal static KimiDeclarationId? Identity(BindingSymbol? contract)
        => contract?.Declaration.BoundSymbol?.LibraryDeclaration is { } id && ArithmeticContracts.IsArithmetic(id) ? id : null;

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
