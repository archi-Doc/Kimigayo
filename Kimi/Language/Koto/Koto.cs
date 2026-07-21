// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimigayo.Diagnostics;

#pragma warning disable SA1401 // Fields should be private

namespace Kimigayo.Language;

public enum KotoKind : byte
{
    Invalid,

    Unresolved,
    Attribute,
    Alias,

    // Group
    Group,
    Struct,
    Enum,
    Extension,
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

[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]
[TinyhandUnion((int)KotoKind.Struct, typeof(StructKoto))]

[TinyhandUnion((int)KotoKind.StringLiteral, typeof(StringLiteralKoto))]
[TinyhandUnion((int)KotoKind.NumericLiteral, typeof(NumericLiteralKoto))]
[TinyhandUnion((int)KotoKind.BoolLiteral, typeof(BoolLiteralKoto))]
public abstract partial class Koto
{
    #region FieldAndProperty

    // Frontend Metadata
    [IgnoreMember]
    public DiagnosticCollection? DiagnosticCollection { get; internal set; }

    [IgnoreMember]
    public SourceRange Range { get; internal set; }

    [IgnoreMember]
    public CodeContext CodeContext { get; internal set; }

    // Backend Metadata

    // Koto Structure
    [IgnoreMember]
    public Koto? Parent { get; internal set; }

    [IgnoreMember]
    public Koto? Previous { get; internal set; }

    [IgnoreMember]
    public Koto? Next { get; internal set; }

    [Key(0)]
    public AttributeKoto? AttributeChain { get; internal set; }

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => this.Parent is null;

    public Kotonoha Kotonoha => this.CodeContext.Kotonoha;

    #endregion

    public Koto(ref TokenReader reader, SourceRange range)
    {
        this.DiagnosticCollection = reader.Diagnostic;
        this.Range = range;
        this.CodeContext = reader.CodeContext;

        this.AttributeChain = reader.PopAttribute();
        // this.Parent = parent;
    }

    internal Koto(CodeContext codeContext)
    {
        this.CodeContext = codeContext;
    }

    public virtual void Unparse(StringWriter writer)
    {
        if (this.AttributeChain is not null)
        {
            UnparseAttribute(this.AttributeChain, writer);

            if (this is UnaryKoto)
            {
            }
            else
            {
                writer.WriteLine();
            }
        }

        writer.WriteLine(this.ToString());

        static void UnparseAttribute(AttributeKoto a0, StringWriter writer)
        {
            var a1 = a0.AttributeChain;
            if (a1 is null)
            {
                writer.Write(a0.ToString());
                writer.Write(' ');
                return;
            }

            var a2 = a1.AttributeChain;
            if (a2 is null)
            {
                writer.Write(a1.ToString());
                writer.Write(' ');
                writer.Write(a0.ToString());
                writer.Write(' ');
                return;
            }

            var a3 = a2.AttributeChain;
            if (a3 is null)
            {
                writer.Write(a2.ToString());
                writer.Write(' ');
                writer.Write(a1.ToString());
                writer.Write(' ');
                writer.Write(a0.ToString());
                writer.Write(' ');
                return;
            }

            var a4 = a3.AttributeChain;
            if (a4 is null)
            {
                writer.Write(a3.ToString());
                writer.Write(' ');
                writer.Write(a2.ToString());
                writer.Write(' ');
                writer.Write(a1.ToString());
                writer.Write(' ');
                writer.Write(a0.ToString());
                writer.Write(' ');
                return;
            }

            var list = new List<Koto>();
            var x = a0;
            while (x is not null)
            {
                list.Add(x);
                x = x.AttributeChain;
            }

            list.Reverse();
            foreach (var y in list)
            {
                writer.Write(y.ToString());
                writer.Write(' ');
            }
        }
    }

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
        this.DiagnosticCollection?.Add(this.Range, diagnosticHash, obj);
    }

    internal virtual bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        return false;
    }
}
