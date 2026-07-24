// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1202 // Elements should be ordered by access

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
public partial class GroupKoto : IdentifiableKoto
{
    // public static readonly TokenState DefaultState = new(default, ModifierKind.Public);

    #region FieldAndProperty

    [Key(2)]
    public ModifierKind Modifier { get; private set; }

    [Key(3)]
    public string Name { get; protected set; } = string.Empty;

    [Key(4)]
    protected List<Koto> KotoList { get; set; } = [];

    [Key(5)]
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

    public override void UnparseTo(IndentWriter writer)
    {// public group A
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, writer);
            writer.WriteLine();
        }

        if (this.IsRoot)
        {
            writer.Write("Root");
            return;
        }

        this.Modifier.WriteTo(writer, true);
        writer.Write(this.TokenKind.ToText());
        writer.Write(' ');
        writer.Write(this.Name);
    }

    public void UnparseToRoot(IndentWriter writer)
    {// rootgroup A
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, writer);
            writer.WriteLine();
        }

        this.Modifier.WriteTo(writer, true);
        writer.Write(Constants.RootgroupKeyword);
        writer.Write(' ');
        KotoParser.WriteQualifiedNameTo(this, writer);
    }

    public void Parse(ref Token token, ref TokenReader reader)
    {
        while (true)
        {
            GroupKoto? nextGroup = default;

            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias (not supported)
                _ = KotoHelper.ValidateAndGetNamespace2(ref reader);
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.Kind == TokenKind.EndBlock)
            {// Exit block
                _ = KotoParser.ConsumeAttributeModifierAndRead(ref reader, out token);
                break;
            }
            else if (token.Kind == TokenKind.Let ||
                token.Kind == TokenKind.Var)
            {// let a = 1, var b = 2
                var fieldKoto = KotoParser.ParseField(ref reader, ref token);
                if (fieldKoto is not null)
                {
                    this.AddLast(fieldKoto);
                }
            }
            else if (token.Kind == TokenKind.RootGroup)
            {// rootgroup
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                var state = reader.StoreState();
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group, state, token.Range);
                this.CodeContext.CurrentGroup = groupKoto;

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                }

                nextGroup = groupKoto;
            }
            else if (token.Kind == TokenKind.Group)
            {// group
            }
            else if (token.Kind == TokenKind.Struct)
            {// struct
                var r = KotoParser.ParseGroupDeclaration(ref reader);
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
            {// unknown
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }

            // Consume Attribute and modifiers
            _ = KotoParser.ConsumeAttributeModifierAndRead(ref reader, out token);
            if (!token.IsValid)
            {
                return;
            }

            if (nextGroup is not null)
            {
                nextGroup.Parse(ref token, ref reader);
            }
        }
    }

    public void UnparseAll(IndentWriter writer)
    {
        GroupKoto? currentGroup = this.IsRoot ? null : this;
        this.UnparseAllInternal(0, writer, false);
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
            group.Merge(state);
        }
    }

    private void Merge(TokenState state)
    {
    }

    private void UnparseAllInternal(int indents, IndentWriter writer, bool parentDeclared)
    {
        var groupDeclared = false;

        if (this.KotoList.Count > 0)
        {
            if (!this.IsRoot || this.Modifier != 0)
            {
                if (this.KotoKind == KotoKind.Group)
                {
                    this.UnparseToRoot(writer);
                    writer.WriteLine();
                    writer.SetIndent(1);
                }
                else
                {
                    this.UnparseTo(writer);
                    writer.WriteLine();
                    writer.IncrementIndent();
                }

                groupDeclared = true;
            }

            foreach (var x in this.KotoList)
            {
                x.UnparseTo(writer);
                writer.WriteLine();
            }

            writer.WriteLine();
        }

        var groups = this.IdentifierToGroupKoto.ToArray();
        if (groups.Length > 0)
        {
            // DeclareGroup(writer, ref currentGroup);

            foreach (var x in groups)
            {
                x.UnparseAllInternal(indents + 1, writer, groupDeclared);
            }
        }

        if (groupDeclared)
        {
            writer.DecrementIndent();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
    }
}
