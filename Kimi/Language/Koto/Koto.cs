// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

public enum KotoKind : byte
{
    Unresolved,

    Namespace,
    Group,
    Struct,
    Contract,
    Comment,

    // Condition
    ConditionUnresolved,
    ConditionLiteral,
    ConditionNegate,
    ConditionEquals,
    ConditionNotEquals,
    ConditionAnd,
    ConditionOr,
}

[TinyhandUnion((int)KotoKind.Unresolved, typeof(UnresolvedKoto))]
[TinyhandUnion((int)KotoKind.Namespace, typeof(NamespaceKoto))]
[TinyhandUnion((int)KotoKind.Group, typeof(GroupKoto))]
[TinyhandUnion((int)KotoKind.Comment, typeof(CommentKoto))]

[TinyhandUnion((int)KotoKind.ConditionUnresolved, typeof(ConditionUnresolvedKoto))]
[TinyhandUnion((int)KotoKind.ConditionLiteral, typeof(ConditionLiteralKoto))]
[TinyhandUnion((int)KotoKind.ConditionNegate, typeof(ConditionNegateKoto))]
[TinyhandUnion((int)KotoKind.ConditionEquals, typeof(ConditionEqualsKoto))]
[TinyhandUnion((int)KotoKind.ConditionNotEquals, typeof(ConditionNotEqualsKoto))]
[TinyhandUnion((int)KotoKind.ConditionAnd, typeof(ConditionAndKoto))]
[TinyhandUnion((int)KotoKind.ConditionOr, typeof(ConditionOrKoto))]
public abstract partial class Koto
{
    public Koto()
    {
    }

    public virtual void Parse(ref TokenReader reader)
    {
    }
}
