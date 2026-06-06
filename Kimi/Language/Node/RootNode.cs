// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

public sealed class RootNode : GroupNode
{
    public Node Current { get; private set; }

    private List<Node> list = new();

    public RootNode(Project project)
    {
        this.Current = this;
    }

    public void Read(List<Token> list, int count)
    {
        foreach (var x in list)
        {
            var code = NodeHelper.FromToken(x);
        }
    }
}
