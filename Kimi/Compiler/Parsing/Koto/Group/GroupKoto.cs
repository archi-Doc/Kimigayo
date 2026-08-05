// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1202 // Elements should be ordered by access

[TinyhandObject(ReservedKeyCount = 3)]
public abstract partial class BlockKoto : IdentifiableKoto
{
    [Key(2)]
    public Koto.GoshujinClass Children { get; protected set; } = new();

    public BlockKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    public BlockKoto(CodeContext codeContext, SourceRange range)
        : base(codeContext, range)
    {
    }
}

[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class IdentifiableKoto : Koto
{
    [Key(1)]
    public ulong KotoId
    {
        get
        {
            if (field == 0)
            {
                var hash = XxHash3Slim.Hash64(this.GetIdentifier());

                var parent = this.Parent;
                while (parent is not null)
                {
                    if (parent is IdentifiableKoto identifiableKoto)
                    {
                        hash = XxHash3Slim.Combine(identifiableKoto.KotoId, hash);
                    }

                    parent = parent.Parent;
                }

                field = hash;
            }

            return field;
        }

        protected set
        {
            field = value;
        }
    }

    public abstract ReadOnlySpan<char> GetIdentifier();

    public IdentifiableKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    public IdentifiableKoto(CodeContext codeContext, SourceRange range)
        : base(codeContext, range)
    {
    }

    /*internal IdentifiableKoto(FrontendMetadata compilationMetadata)
        : base(compilationMetadata)
    {
    }*/
}

/// <summary>
/// namespace, struct, enum, extension, contract.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : BlockKoto
{
    public override KotoKind Akind => KotoKind.Group;

    // public static readonly TokenState DefaultState = new(default, ModifierKind.Public);

    #region FieldAndProperty

    [Key(3)]
    public ModifierKind Modifier { get; private set; }

    [Key(4)]
    public string Name { get; protected set; } = string.Empty;

    [Key(5)]
    protected List<Koto> KotoList { get; set; } = [];

    [Key(6)]
    protected Utf16Hashtable<GroupKoto> IdentifierToGroupKoto { get; set; } = new();

    public KotoKind KotoKind => this switch
    {
        StructKoto => KotoKind.Struct,
        EnumKoto => KotoKind.Enum,
        ExtensionKoto => KotoKind.Extension,
        ContractKoto => KotoKind.Contract,
        _ => KotoKind.Group,
    };

    public TokenKind TokenKind => this switch
    {
        StructKoto => TokenKind.Struct,
        EnumKoto => TokenKind.Enum,
        ExtensionKoto => TokenKind.Extension,
        ContractKoto => TokenKind.Contract,
        _ => TokenKind.Group,
    };

    #endregion

    public GroupKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext, TokenState state, SourceRange range)
        : base(codeContext, range)
    {
        this.AttributeChain = state.AttributeKoto;
        this.Modifier = state.ModifierKind;
    }

    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    public void AddLast(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    public override string ToString()
    {
        if (this.IsRoot)
        {
            return "Root";
        }

        return $"{this.TokenKind.ToText()} {this.Name}";
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {// public group A
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        if (this.IsRoot)
        {
            builder.Append("Root");
            return;
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.TokenKind.ToText());
        builder.Append(' ');
        builder.Append(this.Name);
    }

    public void UnparseToRoot(ref IndentedStringBuilder builder)
    {// rootgroup A
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
            builder.AppendLine();
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(Constants.RootgroupKeyword);
        builder.Append(' ');
        KotoParser.WriteQualifiedNameTo(this, ref builder);
    }

    public void Parse(ref TokenReader reader, bool consumed)
    {
        while (reader.CanRead)
        {
            if (!consumed)
            {
                KotoParser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
                if (isEnd)
                {
                    return;
                }
            }

            var token = reader.CurrentToken;
            GroupKoto? nextGroup = default;

            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias (not supported)
                reader.Advance();
                _ = KotoHelper.ValidateAndGetNamespace2(ref reader);
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.Kind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }
            else if (token.Kind == TokenKind.EndBlock)
            {// Exit block
                reader.Advance();
                break;
            }
            else if (token.Kind == TokenKind.Let ||
                token.Kind == TokenKind.Var)
            {// let a = 1, var b = 2
                reader.Advance();
                var fieldKoto = KotoParser.ParseField(ref reader, ref token);
                if (fieldKoto is not null)
                {
                    this.AddLast(fieldKoto);
                }
            }
            else if (token.Kind == TokenKind.RootGroup)
            {// rootgroup
                reader.Advance();
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(true);
                    goto NextToken;
                }

                var state = reader.StoreState();
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group, state, token.Range);
                // this.CodeContext.CurrentGroup = groupKoto;

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                }

                nextGroup = groupKoto;
            }
            else if (token.Kind == TokenKind.Group)
            {// group
                reader.Advance();
            }
            else if (token.Kind == TokenKind.Struct)
            {// struct
                reader.Advance();
                var r = KotoParser.ParseGroupDeclaration(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    goto NextToken;
                }

                var state = reader.StoreState();
                var structKoto = (StructKoto)this.GetOrAddGroup(r.Name, token.Kind, state, token.Range);
                if (r.List is not null)
                {
                    structKoto.BaseList.AddRange(r.List);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextGroup = structKoto;
                }
            }
            else
            {// Other
                var koto = KotoParser.ParseExpression(ref reader);
                if (koto is ErrorKoto)
                {// Error
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                }
                else
                {
                    this.AddLast(koto);
                }
            }

NextToken:
            if (nextGroup is not null)
            {
                nextGroup.Parse(ref reader, false);
            }
        }
    }

    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        GroupKoto? currentGroup = this.IsRoot ? null : this;
        this.UnparseAllInternal(0, ref builder, false);
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenState state, SourceRange range)
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

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind, TokenState state, SourceRange range)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, GroupKoto> factory = kind switch
        {
            TokenKind.Struct => x => new StructKoto(codeContext, state, range),
            TokenKind.Enum => x => new EnumKoto(codeContext, state, range),
            TokenKind.Extension => x => new ExtensionKoto(codeContext, state, range),
            TokenKind.Contract => x => new ContractKoto(codeContext, state, range),
            _ => x => new GroupKoto(codeContext, state, range),
        };

        group = (GroupKoto)group.IdentifierToGroupKoto.GetOrAdd(text, factory);
        if (string.IsNullOrEmpty(group.Name))
        {// New
            group.Parent = parent;
            group.Name = text.ToString();
        }
        else
        {// Existing
            group.Merge(state, range);
        }
    }

    private void Merge(TokenState state, SourceRange range)
    {
    }

    private void UnparseAllInternal(int indents, ref IndentedStringBuilder builder, bool parentDeclared)
    {
        var groupDeclared = false;

        if ((!this.IsRoot && this.KotoList.Count > 0)
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.KotoKind == KotoKind.Group)
            {// rootgroup A
                builder.SetIndent(0);
                this.UnparseToRoot(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }
            else
            {// struct A
                this.WriteTo(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }

            groupDeclared = true;
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
            // DeclareGroup(builder, ref currentGroup);

            builder.EnsureTrailingBlankLine();
            foreach (var x in groups)
            {
                x.UnparseAllInternal(indents + 1, ref builder, groupDeclared);
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
    }
}
