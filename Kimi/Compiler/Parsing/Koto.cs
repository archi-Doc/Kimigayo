// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

#pragma warning disable SA1401 // Fields should be private

namespace Kimi.Compiler.Parsing;

[Flags]
public enum KotoWriteOptions : byte
{
    /// <summary>
    /// Uses the default output format and appends nothing.
    /// </summary>
    None = 0,

    /// <summary>
    /// Appends a single space after the output.
    /// </summary>
    AppendSpace = 1 << 0,

    /// <summary>
    /// Appends a line feed after the output.
    /// </summary>
    AppendLineFeed = 1 << 1,

    /// <summary>
    /// Writes names in fully qualified form.
    /// </summary>
    FullyQualified = 1 << 2,
}

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
    NumberLiteral,
    StringLiteral,
    IdentifierName,
    Type,

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

    // Binary
    MemberAccess,
    Index,
    Asterisk,
    Slash,
    Percent,
    Plus,
    Minus,
    LessThanLessThan,
    GreaterThanGreaterThan,
    LessThan,
    LessThanEquals,
    GreaterThan,
    GreaterThanEquals,
    As,
    Is,
    EqualsEquals,
    ExclamationEquals,
    Ampersand,
    Caret,
    Bar,
    And,
    Or,
    Equals,
    PlusEquals,
    MinusEquals,
    AsteriskEquals,
    SlashEquals,
    PercentEquals,
    AmpersandEquals,
    CaretEquals,
    BarEquals,
    LessThanLessThanEquals,
    GreaterThanGreaterThanEquals,

    // Misc
    Invocation,
    Generics,

    Omega,
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
[TinyhandUnion((int)KotoKind.NumberLiteral, typeof(NumberLiteralKoto))]
[TinyhandUnion((int)KotoKind.StringLiteral, typeof(StringLiteralKoto))]
[TinyhandUnion((int)KotoKind.IdentifierName, typeof(IdentifierNameKoto))]
[TinyhandUnion((int)KotoKind.Type, typeof(TypeKoto))]

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

[TinyhandUnion((int)KotoKind.MemberAccess, typeof(MemberAccessKoto))]
[TinyhandUnion((int)KotoKind.Index, typeof(IndexKoto))]
[TinyhandUnion((int)KotoKind.Asterisk, typeof(AsteriskKoto))]
[TinyhandUnion((int)KotoKind.Slash, typeof(SlashKoto))]
[TinyhandUnion((int)KotoKind.Percent, typeof(PercentKoto))]
[TinyhandUnion((int)KotoKind.Plus, typeof(PlusKoto))]
[TinyhandUnion((int)KotoKind.Minus, typeof(MinusKoto))]
[TinyhandUnion((int)KotoKind.LessThanLessThan, typeof(LessThanLessThanKoto))]
[TinyhandUnion((int)KotoKind.GreaterThanGreaterThan, typeof(GreaterThanGreaterThanKoto))]
[TinyhandUnion((int)KotoKind.LessThan, typeof(LessThanKoto))]
[TinyhandUnion((int)KotoKind.LessThanEquals, typeof(LessThanEqualsKoto))]
[TinyhandUnion((int)KotoKind.GreaterThan, typeof(GreaterThanKoto))]
[TinyhandUnion((int)KotoKind.GreaterThanEquals, typeof(GreaterThanEqualsKoto))]
[TinyhandUnion((int)KotoKind.As, typeof(AsKoto))]
[TinyhandUnion((int)KotoKind.Is, typeof(IsKoto))]
[TinyhandUnion((int)KotoKind.EqualsEquals, typeof(EqualsEqualsKoto))]
[TinyhandUnion((int)KotoKind.ExclamationEquals, typeof(ExclamationEqualsKoto))]
[TinyhandUnion((int)KotoKind.Ampersand, typeof(AmpersandKoto))]
[TinyhandUnion((int)KotoKind.Caret, typeof(CaretKoto))]
[TinyhandUnion((int)KotoKind.Bar, typeof(BarKoto))]
[TinyhandUnion((int)KotoKind.And, typeof(AndKoto))]
[TinyhandUnion((int)KotoKind.Or, typeof(OrKoto))]
[TinyhandUnion((int)KotoKind.Equals, typeof(EqualsKoto))]
[TinyhandUnion((int)KotoKind.PlusEquals, typeof(PlusEqualsKoto))]
[TinyhandUnion((int)KotoKind.MinusEquals, typeof(MinusEqualsKoto))]
[TinyhandUnion((int)KotoKind.AsteriskEquals, typeof(AsteriskEqualsKoto))]
[TinyhandUnion((int)KotoKind.SlashEquals, typeof(SlashEqualsKoto))]
[TinyhandUnion((int)KotoKind.PercentEquals, typeof(PercentEqualsKoto))]
[TinyhandUnion((int)KotoKind.AmpersandEquals, typeof(AmpersandEqualsKoto))]
[TinyhandUnion((int)KotoKind.CaretEquals, typeof(CaretEqualsKoto))]
[TinyhandUnion((int)KotoKind.BarEquals, typeof(BarEqualsKoto))]
[TinyhandUnion((int)KotoKind.LessThanLessThanEquals, typeof(LessThanLessThanEqualsKoto))]
[TinyhandUnion((int)KotoKind.GreaterThanGreaterThanEquals, typeof(GreaterThanGreaterThanEqualsKoto))]

