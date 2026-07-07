// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

/// <summary>
/// Represents the lexical token kinds produced by the lexer.<br/>
/// When adding a new TokenKind, remember to do the following:<br/>
/// Add the corresponding descriptor to TokenHelper.TokenDescriptors.<br/>
/// Add the necessary handling to Tokenizer.<br/>
/// Add it to TokenHelper.Separator if necessary.
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
    Shared = 32,
    Public,
    Protected,
    Private,
    Internal,
    ProtectedOrInternal,
    ProtectedAndInternal,
    Let,
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
    As, // as
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
    Separator,
    StartBlock,
    EndBlock,
    NumericLiteral, // 1.23d
    CharLiteral, // 'a'
    StringLiteral, // "text"
    RawStringLiteral, // """text"""
    SingleLineComment, // // comment
    MultiLineComment, // /* comment */
    // LineFeed, // \n or \r\n

    // Move, // <=
    // Map, // =>
    // Reference, // &

    // Single token
    At, // @
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
