// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Lexing;

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
    String,

    // Keywords
    True = 32,
    False,
    Let,
    Var,
    Func,

    // Expression keyword
    If, // if
    Else, // else
    // Block, // block
    As, // as
    Is, // is
    Not, // not
    And, // and
    Or, // Or
    For, // for
    While, // while
    Loop, // loop
    Match, // match
    Return, // method/return
    Break, // loop/break
    Continue, // continue
    Yield, // yield

    // Contextual keyword
    Alias = 96,
    RootGroup,
    Group,
    Struct,
    Enum,
    Extension,
    Contract,
    Static,
    Public,
    Protected,
    Private,
    Internal,
    ProtectedOrInternal,
    ProtectedAndInternal,
    Open,
    In, // in; contextual delimiter in a for expression
    Associate,

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
    MinusGreaterThan, // ->
    MinusMinus, // --
    Percent, // %
    PercentEquals, // %=
    Plus, // +
    PlusEquals, // +=
    PlusPlus, // ++
    Slash, // /
    SlashEquals, // /=
}
