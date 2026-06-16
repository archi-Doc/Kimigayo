// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public abstract class Node
{
    public RootNode RootNode { get; protected set; }

    public StatementContext StatementContext { get; }

    public UrlDiagnostic Diagnostic => this.RootNode.Diagnostic;

    public Node(RootNode rootNode)
    {
        this.RootNode = rootNode;
    }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
