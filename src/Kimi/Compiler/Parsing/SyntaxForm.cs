// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// A form of syntax the parser can expect at a position or find where it is not permitted (docs/dev/DIAGNOSTICS.md §4.4).
/// Each value is a requirement of the Syntax partition, <c>Syntax.&lt;Form&gt;</c>, whose phrase and advice come from
/// <c>DiagnosticRequirement.tinyhand</c>; a reporting site chooses a value and never writes text.
/// </summary>
public enum SyntaxForm : ushort
{
    /// <summary>The whole grammar; the requirement of lexical and rule checks that name no form.</summary>
    None,

    /// <summary>An expression, including an operand or an argument.</summary>
    Expression,

    /// <summary>A Type.</summary>
    Type,

    /// <summary>A Name.</summary>
    Name,

    /// <summary>The body of a function, a conditional or a loop.</summary>
    Body,

    /// <summary>The end of the line after a complete statement or header.</summary>
    LineEnd,

    /// <summary>A comma between elements.</summary>
    Comma,

    /// <summary>A colon.</summary>
    Colon,

    /// <summary>The closing parenthesis of an open grouping.</summary>
    CloseParenthesis,

    /// <summary>The closing bracket of an open grouping.</summary>
    CloseBracket,

    /// <summary>The closing brace of an open grouping.</summary>
    CloseBrace,

    /// <summary>The closing angle bracket of an open Type argument list.</summary>
    CloseAngleBracket,

    /// <summary>A closing delimiter with no open grouping; the delimiter itself is underlined.</summary>
    UnmatchedCloser,

    /// <summary>An indented body that no header introduces.</summary>
    IndentedBody,
}
