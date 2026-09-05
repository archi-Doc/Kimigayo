// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a function or method invocation expression.
/// </summary>
public sealed class InvocationKoto : ApplicationKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Invocation;

    /// <summary>Gets the invoked expression.</summary>
    public Koto Method => this.Target;

    // Allocated only when at least one argument is labeled.
    private string?[]? argumentLabels;

    /// <summary>Gets the argument labels in argument order. A positional argument has a null label.</summary>
    public IReadOnlyList<string?> ArgumentLabels
        => this.argumentLabels ??= new string?[this.ArgumentNodes.Count];

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
        IReadOnlyList<Koto>? arguments,
        string?[]? argumentLabels = null)
        : base(ref reader, range, method, arguments)
    {
        if (argumentLabels is not null && argumentLabels.Length != (arguments?.Count ?? 0))
        {
            throw new ArgumentException("The number of argument labels must match the number of arguments.", nameof(argumentLabels));
        }

        this.argumentLabels = argumentLabels;
    }

    /// <summary>Initializes a new instance of the <see cref="InvocationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="method">The expression being invoked.</param>
    /// <param name="arguments">The invocation arguments.</param>
    /// <param name="argumentLabels">The argument labels in argument order, or <see langword="null"/> when no argument is labeled.</param>
    public InvocationKoto(
        ref TokenReader reader,
        Koto method,
        IReadOnlyList<Koto>? arguments,
        string?[]? argumentLabels = null)
        : this(
            ref reader,
            SourceSpan.FromBounds(
                method.Span.Start,
                Math.Max(method.Span.End, arguments is not { Count: > 0 } ? 0 : arguments[^1].Span.End)),
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
        this.WriteArgumentsTo(ref builder, Constants.OpenParenthesisChar, Constants.CloseParenthesisChar, this.argumentLabels);
    }
}
