// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

[Flags]
public enum ModifierKind : ushort
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
    Unsafe = 64,
    Virtual = 128,
    Override = 256,
}
