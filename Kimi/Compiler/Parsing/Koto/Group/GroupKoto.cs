// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a namespace-like group or named type declaration.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : IdentifiableKoto, ITokenParser
{
    private enum DeclarationOrder : byte
    {
        None,
        TypeConstraint,
        NestedType,
        Field,
        Function,
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Group;

    #region FieldAndProperty

    /// <summary>Gets the declaration modifiers.</summary>
    [Key(3)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets or sets the group name.</summary>
    [Key(4)]
    public string Name { get; protected set; } = string.Empty;

    /// <summary>Gets the node kind for the concrete group type.</summary>
    public KotoKind KotoKind => this switch
    {
        StructKoto => KotoKind.Struct,
        EnumKoto => KotoKind.Enum,
        ExtensionKoto => KotoKind.Extension,
        ContractKoto => KotoKind.Contract,
        _ => KotoKind.Group,
    };

    /// <summary>Gets the declaration keyword kind for the concrete group type.</summary>
    public TokenKind TokenKind => this switch
    {
        StructKoto => TokenKind.Struct,
        EnumKoto => TokenKind.Enum,
        ExtensionKoto => TokenKind.Extension,
        ContractKoto => TokenKind.Contract,
        _ => TokenKind.Group,
    };

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
    protected Utf16Hashtable<Koto> IdentifierToGroupKoto { get; set; } = new();

    #endregion

    /// <summary>Initializes a new instance of the <see cref="GroupKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public GroupKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, range)
    {
        this.AttributeChain = state.AttributeKoto;
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
        this.TypeConstraints.Add(constraint);
        constraint.Parent = this;
    }

    /// <summary>Adds origin names to this group.</summary>
    /// <param name="origins">The origin names to add.</param>
    public void AddOrigins(IEnumerable<string> origins)
        => this.Origins.AddRange(origins);

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
        this.IdentifierToGroupKoto.Clear();
        this.GenericArguments.Clear();
        this.TypeConstraints.Clear();
        this.Origins.Clear();
    }

    /// <summary>Writes this group and all nested groups as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        GroupKoto? currentGroup = this.IsRoot ? null : this;
        this.UnparseAllInternal(0, ref builder, false);
    }

    /// <summary>Gets or creates a nested group from a qualified name.</summary>
    /// <param name="qualifiedName">The dot-separated group name.</param>
    /// <param name="kind">The final group's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final group.</returns>
    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text, kind, state, range);
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment, TokenKind.Group, default, default);
            text = text[(index + 1)..];
        }
    }

    /// <summary>Parses declarations into this group.</summary>
    /// <param name="reader">The token reader.</param>
    public void Parse(ref TokenReader reader)
        => this.Parse(ref reader, false);

    internal static GroupKoto CreateStandalone(CodeContext codeContext, TokenKind kind, TokenContext state, SourceSpan range, string name)
    {
        GroupKoto group = kind switch
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
        var groups = this.IdentifierToGroupKoto.ToArray();
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
            constraint.WriteTo(ref builder);
        }

        foreach (var koto in this.KotoList)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            koto.WriteTo(ref builder);
        }

        foreach (var nested in groups)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            ((GroupKoto)nested).WriteAsBlockItem(ref builder);
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

    internal void Parse(ref TokenReader reader, bool useCurrentContext)
    {
        var declarationOrder = DeclarationOrder.None;
        var acceptsTypeConstraints = this is not StructKoto || this.TypeConstraints.Count == 0;
        while (reader.CanRead)
        {
            bool isEnd;
            if (useCurrentContext)
            {
                useCurrentContext = false;
                isEnd = reader.IsEnd;
            }
            else
            {
                Parser.ConsumeAttributeAndModifier(ref reader, out isEnd);
            }

            if (isEnd)
            {
                return;
            }

            if (Parser.IsTypeConstraintStart(ref reader))
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
            var tokenKind = token.Kind;
            ITokenParser? nextParser = default;

            if (tokenKind == TokenKind.Alias)
            {
                // Consume invalid nested aliases so parsing can resume at the next declaration.
                reader.Advance();
                _ = KotoHelper.ParseQualifiedNameSegments(ref reader);
                reader.Diagnostic.Add(token.Span, DiagnosticCode.TopLevelKeywordAfterCode_Kd);
            }
            else if (tokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }
            else if (tokenKind == TokenKind.EndBlock)
            {
                reader.Advance();
                break;
            }
            else if (tokenKind == TokenKind.Let || tokenKind == TokenKind.Var)
            {
                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Field);
                reader.Advance();
                var fieldKoto = Parser.ParseField(ref reader, ref token);
                if (fieldKoto is not null)
                {
                    this.AddLast(fieldKoto);
                }
            }
            else if (tokenKind == TokenKind.RootGroup)
            {
                reader.Advance();
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(true);
                    goto NextToken;
                }

                var state = reader.TakeContext();
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group, state, token.Span);

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                }

                nextParser = groupKoto;
            }
            else if (tokenKind is TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract)
            {
                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.NestedType);
                reader.Advance();
                var r = Parser.ParseGroupDeclaration(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    goto NextToken;
                }

                var state = reader.TakeContext();
                var groupKoto = this.GetOrAddGroup(r.Name, tokenKind, state, token.Span);
                if (r.GenericArguments is not null && groupKoto.GenericArguments.Count == 0)
                {
                    groupKoto.AddGenericArguments(r.GenericArguments);
                }

                if (r.Origins is not null && groupKoto.Origins.Count == 0)
                {
                    groupKoto.AddOrigins(r.Origins);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextParser = groupKoto;
                }
            }
            else if (tokenKind == TokenKind.Func)
            {
                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Function);
                reader.Advance();
                var functionKoto = Parser.ParseFuncDeclaration(ref reader);
                if (functionKoto is not null)
                {
                    if (!functionKoto.IsExcluded)
                    {
                        this.AddLast(functionKoto);
                    }

                    while (reader.CurrentTokenKind == TokenKind.Separator)
                    {
                        reader.Advance();
                    }

                    if (reader.CurrentTokenKind == TokenKind.StartBlock)
                    {
                        nextParser = functionKoto;
                    }
                    else
                    {
                        // Recover a malformed signature by resuming at its body.
                        reader.SkipUntilStartBlock(0);
                        if (reader.CurrentTokenKind == TokenKind.StartBlock)
                        {
                            nextParser = functionKoto;
                        }
                    }
                }
            }
            else
            {
                var koto = Parser.ParseExpression(ref reader);
                if (koto is ErrorKoto)
                {
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                }
                else
                {
                    this.AddLast(koto);
                }
            }

