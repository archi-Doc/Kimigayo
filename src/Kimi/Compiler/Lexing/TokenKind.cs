// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Represents the lexical token kinds produced by the lexer.<br/>
/// The ranges follow the keyword classes of SPEC 2.5.1: every kind below <see cref="Alias"/> is a reserved word, which is never a
/// Name; the kinds from <see cref="Alias"/> through <see cref="Identifier"/> can be Names (<see cref="TokenHelper.IsIdentifierOrContextualKeyword"/>).<br/>
/// When adding a new TokenKind, remember to do the following:<br/>
/// Add the spelling to TokenHelper (the text table and, for keywords, GetKeywordOrIdentifierKind).<br/>
/// Add the necessary handling to Tokenizer.<br/>
/// Add it to TokenHelper.Separators if necessary.
/// </summary>
public enum TokenKind : byte
{
    Invalid,

    // Reserved words: primitive types
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
    Char,
    String,

    // Reserved words
    True = 32,
    False,
    Let,
    Var,
    Func,

    // Access and inheritance (reserved)
    Public,
    Protected,
    Private,
    Internal,
    Open,

    // Reserved words of expressions and statements
    If, // if
    Else, // else
    Case, // case
    As, // as
    Is, // is
    Not, // not
    And, // and
    Or, // Or
    For, // for
    While, // while
    Loop, // loop
    Match, // match
    Return, // function/return
    Exit, // loop,for,while/exit
    Continue, // loop,for,while/continue
    Yield, // if/yield
    Null, // contextually typed raw-pointer literal
    Try,
    Underscore,
    Require,
    Defer,
    Self,
    Init,
    Drop,
    Base,
    Do,
    Switch, // compile-time #switch only

    // Contextual keywords: Names outside their contexts
    Alias = 96,
    RootGroup,
    Group,
    Struct,
    Enum,
    Extension,
    Contract,
    Static,
    In, // in; contextual delimiter in a for expression
    Associate,
    Get,
    Set,
    Has,
    Computed,
    Property,

    // Not keyword
    Identifier = 128,
    Separator,
    StartBlock,
    EndBlock,
    NumericLiteral, // 1.23d
    CharLiteral, // 'a'
    StringLiteral, // "text" and """raw text"""; comments produce no token

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

    // An escaped string containing embedded expressions.
    InterpolatedStringLiteral,
    ColonColon, // ::
}
