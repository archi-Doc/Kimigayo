// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1202 // Keep source collection and extraction responsibilities together.

/// <summary>Documentation for one immutable source snapshot; never part of executable syntax.</summary>
public sealed class DocumentationSource
{
    private readonly List<DocumentationComment> comments = new();
    private bool hasParseErrors;

    internal DocumentationSource(SourceDocument source)
    {
        this.Source = source;
        this.Comments = this.comments.AsReadOnly();
    }

    /// <summary>Gets the original source snapshot.</summary>
    public SourceDocument Source { get; }

    /// <summary>Gets the logical source name, or null for generated source without a relative-link base.</summary>
    public string? LogicalName { get; internal set; }

    /// <summary>Gets the producing Mod ID, or null for ordinary source.</summary>
    public string? ModId { get; internal set; }

    /// <summary>Gets the generated source addition order within its Mod.</summary>
    public int AdditionOrder { get; internal set; }

    /// <summary>Gets source-ordered comments, including unassociated candidates.</summary>
    public IReadOnlyList<DocumentationComment> Comments { get; }

    internal void AddLine(int start, int end)
    {
        var text = this.Source.SourceText;
        var lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is not ('\r' or '\n'))
        {
            lineStart--;
        }

        var recognized = true;
        for (var i = lineStart; i < start; i++)
        {
            recognized &= text[i] == ' ';
        }

        var indent = start - lineStart;
        if (recognized && this.comments.Count > 0 && this.comments[^1] is { IsRecognized: true } previous && previous.Indent == indent)
        {
            var after = previous.Span.End;
            if (after < text.Length && text[after] == '\r')
            {
                after++;
            }

            if (after < text.Length && text[after] == '\n')
            {
                after++;
            }

            if (after == lineStart)
            {
                previous.Span = SourceSpan.FromBounds(previous.Span.Start, end);
                return;
            }
        }

