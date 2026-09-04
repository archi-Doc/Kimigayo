// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // File may only contain a single type

/// <summary>
/// Provides the shared representation of the Never-valued control-transfer expressions
/// <c>return</c>, <c>break</c>, <c>continue</c>, and <c>yield</c>.
/// </summary>
[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class JumpKoto : ExpressionKoto
{
    /// <summary>Gets the transferred value, if present.</summary>
    [Key(1)]
    public Koto? Expression { get; private set; }

    /// <summary>Gets the source keyword for this expression.</summary>
    public string Keyword => this.Akind switch
    {
        KotoKind.Return => Constants.ReturnKeyword,
        KotoKind.Break => Constants.BreakKeyword,
        KotoKind.Continue => Constants.ContinueKeyword,
        _ => Constants.YieldKeyword,
    };

    /// <summary>Initializes a new instance of the <see cref="JumpKoto"/> class for deserialization.</summary>
    /// <param name="codeContext">The owning code context.</param>
    internal JumpKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JumpKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The transferred value, if present.</param>
    protected JumpKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range)
    {
        this.Expression = expression;
        this.Adopt(expression);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Keyword);
        if (this.Expression is not null)
        {
            builder.AppendSpace();
            this.Expression.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.Expression is null ? [] : [this.Expression];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression != oldKoto)
        {
            return false;
        }

        this.Expression = newKoto;
        return true;
    }
}

/// <summary>Represents a Never-valued <c>return</c> expression.</summary>
[TinyhandObject]
public sealed partial class ReturnKoto : JumpKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Return;

    /// <summary>Initializes a new instance of the <see cref="ReturnKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The returned expression, if present.</param>
    public ReturnKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range, expression)
    {
    }
}

/// <summary>Represents a Never-valued <c>break</c> expression.</summary>
[TinyhandObject]
public sealed partial class BreakKoto : JumpKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Break;

    /// <summary>Initializes a new instance of the <see cref="BreakKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The break value, if present.</param>
    public BreakKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range, expression)
    {
    }
}

/// <summary>Represents a Never-valued <c>continue</c> expression.</summary>
[TinyhandObject]
public sealed partial class ContinueKoto : JumpKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Continue;

    /// <summary>Initializes a new instance of the <see cref="ContinueKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The keyword span.</param>
    public ContinueKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range, default)
    {
    }
}

/// <summary>Represents a Never-valued <c>yield</c> expression that supplies a value to the enclosing value-producing construct.</summary>
[TinyhandObject]
public sealed partial class YieldKoto : JumpKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Yield;

    /// <summary>Gets the value supplied to the target value-producing construct.</summary>
    public new Koto Expression => base.Expression!;

    /// <summary>Initializes a new instance of the <see cref="YieldKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The value supplied to the target construct.</param>
    public YieldKoto(ref TokenReader reader, SourceSpan range, Koto expression)
        : base(ref reader, range, expression)
    {
    }
}
