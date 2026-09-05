// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    /// <summary>A local binding declaration.</summary>
    Field,

    /// <summary>A Boolean literal.</summary>
    BoolLiteral,

    /// <summary>A numeric literal.</summary>
    NumberLiteral,

    /// <summary>A string literal.</summary>
    StringLiteral,

    /// <summary>An array literal expression.</summary>
    ArrayLiteral,

    /// <summary>A dictionary literal expression.</summary>
    DictionaryLiteral,

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

    /// <summary>An index measured backward from the end of a collection.</summary>
    FromEndIndex,

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

    /// <summary>A half-open or inclusive range expression.</summary>
    Range,

    /// <summary>An indentation-delimited expression block.</summary>
    CodeBlock,

    /// <summary>An <c>if</c> expression.</summary>
    If,

    /// <summary>A deferred compile-time <c>#if</c> directive.</summary>
    CompileTimeIf,

    /// <summary>A deferred compile-time <c>#case</c> group.</summary>
    CompileTimeCaseGroup,

    /// <summary>A <c>match</c> expression.</summary>
    Match,

    /// <summary>A <c>while</c> expression.</summary>
    While,

    /// <summary>A <c>return</c> expression.</summary>
    Return,

    /// <summary>A <c>exit</c> expression.</summary>
    Exit,

    /// <summary>A <c>continue</c> expression.</summary>
    Continue,

    // Types

    /// <summary>A tuple type.</summary>
    TupleType,

    /// <summary>A function type.</summary>
    FunctionType,

    /// <summary>A <c>for</c> expression.</summary>
    For,

    /// <summary>An unconditional <c>loop</c> expression.</summary>
    Loop,

    /// <summary>A <c>yield</c> expression.</summary>
    Yield,

    /// <summary>A Property declaration.</summary>
    Property,

    /// <summary>A Property accessor declaration.</summary>
    PropertyAccessor,

    /// <summary>A labeled Block or Loop.</summary>
    Labeled,

    /// <summary>The Unit value ().</summary>
    UnitLiteral,

    /// <summary>A string containing embedded expressions.</summary>
    InterpolatedString,

    /// <summary>A tuple value expression.</summary>
    TupleLiteral,

    /// <summary>The upper-bound sentinel for node kinds.</summary>
    Omega,
}

/// <summary>
/// Provides the base representation of a Koto syntax-tree node.
/// </summary>
/// <remarks>
/// Tree links (<see cref="Parent"/>, <see cref="CodeContext"/>) are set during parsing.
/// Concrete nodes enumerate their children through <see cref="ChildNodes"/>.
/// </remarks>
public abstract class Koto
{
    /// <summary>The size required for a table indexed by <see cref="KotoKind"/>.</summary>
    public const int MaxKind = (int)KotoKind.Omega;

    #region FieldAndProperty

    /// <summary>Gets the concrete node kind.</summary>
    public abstract KotoKind Akind { get; }

    /// <summary>Gets the diagnostic destination associated with this node.</summary>
    public DiagnosticCollection? DiagnosticCollection => this.CodeContext?.DiagnosticCollection;

    /// <summary>Gets the node span in the source document.</summary>
    public SourceSpan Span { get; internal set; }

    /// <summary>Gets the code context that owns this node.</summary>
    public CodeContext CodeContext { get; internal set; }

    /// <summary>Gets the parent node, or <see langword="null"/> for the root.</summary>
    public Koto? Parent { get; internal set; }

    /// <summary>Gets the direct syntax-tree children of this node.</summary>
    public IEnumerable<Koto> ChildNodes
    {
        get
        {
            if (this.AttributeChain is not null)
            {
                yield return this.AttributeChain;
            }

            foreach (var child in this.GetChildNodes())
            {
                yield return child;
            }
        }
    }

    /// <summary>Gets the attributes attached to this node.</summary>
    public AttributeKoto? AttributeChain { get; internal set; }

    /// <summary>Gets a value indicating whether this node is the tree root.</summary>
    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => this.Parent is null;

    /// <summary>Gets the source unit that owns this node.</summary>
    public Kotonoha Kotonoha => this.CodeContext.Kotonoha;

    /// <summary>Gets a value indicating whether the node is a top-level declaration.</summary>
    public virtual bool IsToplevel => false;

    #endregion

    /// <summary>Initializes a new instance of the <see cref="Koto"/> class and takes the pending attribute chain from the reader.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The node span.</param>
    public Koto(ref TokenReader reader, SourceSpan range)
    {
        this.CodeContext = reader.CodeContext;
        this.Span = range;
        this.SetAttributeChain(reader.PopAttribute());
    }

    internal Koto(CodeContext codeContext, SourceSpan range)
    {
        this.CodeContext = codeContext;
        this.Span = range;
    }

    /// <summary>Returns the source text of this node.</summary>
    /// <returns>The source text.</returns>
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

