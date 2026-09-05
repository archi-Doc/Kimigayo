// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Provides the base representation of a declaration node.</summary>
public abstract class DeclarationKoto : Koto
{
    /// <summary>Initializes a new instance of the <see cref="DeclarationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected DeclarationKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DeclarationKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="range">The declaration source span.</param>
    protected DeclarationKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }
}
