// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimigayo.Diagnostics;

#pragma warning disable SA1401 // Fields should be private

namespace Kimigayo.Language;

public enum KotoKind : byte
{
    Unresolved,
    Attribute,
    Alias,

    // Group
    Namespace,
    Group,
    Struct,
    Contract,

    // Literal
    NumericLiteral,
    StringLiteral,
    BoolLiteral,
    I8Literal,
}

[TinyhandObject(ReservedKeyCount = 1)]
[TinyhandUnion((int)KotoKind.Unresolved, typeof(UnresolvedKoto))]
[TinyhandUnion((int)KotoKind.Alias, typeof(AliasKoto))]
[TinyhandUnion((int)KotoKind.Attribute, typeof(AttributeKoto))]

[TinyhandUnion((int)KotoKind.Namespace, typeof(NamespaceKoto))]
[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]

[TinyhandUnion((int)KotoKind.StringLiteral, typeof(StringLiteralKoto))]
[TinyhandUnion((int)KotoKind.NumericLiteral, typeof(NumericLiteralKoto))]
[TinyhandUnion((int)KotoKind.BoolLiteral, typeof(BoolLiteralKoto))]
public abstract partial class Koto
{
    [IgnoreMember]
    public FrontendMetadata? FrontendMetadata { get; internal set; }

    [IgnoreMember]
    public Koto? Parent { get; internal set; }

    [IgnoreMember]
    public AttributeKoto? AttributeChain { get; internal set; }

    [Key(0)]
    public ulong KotoId { get; internal set; }
    // public string? Description { get; private set; }

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => this.Parent is null;

    public Koto(ref TokenReader reader, SourceRange range)
    {
        this.FrontendMetadata = new(reader.Diagnostic, range, reader.CodeContext);
        this.AttributeChain = reader.PopAttribute();
        // this.Parent = parent;
    }

    internal Koto(FrontendMetadata compilationMetadata)
    {
        this.FrontendMetadata = compilationMetadata;
        // this.Parent = parent;
    }

    /*public virtual void Parse(ref TokenReader reader)
    {
    }*/

    public virtual (string Text, Koto[]? Children) Dump()
    {
        return (string.Empty, default);
    }

    public virtual Koto? ResolveIdentifier(ReadOnlySpan<char> identifier)
    {
        return default;
    }

    public void AddDiagnostic(ulong diagnosticHash, object? obj = null)
    {
        if (this.FrontendMetadata is not null)
        {
            this.FrontendMetadata.DiagnosticCollection.Add(this.FrontendMetadata.Range, diagnosticHash, obj);
        }
    }

    internal virtual bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        return false;
    }
}
