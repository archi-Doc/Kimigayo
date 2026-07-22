// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

#pragma warning disable SA1401 // Fields should be private

namespace Kimi.Compiler.Parsing;

public enum KotoKind : byte
{
    Invalid,

    // Group
    Contract,
    Enum,
    Extension,
    Group,
    Struct,

    // Nullary
    Alias,
    BoolLiteral,
    Error,
    Field,
    NumericLiteral,
    StringLiteral,
    Unresolved,

    // Unary
    Attribute,
    Macro,
    Reference,
    Unwrap,
    PrefixCaret,
    PrefixPlus,
    PrefixPlusPlus,
    PrefixMinus,
    PrefixMinusMinus,
    PostfixIncrement,
    PostfixDecrement,
    Not,
    Parenthesized,

    // 

    // Misc
    Invocation,
}

[TinyhandObject(ReservedKeyCount = 1)]
[TinyhandUnion((int)KotoKind.Contract, typeof(ContractKoto))]
[TinyhandUnion((int)KotoKind.Enum, typeof(EnumKoto))]
[TinyhandUnion((int)KotoKind.Extension, typeof(ExtensionKoto))]
[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]
[TinyhandUnion((int)KotoKind.Struct, typeof(StructKoto))]

[TinyhandUnion((int)KotoKind.Alias, typeof(AliasKoto))]
[TinyhandUnion((int)KotoKind.BoolLiteral, typeof(BoolLiteralKoto))]
[TinyhandUnion((int)KotoKind.Error, typeof(ErrorKoto))]
[TinyhandUnion((int)KotoKind.Field, typeof(FieldKoto))]
[TinyhandUnion((int)KotoKind.NumericLiteral, typeof(NumericLiteralKoto))]
[TinyhandUnion((int)KotoKind.StringLiteral, typeof(StringLiteralKoto))]
[TinyhandUnion((int)KotoKind.Unresolved, typeof(UnresolvedKoto))]

[TinyhandUnion((int)KotoKind.Attribute, typeof(AttributeKoto))]
[TinyhandUnion((int)KotoKind.Macro, typeof(MacroKoto))]
[TinyhandUnion((int)KotoKind.Reference, typeof(ReferenceKoto))]
[TinyhandUnion((int)KotoKind.Unwrap, typeof(UnwrapKoto))]
[TinyhandUnion((int)KotoKind.PrefixCaret, typeof(PrefixCaretKoto))]
[TinyhandUnion((int)KotoKind.PrefixPlus, typeof(PrefixPlusKoto))]
[TinyhandUnion((int)KotoKind.PrefixPlusPlus, typeof(PrefixPlusPlusKoto))]
[TinyhandUnion((int)KotoKind.PrefixMinus, typeof(PrefixMinusKoto))]
[TinyhandUnion((int)KotoKind.PrefixMinusMinus, typeof(PrefixMinusMinusKoto))]
[TinyhandUnion((int)KotoKind.PostfixIncrement, typeof(PostfixIncrementKoto))]
[TinyhandUnion((int)KotoKind.PostfixDecrement, typeof(PostfixDecrementKoto))]
[TinyhandUnion((int)KotoKind.Not, typeof(NotKoto))]
[TinyhandUnion((int)KotoKind.Parenthesized, typeof(ParenthesizedKoto))]

[TinyhandUnion((int)KotoKind.Invocation, typeof(InvocationKoto))]
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

    public virtual void WriteTo(StringWriter writer)
    {
    }

    public virtual void UnparseTo(StringWriter writer)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, writer);

            if (this is AliasKoto ||
                this is GroupKoto)
            {
                writer.WriteLine();
            }
        }

        this.WriteTo(writer);
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
