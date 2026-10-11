// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The key belongs to the program.

namespace Kimi.Compiler.Mir;

/// <summary>The key of a MIR body (R3 decision 6): its definition's symbol (a Closure's own, an accessor's the function its calls
/// target), which body of it and the index of a default template (0 otherwise), so that a callee's symbol and its declaration name its
/// body. A module's top-level statements (SPEC 22.2.1) have no symbol; their index is the module's ordinal. R7's declaration table
/// replaces the symbol.</summary>
/// <param name="Decl">The definition, or null for a module's top-level statements.</param>
/// <param name="Kind">The body of the definition.</param>
/// <param name="Index">The index of a default template, or the module of top-level statements.</param>
internal readonly record struct MirDefKey(BindingSymbol? Decl, MirBodyKind Kind, int Index = 0);

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>, with the records their construction and validation made.
/// <see cref="Clear"/> returns every body to the pool and <see cref="Begin"/> reuses them in order, so a warm rebuild of the same
/// definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private readonly HashSet<SourceRef> reported = new();
    private MirBody[] pool = [];
    private int count;

    /// <summary>Gets the bodies in the order they were begun.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    /// <summary>Gets or sets the Binding pass the bodies were built from (0: none).</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets the located Unsupported records of construction, one per location (plan 6.2), which the <c>mir</c> pipeline
    /// publishes as <c>Unsupported_Kd</c>.</summary>
    internal List<SourceRef> Unsupported { get; } = [];

    /// <summary>Gets the validator's findings, which the <c>mir</c> pipeline publishes as <c>InternalInvariant_Kd</c>.</summary>
    internal List<MirFinding> Findings { get; } = [];

    internal MirBody? this[MirDefKey key] => this.bodies.TryGetValue(key, out var body) ? body : null;

    internal void Clear()
    {
        this.bodies.Clear();
        this.reported.Clear();
        this.Unsupported.Clear();
        this.Findings.Clear();
        this.count = 0;
    }

    /// <summary>Records an Unsupported form at its location, once however many bodies meet it.</summary>
    /// <param name="site">The form's syntax.</param>
    internal void Report(SourceRef site)
    {
        if (this.reported.Add(site))
        {
            this.Unsupported.Add(site);
        }
    }

    /// <summary>Begins the body of a definition with the next pooled body.</summary>
    /// <param name="key">The definition, which has no body yet.</param>
    /// <param name="source">The body's syntax.</param>
    /// <returns>The empty body.</returns>
    internal MirBody Begin(MirDefKey key, SourceRef source)
    {
        if (this.count == this.pool.Length)
        {
            Array.Resize(ref this.pool, Math.Max(16, this.count * 2));
        }

        var body = this.pool[this.count] ??= new();
        this.count++;
        body.Reset(key, source);
        this.bodies.Add(key, body);
        return body;
    }
}
