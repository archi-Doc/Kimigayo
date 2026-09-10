// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents one arm of an invalid compile-time Case Group.</summary>
public sealed class CompileTimeCaseArmKoto
{
    /// <summary>Gets the arm condition, or <see langword="null"/> for <c>#case _</c>.</summary>
    public Koto? Condition { get; private set; }

    /// <summary>Gets the syntax controlled by the arm.</summary>
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="CompileTimeCaseArmKoto"/> class.</summary>
    /// <param name="condition">The condition, or <see langword="null"/> for the fallback arm.</param>
    /// <param name="body">The controlled body.</param>
    public CompileTimeCaseArmKoto(Koto? condition, CodeBlockKoto body)
    {
        this.Condition = condition;
        this.Body = body;
    }

    internal bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
            return true;
        }

        if (this.Body == oldKoto && newKoto is CodeBlockKoto body)
        {
            this.Body = body;
            return true;
        }

        return false;
    }
}

/// <summary>Stores a compile-time <c>#match</c> retained for error recovery after failed validation or selection.</summary>
public sealed class CompileTimeMatchKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CompileTimeMatch;

    private List<CompileTimeCaseArmKoto> arms;

    /// <summary>Gets the arms in source order.</summary>
    public IReadOnlyList<CompileTimeCaseArmKoto> Arms => this.arms;

    /// <summary>Initializes a new instance of the <see cref="CompileTimeMatchKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete group span.</param>
    /// <param name="arms">The parsed arms.</param>
    public CompileTimeMatchKoto(
        ref TokenReader reader,
        SourceSpan range,
        List<CompileTimeCaseArmKoto> arms)
        : base(ref reader, range)
    {
        this.arms = arms;
        foreach (var arm in arms)
        {
            this.Adopt(arm.Condition);
            this.Adopt(arm.Body);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("#match");
        builder.AppendLine();
        builder.IncrementIndent();
        for (var i = 0; i < this.arms.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append('#');
            builder.Append(Constants.CaseKeyword);
            builder.AppendSpace();
            if (this.arms[i].Condition is { } condition)
            {
                condition.WriteTo(ref builder);
            }
            else
            {
                builder.Append('_');
            }

            this.arms[i].Body.WriteIndentedTo(ref builder);
        }

        builder.DecrementIndent();
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var arm in this.arms)
        {
            if (arm.Condition is not null)
            {
                yield return arm.Condition;
            }

            yield return arm.Body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        foreach (var arm in this.arms)
        {
            if (arm.ReplaceChild(oldKoto, newKoto))
            {
                return true;
            }
        }

        return false;
    }
}