NextToken:
            if (nextParser is not null)
            {
                nextParser.Parse(ref reader);
            }
        }
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        foreach (var argument in this.GenericArguments)
        {
            argument.RestoreAfterDeserialization(codeContext, this);
        }

        foreach (var constraint in this.TypeConstraints)
        {
            constraint.RestoreAfterDeserialization(codeContext, this);
        }

        foreach (var koto in this.KotoList)
        {
            koto.RestoreAfterDeserialization(codeContext, this);
        }

        foreach (var group in this.IdentifierToGroupKoto.ToArray())
        {
            group.RestoreAfterDeserialization(codeContext, this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckDeclarationOrder(ref TokenReader reader, ref DeclarationOrder current, DeclarationOrder next)
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

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind, TokenContext state, SourceSpan range)
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

        group = (GroupKoto)group.IdentifierToGroupKoto.GetOrAdd(text, factory);
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

    private void UnparseAllInternal(int indents, ref IndentedStringBuilder builder, bool parentDeclared)
    {
        var groupDeclared = false;

        if ((!this.IsRoot && (this.KotoList.Count > 0 || this.TypeConstraints.Count > 0 || this.Origins.Count > 0))
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.KotoKind == KotoKind.Group)
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
                constraint.WriteTo(ref builder);
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

        var groups = this.IdentifierToGroupKoto.ToArray();
        if (groups.Length > 0)
        {
            builder.EnsureTrailingBlankLine();
            foreach (var x in groups)
            {
                ((GroupKoto)x).UnparseAllInternal(indents + 1, ref builder, groupDeclared);
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

        foreach (var group in this.IdentifierToGroupKoto.ToArray())
        {
            group.Parent = this;
        }
    }
}
