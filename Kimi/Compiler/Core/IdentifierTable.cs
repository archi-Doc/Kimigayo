// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;

namespace Kimi.Compiler;

/// <summary>
/// Interns identifier spellings so that every occurrence of a name shares one string instance.
/// </summary>
/// <remarks>
/// Lookups are lock-free and allocation-free; only the insertion of a new spelling takes a lock.
/// The table is an open-addressing hash set whose slots are published atomically, and a
/// reader that misses a concurrently inserted entry simply falls through to the locked path.
/// </remarks>
internal sealed class IdentifierTable
{
    private const int InitialCapacity = 256;
    private const int MaxCachedLength = 64;

    private readonly Lock writeLock = new();
    private string?[] slots = new string?[InitialCapacity];
    private int count;

    // Most compilations contain only valid spellings. Store only the exceptions so that
    // caching validation does not enlarge every hash-table slot or allocate a second table.
    private ConcurrentDictionary<string, byte>? invalidIdentifiers;

    /// <summary>
    /// Returns the shared string instance for the specified text, creating it on first use.
    /// </summary>
    /// <param name="text">The identifier text.</param>
    /// <returns>A string equal to <paramref name="text"/>.</returns>
    public string Intern(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        if (text.Length > MaxCachedLength)
        {
            return text.ToString();
        }

        var hash = Hash(text);
        var slots = Volatile.Read(ref this.slots);
        var mask = slots.Length - 1;
        for (var index = hash & mask; ; index = (index + 1) & mask)
        {
            var candidate = Volatile.Read(ref slots[index]);
            if (candidate is null)
            {
                break;
            }

            if (candidate.Length == text.Length && text.SequenceEqual(candidate))
            {
                return candidate;
            }
        }

        return this.Add(text, hash);
    }

    /// <summary>Interns a valid identifier, reusing validation cached with its spelling.</summary>
    /// <param name="text">The identifier spelling.</param>
    /// <param name="identifier">The shared spelling when validation succeeds.</param>
    /// <returns>Whether the spelling is a valid identifier.</returns>
    public bool TryGetIdentifier(ReadOnlySpan<char> text, [NotNullWhen(true)] out string? identifier)
    {
        if (text.IsEmpty || text.Length > MaxCachedLength)
        {
            identifier = IdentifierHelper.IsValidIdentifier(text) ? text.ToString() : null;
            return identifier is not null;
        }

        var spelling = this.Intern(text);
        identifier = Volatile.Read(ref this.invalidIdentifiers)?.ContainsKey(spelling) == true ? null : spelling;
        return identifier is not null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(ReadOnlySpan<char> text)
    {
        // FNV-1a over UTF-16 code units; identifiers are short, so this beats a randomized hash.
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash ^ (hash >> 15));
    }

    private string Add(ReadOnlySpan<char> text, int hash)
    {
        lock (this.writeLock)
        {
            var slots = this.slots;
            if ((this.count + 1) * 2 > slots.Length)
            {
                slots = this.Grow();
            }

            var mask = slots.Length - 1;
            var index = hash & mask;
            while (true)
            {
                var candidate = slots[index];
                if (candidate is null)
                {
                    var created = text.ToString();
                    if (!IdentifierHelper.IsValidIdentifier(text))
                    {
                        var invalid = this.invalidIdentifiers;
                        if (invalid is null)
                        {
                            invalid = new(StringComparer.Ordinal);
                            Volatile.Write(ref this.invalidIdentifiers, invalid);
                        }

                        invalid.TryAdd(created, 0);
                    }

                    // Publish after validation so readers cannot accept an invalid spelling.
                    Volatile.Write(ref slots[index], created);
                    this.count++;
                    return created;
                }

                if (candidate.Length == text.Length && text.SequenceEqual(candidate))
                {
                    return candidate;
                }

                index = (index + 1) & mask;
            }
        }
    }

    private string?[] Grow()
    {
        var previous = this.slots;
        var larger = new string?[previous.Length * 2];
        var mask = larger.Length - 1;
        foreach (var existing in previous)
        {
            if (existing is null)
            {
                continue;
            }

            var index = Hash(existing) & mask;
            while (larger[index] is not null)
            {
                index = (index + 1) & mask;
            }

            larger[index] = existing;
        }

        Volatile.Write(ref this.slots, larger);
        return larger;
    }
}
