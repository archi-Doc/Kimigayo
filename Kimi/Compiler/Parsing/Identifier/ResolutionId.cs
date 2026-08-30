// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Identifies a Koto node within a source unit.
/// </summary>
public readonly record struct ResolutionId
{
    /// <summary>The source unit identifier.</summary>
    public readonly uint KotonohaId;

    /// <summary>The Koto node identifier.</summary>
    public readonly ulong KotoId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolutionId"/> struct.
    /// </summary>
    /// <param name="kotonohaId">The source unit identifier.</param>
    /// <param name="kotoId">The Koto node identifier.</param>
    public ResolutionId(uint kotonohaId, ulong kotoId)
    {
        this.KotonohaId = kotonohaId;
        this.KotoId = kotoId;
    }
}

/// <summary>
/// Stores a Koto identifier and its resolved node.
/// </summary>
[TinyhandObject]
public readonly partial record struct Resolution
{
    /// <summary>The source unit identifier.</summary>
    [Key(0)]
    public readonly uint KotonohaId;

    /// <summary>The Koto node identifier.</summary>
    [Key(1)]
    public readonly ulong KotoId;

    /// <summary>The resolved Koto node, if available.</summary>
    public readonly Koto? Koto;

    /// <summary>
    /// Initializes a new instance of the <see cref="Resolution"/> struct.
    /// </summary>
    /// <param name="kotonohaId">The source unit identifier.</param>
    /// <param name="kotoId">The Koto node identifier.</param>
    public Resolution(uint kotonohaId, ulong kotoId)
    {
        this.KotonohaId = kotonohaId;
        this.KotoId = kotoId;
    }
}
