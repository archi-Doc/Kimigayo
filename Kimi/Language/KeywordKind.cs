// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum KeywordKind : byte
{
    // Primitive types
    Bool,
    Isize,
    Usize,
    I8,
    I16,
    I32,
    I64,
    I128,
    U8,
    U16,
    U32,
    U64,
    U128,
    F32,
    F64,

    // Other
    Namespace = 32,
    Use,
    Group,
    Struct,
    Enum,
    Const,
    Public,
    Protected,
    Private,
    Internal,
    Protected_or_internal,
    Protected_and_internal,
    Reference,
}
