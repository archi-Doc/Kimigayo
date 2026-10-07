// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The standard declaration identities, independent of a bound Contract's own Symbol and of user spelling.
internal static class ArithmeticContracts
{
    internal static bool IsArithmetic(KimiDeclarationId? id) => id is >= KimiDeclarationId.Addable and <= KimiDeclarationId.Negatable;

    internal static KimiDeclarationId? Identity(BindingSymbol contract)
        => contract.Declaration.BoundSymbol?.LibraryDeclaration is { } id && IsArithmetic(id) ? id : null;

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
