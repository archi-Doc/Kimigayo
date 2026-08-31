// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;
#pragma warning disable SA1202 // Serialization keys keep related storage together.
#pragma warning disable SA1204 // Parsing helpers are grouped by responsibility.

/// <summary>
/// Provides shared storage and parsing for Kimigayo collection declarations.
/// </summary>
[TinyhandObject]
public abstract partial class CollectionKoto : IdentifiableKoto, ITokenParser
{
    protected enum DeclarationOrder : byte
    {
        None,
        TypeConstraint,
        Field,
        Function,
    }

    /// <inheritdoc/>
    public abstract override KotoKind Akind { get; }

    #region FieldAndProperty

    /// <summary>Gets the declaration modifiers.</summary>
    [Key(3)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets or sets the group name.</summary>
    [Key(4)]
    public string Name { get; protected set; } = string.Empty;

    /// <summary>Gets the declaration keyword kind for the concrete collection type.</summary>
    public abstract TokenKind TokenKind { get; }

    /// <summary>Gets a value indicating whether the collection can be instantiated.</summary>
    public abstract bool IsInstantiable { get; }

    /// <summary>Gets a value indicating whether every member is static.</summary>
    public virtual bool HasStaticMembersOnly => false;

    /// <summary>Gets a value indicating whether generic parameters are supported.</summary>
    public virtual bool SupportsGenerics => false;

    /// <summary>Gets a value indicating whether origins are supported.</summary>
    public virtual bool SupportsOrigins => false;

    /// <summary>Gets a value indicating whether type constraints are supported.</summary>
    public virtual bool SupportsTypeConstraints => false;

    /// <summary>Gets the generic parameters.</summary>
    [Key(7)]
    public List<TypeKoto> GenericArguments { get; private set; } = [];

    /// <summary>Gets the type constraints.</summary>
    [Key(8)]
    public List<IsKoto> TypeConstraints { get; private set; } = [];

    /// <summary>Gets the declared origins.</summary>
    [Key(9)]
    public List<string> Origins { get; private set; } = [];

    [Key(5)]
    protected List<Koto> KotoList { get; set; } = [];

    [Key(6)]
    protected Utf16Hashtable<Koto> IdentifierToCollectionKoto { get; set; } = new();

    /// <summary>Gets fields and functions in declaration order.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> Members => this.KotoList;

    /// <summary>Gets nested group and type declarations.</summary>
    [IgnoreMember]
    public IEnumerable<CollectionKoto> NestedCollections
        => this.IdentifierToCollectionKoto.ToArray().Cast<CollectionKoto>();

    #endregion

    /// <summary>Initializes a new instance of the <see cref="CollectionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected CollectionKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CollectionKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    protected CollectionKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, range)
    {
        this.SetAttributeChain(state.AttributeKoto);
        this.Modifier = state.ModifierKind;
    }

    /// <inheritdoc/>
    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    /// <summary>Adds a child node to this group.</summary>
    /// <param name="koto">The child node to add.</param>
    public void AddLast(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    /// <summary>Adds generic parameters to this group.</summary>
    /// <param name="genericArguments">The generic parameters to add.</param>
    public void AddGenericArguments(IEnumerable<TypeKoto> genericArguments)
    {
        if (!this.SupportsGenerics)
        {
            return;
        }

        foreach (var argument in genericArguments)
        {
            this.GenericArguments.Add(argument);
            argument.Parent = this;
        }
    }

    /// <summary>Adds a type constraint to this group.</summary>
    /// <param name="constraint">The constraint to add.</param>
    public void AddTypeConstraint(IsKoto constraint)
    {
        if (!this.SupportsTypeConstraints)
        {
            return;
        }

        this.TypeConstraints.Add(constraint);
        constraint.Parent = this;
    }

    /// <summary>Adds origin names to this group.</summary>
    /// <param name="origins">The origin names to add.</param>
    public void AddOrigins(IEnumerable<string> origins)
    {
        if (this.SupportsOrigins)
        {
            this.Origins.AddRange(origins);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (this.IsRoot)
        {
            return Constants.RootKotoName;
        }

        return $"{this.TokenKind.ToText()} {this.Name}";
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        if (this.IsRoot)
        {
            builder.Append(Constants.RootKotoName);
            return;
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.TokenKind.ToText());
        builder.Append(' ');
        builder.Append(this.Name);

        if (this.GenericArguments.Count > 0)
        {
            builder.Append('<');
            for (var i = 0; i < this.GenericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                this.GenericArguments[i].WriteTo(ref builder);
            }

            builder.Append('>');
        }

        if (this.Origins.Count > 0)
        {
            builder.AppendSpace();
            builder.Append(Constants.OriginKeyword);
            builder.AppendSpace();
            for (var i = 0; i < this.Origins.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append(this.Origins[i]);
            }
        }
    }

    /// <summary>Writes a root-group declaration for this group.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseToRoot(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
            builder.AppendLine();
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(Constants.RootgroupKeyword);
        builder.Append(' ');
        Parser.WriteQualifiedNameTo(this, ref builder);
    }

    /// <summary>Removes all declarations and group metadata.</summary>
    public void Clear()
    {
        this.KotoList.Clear();
        this.IdentifierToCollectionKoto.Clear();
        this.GenericArguments.Clear();
        this.TypeConstraints.Clear();
        this.Origins.Clear();
        if (ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            this.Kotonoha.ClearGeneratedFunction();
        }
    }

    /// <summary>Writes this group and all nested groups as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        this.UnparseAllInternal(ref builder);
    }

    /// <summary>Gets or creates a nested collection from a qualified name.</summary>
    /// <param name="qualifiedName">The dot-separated collection name.</param>
    /// <param name="kind">The final collection's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final collection.</returns>
    public CollectionKoto GetOrAddCollection(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddCollection(ref group, text, kind, state, range);
                return group;
            }

            var segment = text[..index];
            GetOrAddCollection(ref group, segment, TokenKind.Group, default, default);
            text = text[(index + 1)..];
        }
    }

    /// <summary>Gets or creates a collection from a qualified name.</summary>
    /// <remarks>Retained as a source-compatible alias for <c>GetOrAddCollection</c>.</remarks>
    /// <param name="qualifiedName">The dot-separated collection name.</param>
    /// <param name="kind">The final collection's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final collection.</returns>
    public CollectionKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
        => this.GetOrAddCollection(qualifiedName, kind, state, range);

    /// <summary>Parses the body supported by this collection kind.</summary>
    /// <param name="reader">The token reader.</param>
    public abstract void Parse(ref TokenReader reader);

    internal static CollectionKoto CreateStandalone(CodeContext codeContext, TokenKind kind, TokenContext state, SourceSpan range, string name)
    {
        CollectionKoto group = kind switch
        {
            TokenKind.Struct => new StructKoto(codeContext, state, range),
            TokenKind.Enum => new EnumKoto(codeContext, state, range),
            TokenKind.Extension => new ExtensionKoto(codeContext, state, range),
            TokenKind.Contract => new ContractKoto(codeContext, state, range),
            _ => new GroupKoto(codeContext, state, range),
        };

        group.Name = name;
        return group;
    }

    internal void WriteAsBlockItem(ref IndentedStringBuilder builder)
    {
        this.WriteTo(ref builder);
        var groups = this.IdentifierToCollectionKoto.ToArray();
        if (this.TypeConstraints.Count == 0 && this.KotoList.Count == 0 && groups.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        var hasPrevious = false;
        foreach (var constraint in this.TypeConstraints)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            this.WriteTypeConstraintTo(constraint, ref builder);
        }

        foreach (var koto in this.KotoList)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            koto.WriteTo(ref builder);
        }

        foreach (var nested in groups)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            ((CollectionKoto)nested).WriteAsBlockItem(ref builder);
        }

        builder.DecrementIndent();

        static void WriteSeparator(ref IndentedStringBuilder builder, ref bool hasPrevious)
        {
            if (hasPrevious)
            {
                builder.AppendLine();
            }
            else
            {
                hasPrevious = true;
            }
        }
    }

    /// <summary>Parses fields, functions, and optionally ordinary type constraints.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="parseTypeConstraints">Whether ordinary type constraints are accepted.</param>
    protected void ParseFieldAndFunctionMembers(ref TokenReader reader, bool parseTypeConstraints)
    {
        ConsumeBlockStart(ref reader);
        var declarationOrder = DeclarationOrder.None;
        var acceptsTypeConstraints = parseTypeConstraints && this.TypeConstraints.Count == 0;
        while (TryBeginDeclaration(ref reader))
        {
            if (parseTypeConstraints && Parser.IsTypeConstraintStart(ref reader))
            {
                if (!acceptsTypeConstraints)
                {
                    reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                    continue;
                }

                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.TypeConstraint);
                var constraint = Parser.ParseTypeConstraint(ref reader);
                if (constraint is not null)
                {
                    this.AddTypeConstraint(constraint);
                }

                continue;
            }

            var token = reader.CurrentToken;
            if (!this.TryParseFieldOrFunction(ref reader, ref declarationOrder))
            {
                SkipUnexpectedDeclaration(ref reader, token);
            }
        }
    }

    /// <summary>Consumes an unimplemented collection body without producing members.</summary>
    /// <param name="reader">The token reader.</param>
    protected static void SkipUnimplementedBody(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        var depth = 0;
        while (reader.CanRead)
        {
            if (reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (depth == 0)
                {
                    reader.Advance();
                    return;
                }

                depth--;
            }

            reader.Advance();
        }
    }

    /// <summary>Consumes the opening block token when the caller left it for the collection parser.</summary>
    /// <param name="reader">The token reader.</param>
    protected static void ConsumeBlockStart(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.Advance();
        }
    }

    /// <summary>Consumes declaration trivia and detects the end of the current collection body.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> when another declaration is available.</returns>
    protected static bool TryBeginDeclaration(ref TokenReader reader)
    {
        Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
        if (isEnd)
        {
            return false;
        }

        if (reader.CurrentTokenKind == TokenKind.EndBlock)
        {
            reader.Advance();
            return false;
        }

        return true;
    }

    /// <summary>Attempts to parse one field or function declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationOrder">The current declaration-order state.</param>
    /// <returns><see langword="true"/> when a supported member was consumed.</returns>
    protected bool TryParseFieldOrFunction(ref TokenReader reader, ref DeclarationOrder declarationOrder)
    {
        var token = reader.CurrentToken;
        if (token.Kind is TokenKind.Let or TokenKind.Var)
        {
            CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Field);
            reader.Advance();
            var fieldKoto = Parser.ParseField(ref reader, ref token);
            if (fieldKoto is not null)
            {
                this.AddLast(fieldKoto);
            }

            return true;
        }

        if (token.Kind != TokenKind.Func)
        {
            return false;
        }

        CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Function);
        reader.Advance();
        var functionKoto = Parser.ParseFuncDeclaration(ref reader);
        if (functionKoto is null)
        {
            return true;
        }

        if (!functionKoto.IsExcluded)
        {
            this.AddLast(functionKoto);
        }

        while (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }

        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.SkipUntilStartBlock(0);
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            functionKoto.Parse(ref reader);
        }

        return true;
    }

    /// <summary>Reports and skips a declaration unsupported by the current collection kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The unsupported declaration's first token.</param>
    protected static void SkipUnexpectedDeclaration(ref TokenReader reader, Token token)
    {
        reader.Diagnostic.Add(
            token.Span,
            DiagnosticCode.UnexpectedToken_Kd,
            reader.GetSpan(token).ToString());
        reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
        while (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
        }

        reader.ClearContext();
    }

    /// <summary>Writes one type constraint in the syntax used by this collection kind.</summary>
    /// <param name="constraint">The constraint to write.</param>
    /// <param name="builder">The destination builder.</param>
    protected virtual void WriteTypeConstraintTo(IsKoto constraint, ref IndentedStringBuilder builder)
        => constraint.WriteTo(ref builder);

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var argument in this.GenericArguments)
        {
            yield return argument;
        }

        foreach (var constraint in this.TypeConstraints)
        {
            yield return constraint;
        }

        foreach (var koto in this.KotoList)
        {
            yield return koto;
        }

        foreach (var group in this.IdentifierToCollectionKoto.ToArray())
        {
            yield return group;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        var index = this.KotoList.IndexOf(oldKoto);
        if (index >= 0)
        {
            this.KotoList[index] = newKoto;
            return true;
        }

        if (oldKoto is TypeKoto oldType && newKoto is TypeKoto newType)
        {
            var typeIndex = this.GenericArguments.IndexOf(oldType);
            if (typeIndex >= 0)
            {
                this.GenericArguments[typeIndex] = newType;
                return true;
            }
        }

        if (oldKoto is IsKoto oldConstraint && newKoto is IsKoto newConstraint)
        {
            var constraintIndex = this.TypeConstraints.IndexOf(oldConstraint);
            if (constraintIndex >= 0)
            {
                this.TypeConstraints[constraintIndex] = newConstraint;
                return true;
            }
        }

        if (oldKoto is CollectionKoto oldCollection && newKoto is CollectionKoto newCollection &&
            this.IdentifierToCollectionKoto.TryGetValue(oldCollection.Name, out var registered) &&
            ReferenceEquals(registered, oldCollection))
        {
            if (!oldCollection.Name.Equals(newCollection.Name, StringComparison.Ordinal) &&
                this.IdentifierToCollectionKoto.TryGetValue(newCollection.Name, out _))
            {
                return false;
            }

            if (this.IdentifierToCollectionKoto.TryRemove(oldCollection.Name) &&
                this.IdentifierToCollectionKoto.TryAdd(newCollection.Name, newCollection))
            {
                return true;
            }

            this.IdentifierToCollectionKoto.TryAdd(oldCollection.Name, oldCollection);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CheckDeclarationOrder(ref TokenReader reader, ref DeclarationOrder current, DeclarationOrder next)
    {
        if (next < current)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DeclarationOrderWarning_Kd);
            current = next;
        }
        else
        {
            current = next;
        }
    }

    private static void GetOrAddCollection(ref CollectionKoto group, ReadOnlySpan<char> text, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, Koto> factory = kind switch
        {
            TokenKind.Struct => x => new StructKoto(codeContext, state, range),
            TokenKind.Enum => x => new EnumKoto(codeContext, state, range),
            TokenKind.Extension => x => new ExtensionKoto(codeContext, state, range),
            TokenKind.Contract => x => new ContractKoto(codeContext, state, range),
            _ => x => new GroupKoto(codeContext, state, range),
        };

        group = (CollectionKoto)group.IdentifierToCollectionKoto.GetOrAdd(text, factory);
        if (string.IsNullOrEmpty(group.Name))
        {
            group.Parent = parent;
            group.Name = text.ToString();
        }
        else
        {
            group.Merge(state, range);
        }
    }

    private void Merge(TokenContext state, SourceSpan range)
    {
    }

    private void UnparseAllInternal(ref IndentedStringBuilder builder)
    {
        var groupDeclared = false;

        if ((!this.IsRoot && (this.KotoList.Count > 0 || this.TypeConstraints.Count > 0 || this.Origins.Count > 0))
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.Akind == KotoKind.Group)
            {
                builder.SetIndent(0);
                this.UnparseToRoot(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }
            else
            {
                this.WriteTo(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }

            groupDeclared = true;
        }

        if (this.TypeConstraints.Count > 0)
        {
            foreach (var constraint in this.TypeConstraints)
            {
                this.WriteTypeConstraintTo(constraint, ref builder);
                builder.AppendLine();
            }

            if (this.KotoList.Count > 0)
            {
                builder.AppendLine();
            }
        }

        if (this.KotoList.Count > 0)
        {
            var previousToplevel = false;
            foreach (var x in this.KotoList)
            {
                if (!x.IsToplevel && previousToplevel)
                {
                    builder.AppendLine();
                }

                x.WriteTo(ref builder);
                builder.AppendLine();

                previousToplevel = x.IsToplevel;
            }
        }

        if (this.IsRoot && this.Kotonoha.GeneratedFunction is { Body.Items.Count: > 0 } generatedFunction)
        {
            if (this.KotoList.Count > 0)
            {
                builder.AppendLine();
            }

            generatedFunction.WriteTo(ref builder);
            builder.AppendLine();
        }

        var groups = this.IdentifierToCollectionKoto.ToArray();
        if (groups.Length > 0)
        {
            builder.EnsureTrailingBlankLine();
            foreach (var x in groups)
            {
                ((CollectionKoto)x).UnparseAllInternal(ref builder);
            }
        }

        if (groupDeclared)
        {
            builder.DecrementIndent();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        foreach (var argument in this.GenericArguments)
        {
            argument.Parent = this;
        }

        foreach (var constraint in this.TypeConstraints)
        {
            constraint.Parent = this;
        }

        foreach (var koto in this.KotoList)
        {
            koto.Parent = this;
        }

        foreach (var group in this.IdentifierToCollectionKoto.ToArray())
        {
            group.Parent = this;
        }
    }
}
