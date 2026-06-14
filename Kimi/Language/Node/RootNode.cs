// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

public sealed class RootNode : GroupNode
{
    public string Namespace { get; private set; } = "Playground";

    public Node Current { get; private set; }

    private readonly HashSet<string> alias = new();

    public RootNode(Project project)
    {
        this.Current = this;
    }

    public override void Read(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count > 0)
        {
            if (tokens[0].IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                var value = NodeHelper.ValidateAndGetNamespace(tokens, 1);
                this.alias.Add(value);
                return;
            }
            else if (tokens[0].IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                var value = NodeHelper.ValidateAndGetNamespace(tokens, 1);
                this.Namespace = value;
                return;
            }
        }

        base.Read(tokens);
    }
}
