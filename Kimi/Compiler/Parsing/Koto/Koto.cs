// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

#pragma warning disable SA1401 // Fields should be private

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines the kinds of nodes in a Koto syntax tree.
/// </summary>
public enum KotoKind : byte
{
    /// <summary>An invalid node.</summary>
    Invalid,

    // Group

    /// <summary>A contract declaration.</summary>
    Contract,

    /// <summary>An enumeration declaration.</summary>
    Enum,

    /// <summary>An extension declaration.</summary>
    Extension,

    /// <summary>A namespace-like group declaration.</summary>
    Group,

    /// <summary>A structure declaration.</summary>
    Struct,

    /// <summary>A function declaration.</summary>
    Function,

    // Nullary

    /// <summary>An error-recovery node.</summary>
    Error,

    /// <summary>An alias declaration.</summary>
    Alias,

    /// <summary>A field or variable declaration.</summary>
    Field,

    /// <summary>A Boolean literal.</summary>
    BoolLiteral,

    /// <summary>A numeric literal.</summary>
    NumberLiteral,

    /// <summary>A string literal.</summary>
    StringLiteral,

    /// <summary>An identifier name.</summary>
    IdentifierName,

    /// <summary>A type with ownership semantics.</summary>
    TypeSemantics,

    /// <summary>A semantics mask.</summary>
    SemanticsMask,

    // Unary

    /// <summary>An attribute expression.</summary>
    Attribute,

    /// <summary>A macro expression.</summary>
    Macro,

    /// <summary>A reference expression.</summary>
    Reference,

    /// <summary>An unwrap expression.</summary>
    Unwrap,

    /// <summary>A prefix caret expression.</summary>
    PrefixCaret,

    /// <summary>A unary plus expression.</summary>
    PrefixPlus,

    /// <summary>A prefix increment expression.</summary>
    PrefixPlusPlus,

    /// <summary>A unary minus expression.</summary>
    PrefixMinus,

    /// <summary>A prefix decrement expression.</summary>
    PrefixMinusMinus,

    /// <summary>A postfix increment expression.</summary>
    PostfixIncrement,

    /// <summary>A postfix decrement expression.</summary>
    PostfixDecrement,

    /// <summary>A logical negation expression.</summary>
    Not,

    /// <summary>A parenthesized expression.</summary>
    Parenthesized,

    // Binary

    /// <summary>A member-access expression.</summary>
    MemberAccess,

    /// <summary>An index expression.</summary>
    Index,

    /// <summary>A multiplication expression.</summary>
    Asterisk,

    /// <summary>A conversion expression.</summary>
    Conversion,

    /// <summary>A division expression.</summary>
    Slash,

    /// <summary>A remainder expression.</summary>
    Percent,

    /// <summary>An addition expression.</summary>
    Plus,

    /// <summary>A subtraction expression.</summary>
    Minus,

    /// <summary>A left-shift expression.</summary>
    LessThanLessThan,

    /// <summary>A right-shift expression.</summary>
    GreaterThanGreaterThan,

    /// <summary>A less-than comparison.</summary>
    LessThan,

    /// <summary>A less-than-or-equal comparison.</summary>
    LessThanEquals,

    /// <summary>A greater-than comparison.</summary>
    GreaterThan,

    /// <summary>A greater-than-or-equal comparison.</summary>
    GreaterThanEquals,

    /// <summary>An <c>as</c> expression.</summary>
    As,

    /// <summary>An <c>is</c> expression.</summary>
    Is,

    /// <summary>An equality comparison.</summary>
    EqualsEquals,

    /// <summary>An inequality comparison.</summary>
    ExclamationEquals,

    /// <summary>A bitwise-and expression.</summary>
    Ampersand,

    /// <summary>A bitwise-exclusive-or expression.</summary>
    Caret,

    /// <summary>A bitwise-or expression.</summary>
    Bar,

    /// <summary>A logical-and expression.</summary>
    And,

    /// <summary>A logical-or expression.</summary>
    Or,

    /// <summary>An assignment expression.</summary>
    Equals,

    /// <summary>An addition-assignment expression.</summary>
    PlusEquals,

    /// <summary>A subtraction-assignment expression.</summary>
    MinusEquals,

    /// <summary>A multiplication-assignment expression.</summary>
    AsteriskEquals,

