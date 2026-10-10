// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The key belongs to the program.

namespace Kimi.Compiler.Mir;

/// <summary>The key of a MIR body (R3 decision 6): its definition's symbol, which body of it and the index of a Closure or default
/// template (0 otherwise). R7's declaration table replaces the symbol.</summary>
/// <param name="Decl">The definition.</param>
/// <param name="Kind">The body of the definition.</param>
/// <param name="Index">The index of a Closure or default template.</param>
internal readonly record struct MirDefKey(BindingSymbol Decl, MirBodyKind Kind, int Index = 0);

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>. <see cref="Clear"/> returns every body to the pool and
/// <see cref="Begin"/> reuses them in order, so a warm rebuild of the same definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private MirBody[] pool = [];
    private int count;

    /// <summary>Gets the bodies in the order they were begun.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    internal MirBody? this[MirDefKey key] => this.bodies.TryGetValue(key, out var body) ? body : null;

    internal void Clear()
    {
        this.bodies.Clear();
        this.count = 0;
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
