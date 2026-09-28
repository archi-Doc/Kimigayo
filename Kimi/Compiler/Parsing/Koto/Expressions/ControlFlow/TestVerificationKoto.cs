// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A standalone test verification; the message is evaluated only on failure.</summary>
public sealed class TestVerificationKoto : Koto
{
    internal TestVerificationKoto(ref TokenReader reader, SourceSpan span, bool require, Koto condition, Koto? message)
        : base(ref reader, span)
    {
        this.IsRequire = require;
        this.Condition = condition;
        this.Message = message;
        this.Adopt(condition);
        if (message is not null)
        {
            this.Adopt(message);
        }
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TestVerification;

    /// <summary>Gets a value indicating whether failure terminates the case after temporary cleanup.</summary>
    public bool IsRequire { get; }

    /// <summary>Gets the Boolean condition.</summary>
    public Koto Condition { get; private set; }

    /// <summary>Gets the optional lazy message.</summary>
    public Koto? Message { get; private set; }

    internal int SiteId { get; set; } = -1;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.IsRequire ? "$require(" : "$expect(");
        this.Condition.WriteTo(ref builder);
        if (this.Message is { } message)
        {
            builder.Append(", message: ");
            message.WriteTo(ref builder);
        }

        builder.Append(")");
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Condition);
        if (this.Message is { } message)
        {
            visitor.Visit(message);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Condition;
        if (this.Message is { } message)
        {
            yield return message;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (ReferenceEquals(this.Condition, oldKoto))
        {
            this.Condition = newKoto;
        }
        else if (ReferenceEquals(this.Message, oldKoto))
        {
            this.Message = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}
