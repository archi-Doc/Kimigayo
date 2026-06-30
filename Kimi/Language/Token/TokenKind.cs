// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

/// <summary>
/// Represents the lexical token kinds produced by the lexer.<br/>
///  When adding a new TokenKind, remember to also add the corresponding descriptor to TokenHelper.TokenDescriptors.
/// </summary>
public enum TokenKind : byte
{
    Invalid,

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

    // Keywords (Group)
    Const = 32,
    Shared,
    Public,
    Protected,
    Private,
    Internal,
    ProtectedOrInternal,
    ProtectedAndInternal,
    Var,
    True,
    False,
    String,

    // Block keyword
    Group, // group
    Struct, // struct
    Enum, // enum
    For, // for
    Loop, // loop
    Match, // match

    // Block or expression keyword
    If, // if
    Else, // else
    Block, // block
    // EqualsGreaterThan, // =>
    Is, // is
    Not, // not
    And, // and
    Or, // Or

    // Non-block keyword
    Return, // method/return
    Break, // loop/break
    Continue, // continue
    Yield, // yield

    // Not keyword
    Identifier = 128,
    StartBlock,
    EndBlock,
    Separator,
    NumericLiteral, // 1.23d
    CharLiteral, // 'a'
    Literal, // "text"
    RawLiteral, // """text"""
    SingleLineComment, // // comment
    MultiLineComment, // /* comment */
    // LineFeed, // \n or \r\n

    // Move, // <=
    // Map, // =>
    // Reference, // &

    // Single token
    Sharp, // #
    Comma, // ,
    OpenBracket, // [
    CloseBracket, // ]
    OpenParenthesis, // (
    CloseParenthesis, // )
    OpenBrace, // {
    CloseBrace, // }
    Colon, // :
    Semicolon, // ;
    Dollar, // $
    Tilde, // ~
    Question, // ?

    Ampersand, // &
    AmpersandAmpersand, // &&
    AmpersandEquals, // &=
    Asterisk, // *
    AsteriskEquals, // *=
    Bar, // |
    BarBar, // ||
    BarEquals, // |=
    Caret, // ^
    CaretEquals, // ^=
    Dot, // .
    DotDot, // ..
    DotDotEquals, // ..=
    Equals, // =
    EqualsEquals, // ==
    EqualsGreaterThan, // =>
    Exclamation, // !
    ExclamationEquals, // !=
    GreaterThan, // >
    GreaterThanEquals, // >=
    GreaterThanGreaterThan, // >>
    GreaterThanGreaterThanEquals, // >>=
    LessThan, // <
    LessThanEquals, // <=
    LessThanLessThan, // <<
    LessThanLessThanEquals, // <<=
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
