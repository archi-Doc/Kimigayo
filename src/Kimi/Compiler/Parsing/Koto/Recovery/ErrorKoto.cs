// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an invalid expression created during parser recovery.
/// </summary>
public sealed class ErrorKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Error;

    /// <summary>Gets or sets the key of the syntax error this recovery stands for; a check that needs the missing syntax names it as its prerequisite.</summary>
    internal DiagnosticKey? Cause { get; set; }

    /// <summary>Gets a value indicating whether this mark stands for a delimited construct the parser read without nodes past the syntax
    /// nesting limit (DECISIONS BU-112): it lies under a cut of <see cref="SyntaxNesting"/>, the one writer of its record.</summary>
    internal bool IsSkip { get; init; }

    /// <summary>Initializes a new instance of the <see cref="ErrorKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The invalid source span.</param>
    public ErrorKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ErrorKoto"/> class that a walk after parsing makes, such as a cut of
    /// <see cref="SyntaxNesting"/>.</summary>
    /// <param name="codeContext">The source context.</param>
    /// <param name="range">The invalid source span.</param>
    internal ErrorKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => builder.Append("Error");
}
