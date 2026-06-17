// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public abstract class Node
{
    public FileRoot RootNode { get; protected set; }

    public StatementContext StatementContext { get; }

    public DiagnosticCollection Diagnostic => this.RootNode.Diagnostic;

    public Node(FileRoot rootNode)
    {
        this.RootNode = rootNode;
    }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
