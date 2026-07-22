// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

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

    public IdentifiableKoto(CodeContext codeContext)
        : base(codeContext)
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
    public static readonly TokenState DefaultState = new(default, ModifierKind.Public);

    #region FieldAndProperty

    [Key(2)]
    public ModifierKind Modifier { get; private set; }

    [Key(3)]
    public string Name { get; protected set; } = string.Empty;

    [Key(4)]
    protected List<Koto> KotoList { get; set; } = [];

    private readonly Utf16Hashtable<GroupKoto> identifierToGroupKoto = new();

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

    internal GroupKoto(CodeContext codeContext, TokenState state)
        : base(codeContext)
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

    public override void UnparseTo(StringWriter writer)
    {// public group A
        KotoParser.UnparseAttribute(this.AttributeChain, writer);
        writer.WriteLine();

        if (this.IsRoot)
        {
            writer.Write("Root");
            return;
        }

        this.Modifier.WriteTo(writer, true);
        writer.Write(this.TokenKind.ToText());
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
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group, state);
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
                var structKoto = (StructKoto)this.GetOrAddGroup(r.Name, token.Kind, state);
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

    public void UnparseAll(StringWriter writer)
    {//
        if (!this.IsRoot)
        {
            this.UnparseTo(writer);
            writer.WriteLine();
        }

        if (this.KotoList.Count > 0)
        {
            foreach (var x in this.KotoList)
            {
                x.UnparseTo(writer);
                writer.WriteLine();
            }

            writer.WriteLine();
        }

        var groups = this.identifierToGroupKoto.ToArray();
        foreach (var x in groups)
        {
            x.UnparseAll(writer);
        }
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenState state)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text, kind, state);
                state = DefaultState;
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment, TokenKind.Group, state);
            state = DefaultState;
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind, TokenState state)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, GroupKoto> factory = kind switch
        {
            TokenKind.Struct => x => new StructKoto(codeContext, state),
            TokenKind.Enum => x => new EnumKoto(codeContext, state),
            TokenKind.Extension => x => new ExtensionKoto(codeContext, state),
            TokenKind.Contract => x => new ContractKoto(codeContext, state),
            _ => x => new GroupKoto(codeContext, state),
        };

        group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(text, factory);
        if (string.IsNullOrEmpty(group.Name))
        {
            group.Parent = parent;
            group.Name = text.ToString();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
    }
}
