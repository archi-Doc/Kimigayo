// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Language;

[Flags]
public enum KotoModifierKind : byte
{
    NoModifier = 0,
    Public = 1,
    Protected = 2,
    Private = 3,
    Internal = 4,
    ProtectedOrInternal = 5,
    ProtectedAndInternal = 6,

    Static = 16,
    Open = 32,
}
