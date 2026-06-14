// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class RootNode : GroupNode
{
    public UrlDiagnostic Diagnostic { get; }

    public string Namespace { get; private set; } = Constants.DefaultNamespace;

    public Node Current { get; private set; }

    private readonly HashSet<string> alias = new();

    public RootNode(UrlDiagnostic diagnostic)
    {
        this.Diagnostic = diagnostic;
        this.Current = this;
    }

    public override void Read(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count > 0)
        {
            if (tokens[0].IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                var value = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, tokens, 1);
                this.alias.Add(value);
                return;
            }
            else if (tokens[0].IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                var value = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, tokens, 1);
                this.Namespace = value;
                return;
            }
        }

        base.Read(tokens);
    }
}
