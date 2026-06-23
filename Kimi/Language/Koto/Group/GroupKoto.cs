// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;
using static System.Net.Mime.MediaTypeNames;

namespace Kimigayo.Language;

/// <summary>
/// group, struct, enum.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : Koto
{
    #region FieldAndProperty

    [Key(0)]
    public string Name { get; protected set; } = string.Empty;

    private readonly Utf16Hashtable<Koto> identifierToGroupKoto = new();

    #endregion

    public GroupKoto()
    {
    }

    public override string ToString()
        => $"Group: {this.Name}";

    public override void Parse(ref TokenReader reader)
    {
        while (reader.TryRead(out var token))
        {
            if (token.Kind == TokenKind.Sharp)
            {// #Attribute
            }
        }

        /*foreach (var x in tokens)
        {
            var code = NodeHelper.FromToken(x);
        }*/
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text);
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment);
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text)
    {
        group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(text, x => new GroupKoto());
        if (string.IsNullOrEmpty(group.Name))
        {
            group.Name = text.ToString();
        }
    }
}
