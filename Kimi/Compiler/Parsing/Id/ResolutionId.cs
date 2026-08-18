// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

public readonly record struct ResolutionId
{
    public readonly uint KotonohaId;

    public readonly ulong KotoId;

    public ResolutionId(uint kotonohaId, ulong kotoId)
    {
        this.KotonohaId = kotonohaId;
        this.KotoId = kotoId;
    }
}

[TinyhandObject]
public readonly partial record struct Resolution
{
    [Key(0)]
    public readonly uint KotonohaId;

    [Key(1)]
    public readonly ulong KotoId;

    public readonly Koto? Koto;

    public Resolution(uint kotonohaId, ulong kotoId)
    {
        this.KotonohaId = kotonohaId;
        this.KotoId = kotoId;
    }
}