    /// <summary>Writes this node as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public virtual void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append("Koto");
    }

    /// <summary>Resolves an identifier relative to this node.</summary>
    /// <param name="identifier">The identifier to resolve.</param>
    /// <returns>The resolved node, or <see langword="null"/>.</returns>
    public virtual Koto? ResolveIdentifier(ReadOnlySpan<char> identifier)
        => default;

    /// <summary>Binds this node and its children to a compilation.</summary>
    /// <param name="compilation">The active compilation.</param>
    public virtual void Bind(Compilation compilation)
    {
        foreach (var child in this.GetChildNodes())
        {
            child.Bind(compilation);
        }
    }

    /// <summary>Adds a diagnostic for this node.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="obj">The first optional diagnostic argument.</param>
    /// <param name="obj2">The second optional diagnostic argument.</param>
    public void AddDiagnostic(DiagnosticCode code, object? obj = null, object? obj2 = null)
        => this.DiagnosticCollection?.Add(this.Span, code, obj, obj2);

    /// <summary>Adds an attribute to this node.</summary>
    /// <param name="attributeKoto">The attribute to add.</param>
    public void AddAttribute(AttributeKoto attributeKoto)
    {
        if (attributeKoto.Parent is not null)
        {
            throw new InvalidOperationException();
        }

        var previous = this.AttributeChain;
        attributeKoto.Parent = this;
        attributeKoto.AttributeChain = previous;
        if (previous is not null)
        {
            previous.Parent = attributeKoto;
        }

        this.AttributeChain = attributeKoto;
    }

    /// <summary>Removes an attribute from this node.</summary>
    /// <param name="attributeKoto">The attribute to remove.</param>
    /// <returns><see langword="true"/> when the attribute was removed.</returns>
    public bool RemoveAttribute(AttributeKoto attributeKoto)
    {
        Koto previous = this;
        var current = this.AttributeChain;
        while (current is not null)
        {
            if (current == attributeKoto)
            {
                var next = current.AttributeChain;
                if (previous == this)
                {
                    this.AttributeChain = next;
                }
                else
                {
                    previous.AttributeChain = next;
                }

                if (next is not null)
                {
                    next.Parent = previous;
                }

                current.Parent = default;
                current.AttributeChain = default;
                return true;
            }

            previous = current;
            current = current.AttributeChain;
        }

        return false;
    }

    /// <summary>Attaches an attribute chain and links its parents.</summary>
    /// <param name="attributeChain">The attribute chain, or <see langword="null"/>.</param>
    internal void SetAttributeChain(AttributeKoto? attributeChain)
    {
        this.AttributeChain = attributeChain;
        Koto parent = this;
        while (attributeChain is not null)
        {
            attributeChain.Parent = parent;
            parent = attributeChain;
            attributeChain = attributeChain.AttributeChain;
        }
    }

    internal bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.AttributeChain == oldKoto && newKoto is AttributeKoto attribute)
        {
            this.AttributeChain = attribute;
        }
        else if (!this.ReplaceChildCore(oldKoto, newKoto))
        {
            return false;
        }

        oldKoto.Parent = default;
        newKoto.Parent = this;
        return true;
    }

    /// <summary>Replaces an element of a list in place.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="list">The list to update.</param>
    /// <param name="oldKoto">The current element.</param>
    /// <param name="newKoto">The replacement element.</param>
    /// <returns><see langword="true"/> when the element was replaced.</returns>
    protected static bool ReplaceInList<T>(IReadOnlyList<T>? list, Koto oldKoto, Koto newKoto)
        where T : Koto
    {
        if (list is not IList<T> mutable ||
            (mutable.IsReadOnly && list is not T[]) ||
            oldKoto is not T current || newKoto is not T replacement)
        {
            return false;
        }

        for (var index = 0; index < list.Count; index++)
        {
            if (ReferenceEquals(list[index], current))
            {
                mutable[index] = replacement;
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes the attribute chain, if any, followed by the requested trailing text.</summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="options">The trailing text option.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void WriteAttributeChainTo(ref IndentedStringBuilder builder, KotoWriteOptions options)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, options);
        }
    }

    /// <summary>Sets this node as the parent of a child.</summary>
    /// <param name="child">The child node, or <see langword="null"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void Adopt(Koto? child)
    {
        if (child is not null)
        {
            child.Parent = this;
        }
    }

    /// <summary>Sets this node as the parent of each child in a list.</summary>
    /// <typeparam name="T">The child node type.</typeparam>
    /// <param name="children">The child nodes, or <see langword="null"/>.</param>
    protected void Adopt<T>(List<T>? children)
        where T : Koto
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            child.Parent = this;
        }
    }

    /// <summary>Sets this node as the parent of an array or list of child nodes.</summary>
    /// <param name="children">The child nodes.</param>
    protected void Adopt(IReadOnlyList<Koto>? children)
    {
        if (children is Koto[] array)
        {
            foreach (var child in array)
            {
                child.Parent = this;
            }
        }
        else if (children is List<Koto> list)
        {
            this.Adopt(list);
        }
        else if (children is not null)
        {
            for (var i = 0; i < children.Count; i++)
            {
                children[i].Parent = this;
            }
        }
    }

    /// <summary>Enumerates children owned by the concrete node.</summary>
    /// <returns>The direct child nodes, excluding the attribute chain handled by <see cref="ChildNodes"/>.</returns>
    protected virtual IEnumerable<Koto> GetChildNodes()
        => [];

    /// <summary>Replaces a child reference owned by the concrete node.</summary>
    /// <param name="oldKoto">The current child.</param>
    /// <param name="newKoto">The replacement child.</param>
    /// <returns><see langword="true"/> when a child reference was replaced.</returns>
    protected virtual bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => false;
}
