// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Diagnostics;

namespace Kimi.Lsp;

#pragma warning disable SA1201, SA1202, SA1204, SA1402, SA1600, SA1649 // Internal state-owner vocabulary.

internal readonly record struct HoverParticipant(UnitKey Key, long Generation, HoverDocument Document);

internal readonly record struct HoverAnswer(string? Body, SourceRange Range, string? Reason = null);

// One open document's last adopted immutable set, with no reference to a unit result, compilation or another file's index.
internal sealed class HoverState(HoverParticipant[] participants, bool markdown)
{
    private readonly Dictionary<int, Cached> cache = new();
    private readonly List<Edit> edits = [];
    private int characters;

    internal ReadOnlySpan<HoverParticipant> Participants => participants;

    internal bool Current { get; set; } = true;

    internal int CachedCount => this.cache.Count;

    internal int CachedCharacters => this.characters;

    internal void Revalidated()
    {
        this.Current = true;
        this.edits.Clear();
    }

    internal bool Edited(int start, int end, int inserted)
    {
        if (this.edits.Count == HoverLimits.Edits)
        {
            return false;
        }

        this.Current = false;
        this.edits.Add(new(start, end, inserted));
        return true;
    }

    internal HoverAnswer Find(TextDocument text, SourcePosition position)
    {
        try
        {
            return this.FindCore(text, position);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or PendingInputException))
        {
            return new(null, default, "Hover request failed: " + ex.Message);
        }
    }

    private HoverAnswer FindCore(TextDocument text, SourcePosition position)
    {
        if (!text.TryGetOffset(position, out var offset))
        {
            return default;
        }

        for (var i = this.edits.Count - 1; i >= 0; i--)
        {
            var edit = this.edits[i];
            if (offset >= edit.Start + edit.Inserted)
            {
                offset += edit.End - edit.Start - edit.Inserted;
            }
            else if (offset >= edit.Start)
            {
                return default;
            }
        }

        var first = participants[0].Document;
        var index = first.Find(offset);
        var key = index >= 0 ? index : ~offset;
        SourceRange range = default;
        if (index >= 0)
        {
            var original = first.Entries[index].Span;
            var start = original.Start;
            var end = original.End;
            foreach (var edit in this.edits)
            {
                // Inclusive boundary contact invalidates continuity, even for an identical replacement or empty insertion.
                if (edit.Start <= end && edit.End >= start)
                {
                    return default;
                }

                if (edit.End < start)
                {
                    var delta = edit.Inserted - (edit.End - edit.Start);
                    start += delta;
                    end += delta;
                }
            }

            if (end > text.Length || !text.TextEquals(start, first.Source.AsSpan().Slice(original.Start, original.Length)))
            {
                return default;
            }

            range = new(text.GetPosition(start), text.GetPosition(end));
        }

        var found = this.cache.TryGetValue(key, out var cached);
        string? reason = null;
        var cacheable = true;
        var retained = found ? cached!.Characters : 0;
        if (!found)
        {
            var rendered = this.Render(index, offset);
            cached = new(rendered.Body, rendered.Reason);
            reason = rendered.Reason;
            cacheable = rendered.Cacheable;
        }

        var body = this.Body(cached!);
        if (cacheable && (!found || retained != cached!.Characters))
        {
            this.cache.Remove(key);
            this.characters -= retained;
            this.MakeRoom(cached!.Characters);
            this.cache.Add(key, cached);
            this.characters += cached.Characters;
        }

        // Deterministic limits are logged on first use; transient failures remain retryable.
        return new(body, range, reason);
    }

    private HoverRendering Render(int index, int offset)
    {
        try
        {
            if (index < 0)
            {
                return new(null);
            }

            var entry = participants[0].Document.Entries[index];
            var agreement = participants.Length > 1 ? new HoverAgreement() : null;
            for (var i = 1; i < participants.Length; i++)
            {
                var other = participants[i].Document;
                var otherIndex = other.Find(offset);
                if (otherIndex < 0 || !agreement!.Equal(entry, other.Entries[otherIndex]))
                {
                    return new(null);
                }
            }

            return HoverRenderer.Render(entry.Info, markdown);
        }
        catch (HoverLimitException ex)
        {
            return new(null, ex.Message);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or PendingInputException))
        {
            return new(null, "Hover request failed: " + ex.Message, false);
        }
    }

    private string? Body(Cached cached)
    {
        if (this.Current || cached.Body is null)
        {
            return cached.Body;
        }

        if (cached.Previous is null)
        {
            cached.Previous = HoverRenderer.PreviousNotice + "\n\n" + cached.Body;
        }

        return cached.Previous;
    }

    private void MakeRoom(int length)
    {
        if (this.cache.Count == HoverLimits.CacheEntries || length > HoverLimits.CacheCharacters - this.characters)
        {
            this.cache.Clear();
            this.characters = 0;
        }
    }

    private readonly record struct Edit(int Start, int End, int Inserted);

    private sealed class Cached(string? body, string? reason)
    {
        internal string? Body { get; } = body;

        internal string? Reason { get; } = reason;

        internal int Characters => (this.Body?.Length ?? 0) + (this.Previous?.Length ?? 0) + (this.Reason?.Length ?? 0);

        internal string? Previous { get; set; }
    }
}
