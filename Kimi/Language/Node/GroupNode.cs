// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

/// <summary>
/// namespace, group, struct, enum.
/// </summary>
public class GroupNode : Node
{
    public virtual void Read(TokenReader reader)
    {
        /*foreach (var x in tokens)
        {
            var code = NodeHelper.FromToken(x);
        }*/
    }
}
