// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a function or method invocation expression.
/// </summary>
[TinyhandObject]
public sealed partial class InvocationKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Invocation;

    /// <summary>Gets the invoked expression.</summary>
    [Key(1)]
    public Koto Method { get; private set; }

    /// <summary>Gets the invocation arguments.</summary>
    [Key(2)]
    public List<Koto> Arguments { get; private set; }

    // Allocated only when at least one argument is labeled.
    [Key(3)]
    private string?[]? argumentLabels;

    /// <summary>Gets the argument labels in argument order. A positional argument has a null label.</summary>
    [IgnoreMember]
    public IReadOnlyList<string?> ArgumentLabels
        => this.argumentLabels ??= new string?[this.Arguments.Count];

    /// <summary>Initializes a new instance of the <see cref="InvocationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="method">The expression being invoked.</param>
    /// <param name="arguments">The invocation arguments.</param>
    /// <param name="argumentLabels">The argument labels in argument order, or <see langword="null"/> when no argument is labeled.</param>
    public InvocationKoto(
        ref TokenReader reader,
        SourceSpan range,
        Koto method,
        List<Koto> arguments,
        string?[]? argumentLabels = null)
        : base(ref reader, range)
    {
        if (argumentLabels is not null && argumentLabels.Length != arguments.Count)
        {
            throw new ArgumentException("The number of argument labels must match the number of arguments.", nameof(argumentLabels));
        }

        this.Method = method;
        this.Arguments = arguments;
        this.argumentLabels = argumentLabels;

        method.Parent = this;
        this.Adopt(arguments);
    }

    /// <summary>Initializes a new instance of the <see cref="InvocationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="method">The expression being invoked.</param>
    /// <param name="arguments">The invocation arguments.</param>
    /// <param name="argumentLabels">The argument labels in argument order, or <see langword="null"/> when no argument is labeled.</param>
    public InvocationKoto(
        ref TokenReader reader,
        Koto method,
        List<Koto> arguments,
        string?[]? argumentLabels = null)
        : this(
            ref reader,
            SourceSpan.FromBounds(
                method.Span.Start,
                Math.Max(method.Span.End, arguments.Count == 0 ? 0 : arguments[^1].Span.End)),
            method,
            arguments,
            argumentLabels)
    {
    }

    /// <summary>Gets the label of an argument, or <see langword="null"/> for a positional argument.</summary>
    /// <param name="index">The argument index.</param>
    /// <returns>The label, or <see langword="null"/>.</returns>
    public string? GetArgumentLabel(int index)
        => this.argumentLabels?[index];

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Method.WriteTo(ref builder);
        builder.Append(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            if (this.GetArgumentLabel(i) is { } label)
            {
                builder.Append(label);
                builder.Append(Constants.ColonChar);
                builder.AppendSpace();
            }

            this.Arguments[i].WriteTo(ref builder);
        }

        builder.Append(Constants.CloseParenthesisChar);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Method;
        foreach (var argument in this.Arguments)
        {
            yield return argument;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Method == oldKoto)
        {
            this.Method = newKoto;
            return true;
        }

        return ReplaceInList(this.Arguments, oldKoto, newKoto);
    }
}
