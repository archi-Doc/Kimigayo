// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum TokenKind : byte
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
    Namespace = 32, // namespace Kimi.Base
    Use, // use Kimi.Base
    Group, // group
    Struct, // struct
    Enum, // enum
    Const, // const
    Static, // static
    Public,
    Protected,
    Private,
    Internal,
    Protected_or_internal,
    Protected_and_internal,

    Switch, // switch
    Case, // case
    Break, // break
    Return, // return

    // Not keyword
    Attribute = 128, // #Attribute
    Identifier,
    Assignment, // =
    Move, // <=
    Map, // =>
    Reference, // &

    OpenParentheses, // (
    CloseParentheses, // )

    Literal, // "text"
    RawLiteral, // """text"""

    SingleLineComment,
    MultiLineComment,
    LineFeed,
}
