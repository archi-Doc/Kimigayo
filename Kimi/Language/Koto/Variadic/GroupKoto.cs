// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimigayo.Language;

/// <summary>
/// group, struct, enum.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : Koto
{
    #region FieldAndProperty

    [Key(1)]
    public string Name { get; protected set; } = string.Empty;

    [Key(2)]
    protected List<Koto> KotoList { get; set; } = [];

    private readonly Utf16Hashtable<Koto> identifierToGroupKoto = new();

    #endregion

    public GroupKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(FrontendMetadata compilationMetadata)
        : base(compilationMetadata)
    {
    }

    public void Add(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    public override string ToString()
        => $"Group: {this.Name}";

    public void Parse(ref TokenReader reader)
    {
        while (reader.TryRead(out var token))
        {
            if (token.Kind == TokenKind.At)
            {// #Attribute
            }
        }

        /*foreach (var x in tokens)
        {
            var code = NodeHelper.FromToken(x);
        }*/
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

    protected void Parse(ref Token token, ref TokenReader reader)
    {
        if (token.IsIdentifierToken(Constants.AliasKeyword))
        {// alias
            reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, KotoKind groupKind)
    {
        Func<string, Koto> factory = groupKind switch
        {
            KotoKind.Group => static x => new GroupKoto(new FrontendMetadata(default!, default, default!)),
            KotoKind.Namespace => static x => new NamespaceKoto(new FrontendMetadata(default!, default, default!)),
            _ => throw new InvalidOperationException(),
        };

        group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(text, factory);
        //group.Initialize(group, )
        if (string.IsNullOrEmpty(group.Name))
        {
            group.Name = text.ToString();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
    }
}
