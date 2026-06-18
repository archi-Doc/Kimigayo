// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public abstract class Koto
{
    [IgnoreMember]
    public FileRoot RootNode { get; protected set; }

    public Koto(FileRoot rootNode)
    {
        this.RootNode = rootNode;
    }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
