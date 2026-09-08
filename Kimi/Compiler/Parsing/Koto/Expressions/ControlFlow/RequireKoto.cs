// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Retains a require condition and its failure statement.</summary>
public sealed class RequireKoto : Koto
{
    internal RequireKoto(ref TokenReader reader, SourceSpan span, Koto condition, Koto body)
        : base(ref reader, span)
    {
        this.Condition = condition;
        this.ElseBody = body;
        this.Adopt(condition);
        this.Adopt(body);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Require;

    /// <summary>Gets the condition.</summary>
    public Koto Condition { get; private set; }

    /// <summary>Gets the failure statement or block.</summary>
    public Koto ElseBody { get; private set; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("require ");
        this.Condition.WriteTo(ref builder);
        builder.Append(" else");
        if (this.ElseBody is CodeBlockKoto block)
        {
            block.WriteIndentedTo(ref builder);
        }
        else
        {
            builder.AppendSpace();
            this.ElseBody.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Condition;
        yield return this.ElseBody;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
        }
        else if (this.ElseBody == oldKoto)
        {
            this.ElseBody = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}
