// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

public abstract class Node
{
    public StatementContext StatementContext { get; }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
