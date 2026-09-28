// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // Relation syntax and its allocation-free attachment helpers.

/// <summary>A declaration-attached Origin equality or outlives requirement.</summary>
public sealed class OriginRelationKoto : BinaryKoto
{
    internal OriginRelationKoto(ref TokenReader reader, SourceSpan span, Koto left, Koto right, bool equality)
        : base(ref reader, span, left, right)
    {
        this.IsEquality = equality;
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.OriginRelation;

    /// <summary>Gets a value indicating whether the relation requires equality rather than outlives.</summary>
    public bool IsEquality { get; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("origin ");
        this.Left.WriteTo(ref builder);
        builder.Append(this.IsEquality ? " == " : " outlives ");
        this.Right.WriteTo(ref builder);
    }
}

internal interface IOriginClauseOwner
{
    List<OriginRelationKoto>? OriginClauses { get; set; }
}

internal static class OriginClauses
{
    internal static IReadOnlyList<OriginRelationKoto> Get(Koto owner)
        => owner is IOriginClauseOwner { OriginClauses: { } clauses } ? clauses : Array.Empty<OriginRelationKoto>();

    internal static void Add(Koto owner, OriginRelationKoto clause)
    {
        if (owner is not IOriginClauseOwner target)
        {
            throw new InvalidOperationException("This syntax cannot own Origin clauses.");
        }

        (target.OriginClauses ??= new(2)).Add(clause);
        clause.Parent = owner;
    }

    internal static void Write(Koto owner, ref IndentedStringBuilder builder, bool indent = true)
    {
        var clauses = Get(owner);
        if (clauses.Count == 0)
        {
            return;
        }

        if (indent)
        {
            builder.AppendLine();
            builder.IncrementIndent();
        }

        for (var i = 0; i < clauses.Count; i++)
        {
            clauses[i].WriteTo(ref builder);
            if (!indent || i + 1 < clauses.Count)
            {
                builder.AppendLine();
            }
        }

        if (indent)
        {
            builder.DecrementIndent();
        }
    }
}
