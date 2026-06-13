// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

/// <summary>
/// namespace, group, struct, enum.
/// </summary>
public class GroupNode : Node
{
    public void Read(IEnumerable<Token> tokens)
    {
        foreach (var x in tokens)
        {
            var code = NodeHelper.FromToken(x);
        }
    }
}
