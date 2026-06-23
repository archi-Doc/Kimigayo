// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

public enum KotoKind : byte
{
    Namespace,
    Group,
    Struct,
    Contract,
    Comment,
}

[TinyhandUnion((int)KotoKind.Namespace, typeof(NamespaceKoto))]
[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]
[TinyhandUnion((int)KotoKind.Comment, typeof(CommentKoto))]
public abstract partial class Koto
{
    public Koto()
    {
    }

    public virtual void Parse(ref TokenReader reader)
    {
    }
}
