// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    internal static KotoKind CompoundOperation(KotoKind assignment) => assignment switch
    {
        KotoKind.PlusEquals => KotoKind.Plus,
        KotoKind.MinusEquals => KotoKind.Minus,
        KotoKind.AsteriskEquals => KotoKind.Asterisk,
        KotoKind.SlashEquals => KotoKind.Slash,
        KotoKind.PercentEquals => KotoKind.Percent,
        KotoKind.AmpersandEquals => KotoKind.Ampersand,
        KotoKind.CaretEquals => KotoKind.Caret,
        KotoKind.BarEquals => KotoKind.Bar,
        KotoKind.LessThanLessThanEquals => KotoKind.LessThanLessThan,
        KotoKind.GreaterThanGreaterThanEquals => KotoKind.GreaterThanGreaterThan,
        _ => KotoKind.Invalid,
    };
}
