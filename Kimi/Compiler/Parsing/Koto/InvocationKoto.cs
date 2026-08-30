// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a function or method invocation expression.
/// </summary>
[TinyhandObject]
public partial class InvocationKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Invocation;

    /// <summary>Gets the invoked expression.</summary>
    [Key(1)]
    public Koto Method { get; private set; }

    /// <summary>Gets the invocation arguments.</summary>
    [Key(2)]
    public List<Koto> Arguments { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="InvocationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="method">The expression being invoked.</param>
    /// <param name="arguments">The invocation arguments.</param>
    public InvocationKoto(ref TokenReader reader, SourceSpan range, Koto method, List<Koto> arguments)
        : base(ref reader, range)
    {
        this.Method = method;
        this.Arguments = arguments;

        method.Parent = this;
        foreach (var x in arguments)
        {
            x.Parent = this;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="InvocationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="method">The expression being invoked.</param>
    /// <param name="arguments">The invocation arguments.</param>
    public InvocationKoto(ref TokenReader reader, Koto method, List<Koto> arguments)
        : this(
            ref reader,
            SourceSpan.FromBounds(
                method.Span.Start,
                Math.Max(method.Span.End, arguments.Count == 0 ? 0 : arguments[^1].Span.End)),
            method,
            arguments)
    {
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Method.Bind(compilation);
        foreach (var argument in this.Arguments)
        {
            argument.Bind(compilation);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Method.WriteTo(ref builder);
        builder.Append(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            this.Arguments[i].WriteTo(ref builder);
            if (i < (this.Arguments.Count - 1))
            {
                builder.Append(Constants.CommaChar);
                builder.Append(Constants.SpaceChar);
            }
        }

        builder.Append(Constants.CloseParenthesisChar);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Method == oldKoto)
        {
            this.Method = newKoto;
        }
        else
        {
            var index = this.Arguments.IndexOf(oldKoto);
            if (index < 0)
            {
                return false;
            }

            this.Arguments[index] = newKoto;
        }

        newKoto.Parent = this;
        oldKoto.Parent = default;
        return true;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Method.RestoreAfterDeserialization(codeContext, this);
        foreach (var argument in this.Arguments)
        {
            argument.RestoreAfterDeserialization(codeContext, this);
        }
    }
}