[TinyhandUnion((int)KotoKind.Invocation, typeof(InvocationKoto))]
[TinyhandUnion((int)KotoKind.Generics, typeof(GenericsKoto))]
[ValueLinkObject]
public abstract partial class Koto
{
    public const int MaxKind = (int)KotoKind.Omega + 1;

    #region FieldAndProperty

    public abstract KotoKind Akind { get; }

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

    /*[IgnoreMember]
    public Koto? Previous { get; internal set; }

    [IgnoreMember]
    public Koto? Next { get; internal set; }*/

    /*[IgnoreMember]
    public Koto? Previous => this.previous == null || this == this.Parent?.head ? null : this.previous;

    [IgnoreMember]
    public Koto? Next
    {//
        get
        {
            if (this is AttributeKoto)
            {
                return this.next == this.Parent?.head ? null : this.next;
            }
            else
            {
                return this.next == this.Parent?.attributeHead ? null : this.next;
            }
        }
    }*/

    [Key(0)]
    public AttributeKoto? AttributeChain { get; internal set; }

    public Type Atype => this.GetType();

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => this.Parent is null;

    public Kotonoha Kotonoha => this.CodeContext.Kotonoha;

    #endregion

    [Link(Primary = true, Type = ChainType.LinkedList, Name = "ChildLink")]
    public Koto(ref TokenReader reader, SourceRange range)
    {
        this.DiagnosticCollection = reader.Diagnostic;
        this.CodeContext = reader.CodeContext;
        this.Range = range;

        this.AttributeChain = reader.PopAttribute();
        // this.Parent = parent;
    }

    internal Koto(CodeContext codeContext, SourceRange range)
    {
        this.CodeContext = codeContext;
        this.Range = range;
    }

    public override string ToString()
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            this.WriteTo(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    public virtual bool IsToplevel => false;

    public virtual void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append("Koto");
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

    public void AddAttribute(AttributeKoto attributeKoto)
    {
        if (attributeKoto.Parent is not null)
        {
            throw new InvalidOperationException();
        }

        attributeKoto.Parent = this;
        attributeKoto.AttributeChain = this.AttributeChain;
        this.AttributeChain = attributeKoto;
    }

    public bool RemoveAttribute(AttributeKoto attributeKoto)
    {
        AttributeKoto? previous = default;
        var current = this.AttributeChain;
        while (current is not null)
        {
            if (current == attributeKoto)
            {
                current.Parent = default;
                if (previous == null)
                {
                    this.AttributeChain = current.AttributeChain;
                }
                else
                {
                    previous.AttributeChain = current.AttributeChain;
                }

                return true;
            }
        }

        return false;
    }

    internal virtual bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        return false;
    }
}
