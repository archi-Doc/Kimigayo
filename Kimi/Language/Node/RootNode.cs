// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
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

    public override void Read(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                reader.MoveNext();
                var value = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, reader);
                this.alias.Add(value);
                return;
            }
            else if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                reader.MoveNext();
                var value = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, reader);
                this.Namespace = value;
                return;
            }
        }

        base.Read(ref reader);
    }
}
