// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Describes one arm of a <see cref="MatchKoto"/> expression.</summary>
[TinyhandObject]
public sealed partial class MatchArmKoto
{
    /// <summary>Gets the arm pattern expression.</summary>
    [Key(0)]
    public Koto Pattern { get; internal set; } = default!;

    /// <summary>Gets the arm result expression or block.</summary>
    [Key(1)]
    public Koto Body { get; internal set; } = default!;

    /// <summary>Gets a value indicating whether an inline arm ends with a semicolon.</summary>
    [Key(2)]
    public bool HasTrailingSemicolon { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="MatchArmKoto"/> class.</summary>
    /// <param name="pattern">The arm pattern.</param>
    /// <param name="body">The arm body.</param>
    /// <param name="hasTrailingSemicolon">Whether an inline arm has a trailing semicolon.</param>
    public MatchArmKoto(Koto pattern, Koto body, bool hasTrailingSemicolon = false)
    {
        this.Pattern = pattern;
        this.Body = body;
        this.HasTrailingSemicolon = hasTrailingSemicolon;
    }
}

/// <summary>Represents a <c>match</c> expression.</summary>
[TinyhandObject]
public sealed partial class MatchKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Match;

    /// <summary>Gets the expression being matched.</summary>
    [Key(1)]
    public Koto Expression { get; private set; }

    [Key(2)]
    private List<MatchArmKoto> arms;

    /// <summary>Gets the match arms.</summary>
    [IgnoreMember]
    public IReadOnlyList<MatchArmKoto> Arms => this.arms;

    /// <summary>Initializes a new instance of the <see cref="MatchKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The expression being matched.</param>
    /// <param name="arms">The parsed match arms.</param>
    public MatchKoto(ref TokenReader reader, SourceSpan range, Koto expression, List<MatchArmKoto> arms)
        : base(ref reader, range)
    {
        this.Expression = expression;
        this.arms = arms;

        expression.Parent = this;
        foreach (var arm in arms)
        {
            arm.Pattern.Parent = this;
            arm.Body.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MatchKeyword);
        builder.AppendSpace();
        this.Expression.WriteTo(ref builder);
        builder.AppendLine();
        builder.IncrementIndent();
        for (var i = 0; i < this.arms.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            var arm = this.arms[i];
            arm.Pattern.WriteTo(ref builder);
            builder.Append(" =>");
            if (arm.Body is CodeBlockKoto block)
            {
                block.WriteIndentedTo(ref builder);
            }
            else
            {
                builder.AppendSpace();
                if (arm.HasTrailingSemicolon && ParenthesizedKoto.NeedsMultilineGrouping(arm.Body))
                {
                    ParenthesizedKoto.WriteGroupedTo(arm.Body, ref builder);
                }
                else
                {
                    arm.Body.WriteTo(ref builder);
                }

                if (arm.HasTrailingSemicolon)
                {
                    builder.Append(';');
                }
            }
        }

        builder.DecrementIndent();
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Expression;
        foreach (var arm in this.arms)
        {
            yield return arm.Pattern;
            yield return arm.Body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression == oldKoto)
        {
            this.Expression = newKoto;
            return true;
        }

        foreach (var arm in this.arms)
        {
            if (arm.Pattern == oldKoto)
            {
                arm.Pattern = newKoto;
                return true;
            }

            if (arm.Body == oldKoto)
            {
                arm.Body = newKoto;
                return true;
            }
        }

        return false;
    }
}
