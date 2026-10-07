// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Checking;

namespace Kimi.Lsp;

#pragma warning disable SA1201, SA1202, SA1204, SA1600 // Internal bounded comparison of detached immutable facts.

internal sealed class HoverAgreement
{
    private readonly HoverBudget budget = new();
    private readonly HashSet<(HoverKey Left, HoverKey Right)> keys = new(KeyPairs.Instance);

    internal bool Equal(HoverEntry left, HoverEntry right)
        => left.Span == right.Span && this.Info(left.Info, right.Info);

    private bool Info(HoverInfo left, HoverInfo right)
    {
        this.budget.Charge();
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Copy != right.Copy || !this.Text(left.Use, right.Use) || !this.Text(left.CopyType, right.CopyType) ||
            !this.Text(left.Effects, right.Effects) || !this.Key(left.TypeIdentity, right.TypeIdentity) ||
            left.Declarations.Length != right.Declarations.Length)
        {
            return false;
        }

        if (!ReferenceEquals(left.Variable, right.Variable) &&
            (left.Variable is not { } a || right.Variable is not { } b ||
            !this.Declaration(a.Declaration, b.Declaration) || !this.Text(a.Semantics, b.Semantics) ||
            !this.Text(a.Referent, b.Referent) || !this.Text(a.Target, b.Target)))
        {
            return false;
        }

        for (var i = 0; i < left.Declarations.Length; i++)
        {
            if (!this.Declaration(left.Declarations[i], right.Declarations[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool Declaration(HoverDeclaration left, HoverDeclaration right)
    {
        this.budget.Charge();
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!this.Text(left.Kind, right.Kind) || !this.Text(left.Owner, right.Owner) || !this.Text(left.Header, right.Header) ||
            !this.Text(left.Details, right.Details) || !this.Text(left.Parameter, right.Parameter) ||
            !this.Text(left.DocumentationNotice, right.DocumentationNotice) || left.ImplementationNote != right.ImplementationNote ||
            !this.Key(left.Identity, right.Identity) || left.Origins.Length != right.Origins.Length ||
            left.Documentation.Length != right.Documentation.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Origins.Length; i++)
        {
            var a = left.Origins[i];
            var b = right.Origins[i];
            if (a.Name != b.Name || !this.Text(a.Project, b.Project) || !this.Text(a.Source, b.Source) || !this.Text(a.LogicalName, b.LogicalName))
            {
                return false;
            }
        }

        for (var i = 0; i < left.Documentation.Length; i++)
        {
            if (!this.Documentation(left.Documentation[i], right.Documentation[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool Documentation(HoverDocumentation left, HoverDocumentation right)
    {
        this.budget.Charge();
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Span != right.Span || left.Indent != right.Indent || left.AdditionOrder != right.AdditionOrder ||
            left.DeclarationSpan != right.DeclarationSpan || !this.Text(left.Project, right.Project) ||
            !this.Text(left.Source.Path, right.Source.Path) || !this.Text(left.LogicalName, right.LogicalName) ||
            !this.Text(left.ModId, right.ModId) || left.Parameters.Length != right.Parameters.Length ||
            !this.Placement(left.Placement, right.Placement))
        {
            return false;
        }

        if (!ReferenceEquals(left.Source, right.Source))
        {
            this.budget.Charge(left.Span.Length);
            if (!left.Source.AsSpan().Slice(left.Span.Start, left.Span.Length).SequenceEqual(right.Source.AsSpan().Slice(right.Span.Start, right.Span.Length)))
            {
                return false;
            }
        }

        foreach (var parameter in left.Parameters.AsSpan())
        {
            this.budget.Charge(parameter.Name.Length + 1);
        }

        return left.Parameters.AsSpan().SequenceEqual(right.Parameters);
    }

    private bool Placement(HoverPlacement? left, HoverPlacement? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || !this.Text(left.Root, right.Root) || left.Files.Length != right.Files.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Files.Length; i++)
        {
            if (!this.Text(left.Files[i].Key, right.Files[i].Key) || !this.Text(left.Files[i].Value, right.Files[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private bool Key(HoverKey? left, HoverKey? right)
    {
        using var depth = this.budget.Enter();
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || !this.Text(left.Value, right.Value) || left.Parts.Length != right.Parts.Length)
        {
            return false;
        }

        if (!this.keys.Add((left, right)))
        {
            return true;
        }

        for (var i = 0; i < left.Parts.Length; i++)
        {
            if (!this.Key(left.Parts[i], right.Parts[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool Text(string? left, string? right)
    {
        this.budget.Charge();
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        this.budget.Charge(left.Length);
        return left == right;
    }

    // HoverKey's generated record equality recursively hashes arrays; comparison memoization needs only object identity.
    private sealed class KeyPairs : IEqualityComparer<(HoverKey Left, HoverKey Right)>
    {
        internal static readonly KeyPairs Instance = new();

        public bool Equals((HoverKey Left, HoverKey Right) x, (HoverKey Left, HoverKey Right) y)
            => ReferenceEquals(x.Left, y.Left) && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode((HoverKey Left, HoverKey Right) pair)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(pair.Left), RuntimeHelpers.GetHashCode(pair.Right));
    }
}
