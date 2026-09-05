// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Shares target and argument storage for invocations and generic applications.</summary>
public abstract class ApplicationKoto : ExpressionKoto
{
    /// <summary>Gets the mutable arguments, materializing a list only when requested.</summary>
    public List<Koto> Arguments
    {
        get
        {
            if (this.ArgumentStorage is List<Koto> list)
            {
                return list;
            }

            var result = this.ArgumentStorage is null ? [] : new List<Koto>(this.ArgumentStorage);
            this.ArgumentStorage = result;
            return result;
        }
    }

    /// <summary>Gets the arguments without materializing a mutable list.</summary>
    public IReadOnlyList<Koto> ArgumentNodes => this.ArgumentStorage ?? [];

    /// <summary>Gets the expression to which the arguments apply.</summary>
    protected Koto Target { get; private set; }

    /// <summary>Gets or sets the compact argument storage.</summary>
    protected IReadOnlyList<Koto>? ArgumentStorage { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ApplicationKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="target">The target expression.</param>
    /// <param name="arguments">The arguments, or null for an empty list.</param>
    protected ApplicationKoto(ref TokenReader reader, SourceSpan range, Koto target, IReadOnlyList<Koto>? arguments)
        : base(ref reader, range)
    {
        this.Target = target;
        this.ArgumentStorage = arguments;
        this.Adopt(target);
        this.Adopt(arguments);
    }

    /// <summary>Writes a comma-separated argument list with its delimiters.</summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="open">The opening delimiter.</param>
    /// <param name="close">The closing delimiter.</param>
    /// <param name="labels">Optional argument labels.</param>
    protected void WriteArgumentsTo(ref IndentedStringBuilder builder, char open, char close, string?[]? labels = null)
    {
        builder.Append(open);
        if (this.ArgumentStorage is { } arguments)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                if (labels?[i] is { } label)
                {
                    builder.Append(label);
                    builder.Append(Constants.ColonChar);
                    builder.AppendSpace();
                }

                arguments[i].WriteTo(ref builder);
            }
        }

        builder.Append(close);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Target;
        if (this.ArgumentStorage is { } arguments)
        {
            foreach (var argument in arguments)
            {
                yield return argument;
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Target == oldKoto)
        {
            this.Target = newKoto;
            return true;
        }

        return ReplaceInList(this.ArgumentStorage, oldKoto, newKoto);
    }
}
