// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimigayo.Language;

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
/// group, struct, enum.
/// </summary>
[TinyhandObject]
public abstract partial class GroupKoto : IdentifiableKoto
{
    #region FieldAndProperty

    [Key(2)]
    public string Name { get; protected set; } = string.Empty;

    [Key(3)]
    protected List<Koto> KotoList { get; set; } = [];

    private readonly Utf16Hashtable<Koto> identifierToGroupKoto = new();

    public KotoKind Kind => this switch
    {
        NamespaceKoto => KotoKind.Namespace,
        StructKoto => KotoKind.Struct,
        // NamespaceKoto => KotoKind.Namespace,
        _ => KotoKind.Invalid,
    };

    #endregion

    public GroupKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }

    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    public void Add(Koto koto)
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

        return this.Kind switch
        {
            KotoKind.Namespace => $"namespace {this.Name}",
            KotoKind.Struct => $"struct {this.Name}",
            _ => string.Empty,
        };
    }

    public void Parse(ref Token token, ref TokenReader reader)
    {
        while (true)
        {
            GroupKoto? nextGroup = default;

            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                _ = KotoHelper.ValidateAndGetNamespace2(ref reader);
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.Kind == TokenKind.EndBlock)
            {
                // reader.Clear();
                return;
            }
            else if (token.Kind == TokenKind.Let ||
                token.Kind == TokenKind.Var)
            {// let a = 1, var b = 2
                // KotoHelper.ParseVariable(this, ref reader, ref token);
            }
            else if (token.Kind == TokenKind.Namespace)
            {// namespace
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(ref reader);
                nextGroup = this.GetOrAddGroup(qualifiedName, KotoKind.Namespace);
            }
            else if (token.Kind == TokenKind.Struct)
            {// struct
                var name = KotoHelper.ValidateAndGetGroupName(ref reader);
                var group = this.GetOrAddGroup(name, KotoKind.Struct);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextGroup = group;
                }
            }

            // Consume Attribute and modifiers
            _ = KotoParser.ConsumeTriviaAndRead(ref reader, out token);
            if (!token.IsValid)
            {
                return;
            }

            if (nextGroup is not null)
            {
                nextGroup.Parse(ref token, ref reader);
            }

            // this.Parse(ref token, ref reader);
        }
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, KotoKind groupKind)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text, groupKind);
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment, groupKind);
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, KotoKind groupKind)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, Koto> factory = groupKind switch
        {
            KotoKind.Namespace => x => new NamespaceKoto(codeContext),
            KotoKind.Struct => x => new StructKoto(codeContext),
            _ => throw new InvalidOperationException(),
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