    /// <summary>A division-assignment expression.</summary>
    SlashEquals,

    /// <summary>A remainder-assignment expression.</summary>
    PercentEquals,

    /// <summary>A bitwise-and-assignment expression.</summary>
    AmpersandEquals,

    /// <summary>A bitwise-exclusive-or-assignment expression.</summary>
    CaretEquals,

    /// <summary>A bitwise-or-assignment expression.</summary>
    BarEquals,

    /// <summary>A left-shift-assignment expression.</summary>
    LessThanLessThanEquals,

    /// <summary>A right-shift-assignment expression.</summary>
    GreaterThanGreaterThanEquals,

    // Misc

    /// <summary>An invocation expression.</summary>
    Invocation,

    /// <summary>A generic name.</summary>
    Generics,

    /// <summary>An indentation-delimited expression block.</summary>
    CodeBlock,

    /// <summary>An <c>if</c> expression.</summary>
    If,

    /// <summary>A <c>match</c> expression.</summary>
    Match,

    /// <summary>A <c>while</c> expression.</summary>
    While,

    /// <summary>A <c>return</c> expression.</summary>
    Return,

    /// <summary>A <c>break</c> expression.</summary>
    Break,

    /// <summary>A <c>continue</c> expression.</summary>
    Continue,

    // Types

    /// <summary>A tuple type.</summary>
    TupleType,

    /// <summary>A function type.</summary>
    FunctionType,

    /// <summary>The upper-bound sentinel for node kinds.</summary>
    Omega,
}

/// <summary>
/// Provides the base representation of a Koto syntax-tree node.
/// </summary>
[TinyhandObject(ReservedKeyCount = 1)]
[TinyhandUnion((int)KotoKind.Contract, typeof(ContractKoto))]
[TinyhandUnion((int)KotoKind.Enum, typeof(EnumKoto))]
[TinyhandUnion((int)KotoKind.Extension, typeof(ExtensionKoto))]
[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]
[TinyhandUnion((int)KotoKind.Struct, typeof(StructKoto))]
[TinyhandUnion((int)KotoKind.Function, typeof(FunctionKoto))]

[TinyhandUnion((int)KotoKind.Alias, typeof(AliasKoto))]
[TinyhandUnion((int)KotoKind.BoolLiteral, typeof(BoolLiteralKoto))]
[TinyhandUnion((int)KotoKind.Error, typeof(ErrorKoto))]
[TinyhandUnion((int)KotoKind.Field, typeof(FieldKoto))]
[TinyhandUnion((int)KotoKind.NumberLiteral, typeof(NumberLiteralKoto))]
[TinyhandUnion((int)KotoKind.StringLiteral, typeof(StringLiteralKoto))]
[TinyhandUnion((int)KotoKind.IdentifierName, typeof(IdentifierNameKoto))]
[TinyhandUnion((int)KotoKind.TypeSemantics, typeof(TypeKoto))]
[TinyhandUnion((int)KotoKind.SemanticsMask, typeof(SemanticsMaskKoto))]

[TinyhandUnion((int)KotoKind.Attribute, typeof(AttributeKoto))]
[TinyhandUnion((int)KotoKind.Macro, typeof(MacroKoto))]
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
[TinyhandUnion((int)KotoKind.Conversion, typeof(ConversionKoto))]
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
[TinyhandUnion((int)KotoKind.CodeBlock, typeof(CodeBlockKoto))]
[TinyhandUnion((int)KotoKind.If, typeof(IfKoto))]
[TinyhandUnion((int)KotoKind.Match, typeof(MatchKoto))]
[TinyhandUnion((int)KotoKind.While, typeof(WhileKoto))]
[TinyhandUnion((int)KotoKind.Return, typeof(ReturnKoto))]
[TinyhandUnion((int)KotoKind.Break, typeof(BreakKoto))]
[TinyhandUnion((int)KotoKind.Continue, typeof(ContinueKoto))]
[TinyhandUnion((int)KotoKind.TupleType, typeof(TupleTypeKoto))]
[TinyhandUnion((int)KotoKind.FunctionType, typeof(FunctionTypeKoto))]
[ValueLinkObject]
public abstract partial class Koto
{
    /// <summary>The size required for a table indexed by <see cref="KotoKind"/>.</summary>
    public const int MaxKind = (int)KotoKind.Omega + 1;

