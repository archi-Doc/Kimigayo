// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public enum KotoKind : byte
{
    Unresolved,

    // Group
    Namespace,
    Group,
    Struct,
    Contract,

    Comment,
    Literal,

    // Condition
    ConditionUnresolved,
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
[TinyhandUnion((int)KotoKind.Literal, typeof(LiteralKoto))]

[TinyhandUnion((int)KotoKind.ConditionNegate, typeof(ConditionNegateKoto))]
[TinyhandUnion((int)KotoKind.ConditionEquals, typeof(ConditionEqualsKoto))]
[TinyhandUnion((int)KotoKind.ConditionNotEquals, typeof(ConditionNotEqualsKoto))]
[TinyhandUnion((int)KotoKind.ConditionAnd, typeof(ConditionAndKoto))]
[TinyhandUnion((int)KotoKind.ConditionOr, typeof(ConditionOrKoto))]
public abstract partial class Koto
{
    [IgnoreMember]
    private DiagnosticSource? diagnosticSource;

    [IgnoreMember]
    private Koto? parent;

    public Koto()
    {
    }

    public virtual void Parse(ref TokenReader reader)
    {
    }

    public virtual Koto? ResolveIdentifier(ReadOnlySpan<char> identifier)
    {
        return default;
    }

    public void InitializeDiagnostic(DiagnosticCollection diagnosticCollection, Token token)
    {
        this.diagnosticSource = new(diagnosticCollection, token.Range);
    }

    public void AddDiagnostic(ulong diagnosticHash, object? obj = null)
    {
        if (this.diagnosticSource is not null)
        {
            this.diagnosticSource.DiagnosticCollection.Add(this.diagnosticSource.Range, diagnosticHash, obj);
        }
    }
}
