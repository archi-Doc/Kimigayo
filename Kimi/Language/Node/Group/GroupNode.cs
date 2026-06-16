// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;

namespace Kimigayo.Language;

/// <summary>
/// group, struct, enum.
/// </summary>
public class GroupNode : Node
{
    #region FieldAndProperty

    private readonly Utf16Hashtable<GroupNode> identifierToGroupNode = new();

    #endregion

    public override void Read(ref TokenReader reader)
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

    public GroupNode GetOrAddGroup(ReadOnlySpan<char> qualifiedName)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                return group.identifierToGroupNode.GetOrAdd(text, x => new GroupNode());
            }

            var segment = text[..index];
            group = group.identifierToGroupNode.GetOrAdd(segment, x => new GroupNode());
            text = text[(index + 1)..];
        }
    }
}