    #region FieldAndProperty

    /// <summary>Gets the concrete node kind.</summary>
    public abstract KotoKind Akind { get; }

    // Parser metadata is runtime-only and is restored after deserialization.

    /// <summary>Gets the diagnostic destination associated with this node.</summary>
    [IgnoreMember]
    public DiagnosticCollection? DiagnosticCollection { get; internal set; }

    /// <summary>Gets the node span in the source document.</summary>
    [IgnoreMember]
    public SourceSpan Span { get; internal set; }

    /// <summary>Gets the code context that owns this node.</summary>
    [IgnoreMember]
    public CodeContext CodeContext { get; internal set; }

    // Tree links are rebuilt after deserialization.

    /// <summary>Gets the parent node, or <see langword="null"/> for the root.</summary>
    [IgnoreMember]
    public Koto? Parent { get; internal set; }

    /// <summary>Gets the attributes attached to this node.</summary>
    [Key(0)]
    public AttributeKoto? AttributeChain { get; internal set; }

    /// <summary>Gets a value indicating whether this node is the tree root.</summary>
    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => this.Parent is null;

    /// <summary>Gets the source unit that owns this node.</summary>
    public Kotonoha Kotonoha => this.CodeContext.Kotonoha;

    #endregion

    /// <summary>Initializes a new instance of the <see cref="Koto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The node span.</param>
    [Link(Primary = true, Type = ChainType.LinkedList, Name = "ChildLink")]
    public Koto(ref TokenReader reader, SourceSpan range)
    {
        this.DiagnosticCollection = reader.Diagnostic;
        this.CodeContext = reader.CodeContext;
        this.Span = range;

        this.AttributeChain = reader.PopAttribute();
    }

    internal Koto(CodeContext codeContext, SourceSpan range)
    {
        this.CodeContext = codeContext;
        this.Span = range;
    }

    /// <inheritdoc/>
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

    /// <summary>Gets a value indicating whether the node is a top-level declaration.</summary>
    public virtual bool IsToplevel => false;

    /// <summary>Writes this node as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public virtual void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append("Koto");
    }

    /// <summary>Resolves an identifier relative to this node.</summary>
    /// <param name="identifier">The identifier to resolve.</param>
    /// <returns>The resolved node, or <see langword="null"/>.</returns>
    public virtual Koto? ResolveIdentifier(ReadOnlySpan<char> identifier)
    {
        return default;
    }

    /// <summary>Binds this node to a compilation.</summary>
    /// <param name="compilation">The active compilation.</param>
    public virtual void Bind(Compilation compilation)
    {
    }

    /// <summary>Adds a diagnostic for this node.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="obj">The first optional diagnostic argument.</param>
    /// <param name="obj2">The second optional diagnostic argument.</param>
    public void AddDiagnostic(DiagnosticCode code, object? obj = null, object? obj2 = null)
    {
        this.DiagnosticCollection?.Add(this.Span, code, obj, obj2);
    }

    /// <summary>Adds an attribute to this node.</summary>
    /// <param name="attributeKoto">The attribute to add.</param>
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

    /// <summary>Removes an attribute from this node.</summary>
    /// <param name="attributeKoto">The attribute to remove.</param>
    /// <returns><see langword="true"/> when the attribute was removed.</returns>
    public bool RemoveAttribute(AttributeKoto attributeKoto)
    {
        AttributeKoto? previous = default;
        var current = this.AttributeChain;
        while (current is not null)
        {
            if (current == attributeKoto)
            {
                var next = current.AttributeChain;
                current.Parent = default;
                if (previous == null)
                {
                    this.AttributeChain = next;
                }
                else
                {
                    previous.AttributeChain = next;
                }

                current.AttributeChain = default;
                return true;
            }

            previous = current;
            current = current.AttributeChain;
        }

        return false;
    }

    internal virtual void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        this.CodeContext = codeContext;
        this.DiagnosticCollection = codeContext.DiagnosticCollection;
        this.Parent = parent;

        if (this.AttributeChain is not null)
        {
            this.AttributeChain.RestoreAfterDeserialization(codeContext, this);
        }
    }

    internal virtual bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        return false;
    }
}
