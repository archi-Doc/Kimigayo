// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum TokenKind : byte
{
    // Keywords (Primitive types)
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

    // Keywords (Other)
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

    If, // if
    ElseIf, // else if
    Else, // else
    Switch, // switch
    Case, // case
    Break, // break
    Return, // return
    Continue, // continue

    // Not keyword
    Attribute = 128, // #Attribute
    Identifier,
    Dot, // .
    Comma, // ,
    Assignment, // =
    Move, // <=
    Map, // =>
    Reference, // &

    OpenBracket, // (
    CloseBracket, // )

    Literal, // "text"
    RawLiteral, // """text"""

    AmpersandAmpersandToken,
    AmpersandEqualsToken,
    AmpersandToken,
    AsteriskEquals,
    Asterisk,
    BarBarToken,
    BarEqualsToken,
    BarToken,
    SlashEquals, // /=
    Slash, // /

    SingleLineComment, // // comment
    MultiLineComment, // /* comment */
    LineFeed, // \n or \r\n
}