        this.comments.Add(new(this, SourceSpan.FromBounds(start, end), indent, recognized));
    }

    internal void SetLocation(string projectDirectory, string? modId = null, int additionOrder = 0)
    {
        this.ModId = modId;
        this.AdditionOrder = additionOrder;
        if (modId is null)
        {
            var path = this.Source.Path;
            this.LogicalName = (Path.IsPathRooted(path) && projectDirectory.Length > 0 ? Path.GetRelativePath(projectDirectory, path) : path).Replace('\\', '/');
        }
    }

    internal void Merge(DocumentationSource nested)
    {
        // Interpolation expressions are lexed by the parser. Insert their
        // source-ordered ranges without retaining either token buffer.
        if (nested.comments.Count > 0)
        {
            foreach (var comment in nested.comments)
            {
                comment.Owner = this;
            }

            var index = this.FindBefore(nested.comments[0].Span.Start) + 1;
            this.comments.InsertRange(index, nested.comments);
        }
    }

    internal void Suppress(SourceSpan span, bool excluded = false)
    {
        var index = this.FindBefore(span.Start) + 1;
        for (; index < this.comments.Count && this.comments[index].Span.Start < span.End; index++)
        {
            var comment = this.comments[index];
            comment.IsSuppressed = true;
            if (excluded)
            {
                comment.IsSelected = false;
            }
        }
    }

    internal void Finish(bool parseErrors)
    {
        this.hasParseErrors |= parseErrors;
        if (parseErrors)
        {
            // A broken source snapshot has no reliable publication tree. Keep raw text for the editor.
            foreach (var comment in this.comments)
            {
                comment.Declaration = null;
            }
        }
    }

    internal void Exclude(int start, int syntaxEnd, int nextToken)
    {
        this.Suppress(SourceSpan.FromBounds(start, Math.Max(start, syntaxEnd)), excluded: true);
        // Layout tokens point at the next syntax. Include trailing comments only
        // inside the excluded indentation, preserving the following prelude.
        var baseline = BaseHelper.CountLeadingSpaces(this.Source.GetLineSpan(this.Source.GetPosition(start).Line));
        for (var i = this.FindBefore(syntaxEnd) + 1; i < this.comments.Count && this.comments[i].Span.Start < nextToken; i++)
        {
            if (this.comments[i].Indent > baseline)
            {
                this.comments[i].IsSuppressed = true;
                this.comments[i].IsSelected = false;
            }
        }
    }

    internal void Associate(Koto declaration, SourceSpan header, AttributeKoto? attributes, ReadOnlySpan<Token> tokens)
    {
        var headerStart = header.Start;
        var tokenIndex = 0;
        var high = tokens.Length;
        while (tokenIndex < high)
        {
            var middle = (tokenIndex + high) / 2;
            if (tokens[middle].Span.Start < headerStart)
            {
                tokenIndex = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        // Function parser spans start at the name. Recover the written header prefix.
        var prefix = tokenIndex - 1;
        while (prefix >= 0)
        {
            var token = tokens[prefix];
            var isPrefix = token.Kind is TokenKind.Func or TokenKind.Public or TokenKind.Private or TokenKind.Protected or TokenKind.Internal or TokenKind.Open;
            if (token.Kind == TokenKind.Identifier)
            {
                var spelling = this.Source.AsSpan().Slice(token.Span.Start, token.Length);
                isPrefix = spelling.SequenceEqual("unsafe") || spelling.SequenceEqual("specialize");
            }

            if (!isPrefix)
            {
                break;
            }

            headerStart = token.Span.Start;
            prefix--;
        }

        // Header/Attribute-interior comments cannot become a later declaration's prelude.
        this.Suppress(SourceSpan.FromBounds(headerStart, Math.Max(headerStart, header.End)));
        var candidateIndex = this.FindBefore(headerStart);
        while (candidateIndex >= 0 && (!this.comments[candidateIndex].IsRecognized || this.comments[candidateIndex].IsSuppressed))
        {
            candidateIndex--;
        }

        if (candidateIndex < 0)
        {
            return;
        }

        var candidate = this.comments[candidateIndex];
        if (candidate.Declaration is not null)
        {
            return;
        }

        var headerLine = this.Source.GetPosition(headerStart).Line;
        var indent = BaseHelper.CountLeadingSpaces(this.Source.GetLineSpan(headerLine));
        if (candidate.Indent != indent)
        {
            return;
        }

        for (var i = prefix; i >= 0 && tokens[i].Span.End > candidate.Span.End; i--)
        {
            var token = tokens[i];
            if (token.Kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock)
            {
                continue;
            }

            var inAttribute = false;
            for (var attribute = attributes; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (ReferenceEquals(attribute.CodeContext.SourceDocument, this.Source) && attribute.Span.Start <= token.Span.Start && attribute.Span.End >= token.Span.End)
                {
                    inAttribute = true;
                    break;
                }
            }

            if (!inAttribute)
            {
                return;
            }
        }

        candidate.Declaration = declaration;
        candidate.DeclarationSpan = SourceSpan.FromBounds(headerStart, Math.Max(header.End, headerStart));
    }

    /// <summary>Gets optional documentation diagnostics, independently of language diagnostics.</summary>
    /// <returns>The documentation diagnostics.</returns>
    public IEnumerable<DocumentationDiagnostic> GetDiagnostics()
    {
        if (this.hasParseErrors)
        {
            yield break;
        }

        foreach (var comment in this.comments)
        {
            if (comment.IsSuppressed || !comment.IsSelected)
            {
                continue;
            }

            if (!comment.IsRecognized || comment.Declaration is null)
            {
                yield return new(this.Source, comment.Span, comment.IsRecognized ? "UnattachedDocumentation" : "IgnoredDocumentation");
            }
        }
    }

    private int FindBefore(int offset)
    {
        var low = 0;
        var high = this.comments.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (this.comments[middle].Span.Start < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}

/// <summary>An optional documentation diagnostic; it cannot make a program invalid.</summary>
/// <param name="Source">The original source.</param>
/// <param name="Span">The original source range.</param>
/// <param name="Code">The stable diagnostic name.</param>
public sealed record DocumentationDiagnostic(SourceDocument Source, SourceSpan Span, string Code);

/// <summary>A source-backed block and its optional syntactic declaration association.</summary>
public sealed class DocumentationComment
{
    private DocumentationText? text;

    internal DocumentationComment(DocumentationSource owner, SourceSpan span, int indent, bool recognized)
    {
        this.Owner = owner;
        this.Source = owner.Source;
        this.Span = span;
        this.Indent = indent;
        this.IsRecognized = recognized;
    }

    /// <summary>Gets the immutable source snapshot.</summary>
    public SourceDocument Source { get; }

    /// <summary>Gets the original block range, excluding its final line ending.</summary>
    public SourceSpan Span { get; internal set; }

    /// <summary>Gets the associated declaration, or null.</summary>
    public Koto? Declaration { get; internal set; }

    /// <summary>Gets this fragment's header range, even when its Container is merged.</summary>
    public SourceSpan DeclarationSpan { get; internal set; }

    /// <summary>Gets a value indicating whether the containing syntax survives configuration selection.</summary>
    public bool IsSelected { get; internal set; } = true;

    /// <summary>Gets a value indicating whether this comment has documentation syntax.</summary>
    public bool IsRecognized { get; }

    internal int Indent { get; }

    internal DocumentationSource Owner { get; set; }

    internal bool IsSuppressed { get; set; }

    /// <summary>Extracts and caches normalized text and its source mapping on demand.</summary>
    /// <returns>The normalized text with source mappings.</returns>
    public DocumentationText GetText()
    {
        var existing = Volatile.Read(ref this.text);
        if (existing is not null)
        {
            return existing;
        }

        var builder = new StringBuilder(this.Span.Length);
        var sourceOffsets = new List<int>();
        var textOffsets = new List<int>();
        var source = this.Source.SourceText;
        var position = this.Span.Start;
        while (position <= this.Span.End)
        {
            if (sourceOffsets.Count > 0)
            {
                builder.Append('\n');
                position += this.Indent;
            }

            position += 3;
            if (position < this.Span.End && source[position] == ' ')
            {
                position++;
            }

            sourceOffsets.Add(position);
            textOffsets.Add(builder.Length);
            var end = position;
            while (end < this.Span.End && source[end] is not ('\r' or '\n'))
            {
                end++;
            }

            builder.Append(source.AsSpan(position, end - position));
            if (end == this.Span.End)
            {
                break;
            }

            position = end + (source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? 2 : 1);
        }

        var created = new DocumentationText(this.Source, builder.ToString(), textOffsets.ToArray(), sourceOffsets.ToArray());
        return Interlocked.CompareExchange(ref this.text, created, null) ?? created;
    }

    /// <summary>Writes canonical comment prefixes without changing the extracted text.</summary>
    /// <returns>The formatted comment block.</returns>
    public string Format()
    {
        var lines = this.GetText().Text.Split('\n');
        var prefix = new string(' ', this.Indent) + "///";
        return string.Join("\n", lines.Select(line => line.Length == 0 ? prefix : prefix + " " + line));
    }
}

/// <summary>Normalized Markdown and a mapping back to the original UTF-16 source positions.</summary>
public sealed class DocumentationText
{
    private readonly int[] textOffsets;
    private readonly int[] sourceOffsets;

    internal DocumentationText(SourceDocument source, string text, int[] textOffsets, int[] sourceOffsets)
    {
        this.Source = source;
        this.Text = text;
        this.textOffsets = textOffsets;
        this.sourceOffsets = sourceOffsets;
    }

    /// <summary>Gets the original source.</summary>
    public SourceDocument Source { get; }

    /// <summary>Gets the normalized Markdown.</summary>
    public string Text { get; }

    /// <summary>Maps a normalized UTF-16 offset, including EOF, to the original source.</summary>
    /// <param name="offset">The normalized offset.</param>
    /// <returns>The original source offset.</returns>
    public int GetSourceOffset(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, this.Text.Length);
        var line = Array.BinarySearch(this.textOffsets, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return this.sourceOffsets[line] + offset - this.textOffsets[line];
    }
}
