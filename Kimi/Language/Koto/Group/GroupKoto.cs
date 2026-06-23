// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;

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
                group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(text, x => new GroupKoto());
                group.TrySetName(text);
            }

            var segment = text[..index];
            group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(segment, x => new GroupKoto());
            group.TrySetName(text);
            text = text[(index + 1)..];
        }
    }

    private void TrySetName(ReadOnlySpan<char> text)
    {
        if (string.IsNullOrEmpty(this.Name))
        {
            this.Name = text.ToString();
        }
    }
}
