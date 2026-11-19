// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Describes one conditional branch of an <see cref="IfKoto"/>.</summary>
public sealed class ConditionalBranchKoto
{
    /// <summary>Gets the branch condition.</summary>
    public Koto Condition { get; internal set; } = default!;

    /// <summary>Gets the branch body.</summary>
    public CodeBlockKoto Body { get; internal set; } = default!;

    /// <summary>Initializes a new instance of the <see cref="ConditionalBranchKoto"/> class.</summary>
    /// <param name="condition">The branch condition.</param>
    /// <param name="body">The branch body.</param>
    public ConditionalBranchKoto(Koto condition, CodeBlockKoto body)
    {
        this.Condition = condition;
        this.Body = body;
    }
}

/// <summary>Represents an <c>if</c> expression.</summary>
public sealed class IfKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.If;

    private List<ConditionalBranchKoto> branches;

    /// <summary>Gets the conditional branches.</summary>
    public IReadOnlyList<ConditionalBranchKoto> Branches => this.branches;

    /// <summary>Gets the final else body, if present.</summary>
    public CodeBlockKoto? ElseBody { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="IfKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="branches">The conditional branches.</param>
    /// <param name="elseBody">The final else body, if present.</param>
    public IfKoto(ref TokenReader reader, SourceSpan range, List<ConditionalBranchKoto> branches, CodeBlockKoto? elseBody)
        : base(ref reader, range)
    {
        this.branches = branches;
        this.ElseBody = elseBody;

        for (var i = 0; i < branches.Count; i++)
        {
            branches[i].Condition.Parent = this;
            this.Adopt(branches[i].Body, SelectionRole.Body, i);
        }

        if (elseBody is not null)
        {
            this.Adopt(elseBody, SelectionRole.Body, branches.Count);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        for (var i = 0; i < this.branches.Count; i++)
        {
            if (i == 0)
            {
                builder.Append(TokenKind.If.ToText());
            }
            else
            {
                builder.AppendLine();
                builder.Append(TokenKind.Else.ToText());
                builder.AppendSpace();
                builder.Append(TokenKind.If.ToText());
            }

            builder.AppendSpace();
            this.branches[i].Condition.WriteTo(ref builder);
            this.branches[i].Body.WriteBranchTo(ref builder);
        }

        if (this.ElseBody is not null)
        {
            builder.AppendLine();
            builder.Append(TokenKind.Else.ToText());
            this.ElseBody.WriteBranchTo(ref builder);
        }
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        foreach (var branch in this.branches)
        {
            branch.Condition = slots.Slot(branch.Condition);
            branch.Body = slots.Slot(branch.Body);
        }

        this.ElseBody = slots.Slot(this.ElseBody);
    }
}
