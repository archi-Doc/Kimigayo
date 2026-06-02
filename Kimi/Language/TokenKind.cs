// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum TokenKind : byte
{
    None,

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
    Var, // var

    If, // if
    ElseIf, // else if
    Else, // else
    For, // for
    Loop, // loop
    Switch, // switch
    Case, // case
    Break, // break
    Return, // return
    Continue, // continue

    // Not keyword
    Identifier = 128,
    Attribute, // #Attribute
    Literal, // "text"
    RawLiteral, // """text"""
    SingleLineComment, // // comment
    MultiLineComment, // /* comment */
    LineFeed, // \n or \r\n

    Move, // <=
    Map, // =>
    Reference, // &

    // Single token
    Dot, // .
    Comma, // ,
    OpenBracket, // [
    CloseBracket, // ]
    OpenParenthesis, // (
    CloseParenthesis, // )
    Colon, // :
    Semicolon, // ;
    Dollar, // $
    Tilde, // ~

    AmpersandAmpersand, // &&
    AmpersandEquals, // &=
    Ampersand, // &
    AsteriskEquals, // *=
    Asterisk, // *
    BarBar, // ||
    BarEquals, // |=
    Bar, // |
    Caret, // ^
    CaretEquals, // ^=
    Equals, // =
    EqualsEquals, // ==
    EqualsGreaterThan, // =>
    Exclamation, // !
    ExclamationEquals, // !=
    GreaterThan, // >
    GreaterThanEquals, // >=
    GreaterThanGreaterThanEquals, // >>=
    LessThanEquals, // <=
    LessThanLessThanEquals, // <<=
    LessThan, // <
    Minus, // -
    MinusEquals, // -=
    MinusMinus, // --
    Percent, // %
    PercentEquals, // %=
    Plus, // +
    PlusEquals, // +=
    PlusPlus, // ++
    Slash, // /
    SlashEquals, // /=
}
