// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

public abstract class Node
{
    public RootNode RootNode { get; }

    public StatementContext StatementContext { get; }

    public Node(RootNode rootNode)
    {
        this.RootNode = rootNode;
    }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
