// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1600, SA1649 // Compact syntax model and allocation-free enumeration.

/// <summary>The node kinds of the limited documentation Markdown profile.</summary>
public enum DocumentationMarkdownKind : byte
{
    Document,
    Paragraph,
    Heading,
    BulletList,
    OrderedList,
    ListItem,
    Quote,
    CodeBlock,
    Text,
    Code,
    Emphasis,
    Strong,
    Link,
    AutoLink,
    SoftBreak,
    HardBreak,
}

/// <summary>
/// An immutable, eagerly completed Markdown snapshot. Parsing has no Markdig dependency.
/// The input is normalized documentation text (LF line endings); retained nodes borrow its storage.
/// </summary>
public sealed partial class DocumentationMarkdownDocument
{
    // Every inline marker plus the line ending: one scan both rejects markup and
    // splits lines. NUL is excluded because it selects a slower vectorized searcher;
    // ParseCore finds it separately and NUL-containing text takes the general parser.
    private static readonly SearchValues<char> PlainStops = SearchValues.Create("*_`[\\&<]\n");

    private readonly MarkdownNodeData[] nodes;

    private readonly MarkdownValue[] values;

    private readonly DocumentationText? source;

    internal DocumentationMarkdownDocument(string text, MarkdownNodeData[] nodes, MarkdownValue[] values, DocumentationText? source)
    {
        this.Text = text;
        this.nodes = nodes;
        this.values = values;
        this.source = source;
    }

    /// <summary>Gets the immutable normalized input.</summary>
    public string Text { get; }

    /// <summary>Gets the root of this snapshot.</summary>
    public DocumentationMarkdownNode Root => new(this, 0);

    /// <summary>Gets the first block only when it is a paragraph.</summary>
    public DocumentationMarkdownNode? Summary => this.Root.FirstChild is { Kind: DocumentationMarkdownKind.Paragraph } node ? node : null;

    /// <summary>Parses normalized text. A depth limit is an interruption, never a syntax fallback.</summary>
    /// <param name = "text">The normalized text; CR line endings must be normalized by the caller.</param>
    /// <param name = "cancellationToken">Cancellation before publishing any result.</param>
    /// <param name = "maximumDepth">Maximum tree depth, excluding the document root; default 256.</param>
    /// <returns>A completed snapshot safe for concurrent reads.</returns>
    public static DocumentationMarkdownDocument Parse(string text, CancellationToken cancellationToken = default, int maximumDepth = 256) => ParseCore(text, null, cancellationToken, maximumDepth);

    /// <summary>Parses a source-backed comment into an immutable documentation snapshot.</summary>
    /// <param name = "comment">The source-backed comment.</param>
    /// <param name = "cancellationToken">Cancellation before publication.</param>
    /// <param name = "maximumDepth">Maximum tree depth, excluding the document root.</param>
    /// <returns>A completed, source-mapped snapshot.</returns>
    public static DocumentationMarkdownDocument Parse(DocumentationComment comment, CancellationToken cancellationToken = default, int maximumDepth = 256)
    {
        ArgumentNullException.ThrowIfNull(comment);
        cancellationToken.ThrowIfCancellationRequested();
        var source = comment.GetText();
        return ParseCore(source.Text, source, cancellationToken, maximumDepth);
    }

    /// <summary>Maps a normalized range to its minimal enclosing original UTF-16 range.</summary>
    /// <param name = "span">A half-open normalized range.</param>
    /// <returns>The original range; identity mapping for text-only documents.</returns>
    public SourceSpan GetSourceSpan(SourceSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(span.Start);
        ArgumentOutOfRangeException.ThrowIfNegative(span.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, this.Text.Length);
        if (this.source is null)
        {
            return span;
        }

        var start = this.source.GetSourceOffset(span.Start);
        if (span.Length == 0)
        {
            return new(start, 0);
        }

        var end = this.source.GetSourceOffset(span.End - 1);
        var original = this.source.Source.SourceText;
        end += this.Text[span.End - 1] == '\n' && original[end] == '\r' && end + 1 < original.Length && original[end + 1] == '\n' ? 2 : 1;
        return SourceSpan.FromBounds(start, end);
    }

    internal ref readonly MarkdownNodeData GetData(int id) => ref this.nodes[id];

    internal ReadOnlySpan<char> GetText(in MarkdownNodeData node)
    {
        if (node.TextStart == int.MinValue)
        {
            // Decoded scalars occupy the node's otherwise unused argument word.
            // This read-only interior span keeps the immutable arena alive; no
            // temporary string, pooled storage, or node copy backs the result.
            ref var characters = ref Unsafe.As<int, char>(ref Unsafe.AsRef(in node.Argument));
            return MemoryMarshal.CreateReadOnlySpan(ref characters, node.TextLength);
        }

        return node.TextStart < 0 ? this.values[~node.TextStart].Text.AsSpan(0, node.TextLength) : this.Text.AsSpan(node.TextStart, node.TextLength);
    }

    // Destinations and titles that equal a source slice are materialized on first
    // use. A concurrent first use may create equal strings; either result is valid.
    internal string GetValue(int index)
    {
        ref var value = ref this.values[index];
        return value.Text ?? (value.Text = this.Text.Substring(value.Start, value.Length));
    }

    private static DocumentationMarkdownDocument ParseCore(string text, DocumentationText? source, CancellationToken cancellationToken, int maximumDepth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);
        cancellationToken.ThrowIfCancellationRequested();
        // One scan finds both the rejected CR and the rare NUL that later stages must handle.
        var hasNul = false;
        if (text.AsSpan().IndexOfAny('\r', '\0') >= 0)
        {
            if (text.Contains('\r'))
            {
                throw new ArgumentException("Documentation text must use normalized LF line endings.", nameof(text));
            }

            hasNul = true;
        }

        var plain = text.AsSpan().Trim(" \t");
        if (plain.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(text, [new() { Kind = DocumentationMarkdownKind.Document, End = text.Length }], [], source);
        }

        if (!hasNul && TryParsePlainParagraph(text, plain, maximumDepth, out var nodes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(text, nodes, [], source);
        }

        return ParseGeneral(text, source, cancellationToken, maximumDepth, hasNul);
    }

    // Kept out of ParseCore: the parser struct's stack frame and its try/finally
    // would otherwise be paid by the allocation-free plain-paragraph path too.
    [MethodImpl(MethodImplOptions.NoInlining)]
    [SkipLocalsInit]
    private static DocumentationMarkdownDocument ParseGeneral(string text, DocumentationText? source, CancellationToken cancellationToken, int maximumDepth, bool hasNul)
    {
        // Typical comments fit in this frame's scratch; only larger ones touch the pool.
        // The buffers expose only written elements, so the stack need not be zeroed.
        ValueScratch values = default;
        var scratch = new DocumentationMarkdownParser.Scratch(
            stackalloc MarkdownNodeData[64],
            stackalloc DocumentationMarkdownParser.NodeLinks[64],
            values,
            stackalloc DocumentationMarkdownParser.Container[8],
            stackalloc DocumentationMarkdownParser.ContentLine[16],
            stackalloc DocumentationMarkdownParser.Delimiter[16],
            stackalloc DocumentationMarkdownParser.Bracket[8],
            stackalloc DocumentationMarkdownParser.CodeRun[8]);
        // Not `using`: a read-only local would call Parse on a defensive copy.
        var parser = new DocumentationMarkdownParser(text, cancellationToken, maximumDepth, hasNul, in scratch);
        try
        {
            return parser.Parse(source);
        }
        finally
        {
            parser.Dispose();
        }
    }

    [InlineArray(4)]
    private struct ValueScratch
    {
        private MarkdownValue element;
    }

    // A single paragraph of plain text, possibly over several lines, needs no
    // mutable parser or pooled scratch: construct the immutable arena directly.
    // Conservatively defer every possible block opener, inline marker, blank
    // line and hard break to the general parser.
    [SkipLocalsInit]
    private static bool TryParsePlainParagraph(string text, ReadOnlySpan<char> plain, int maximumDepth, out MarkdownNodeData[] nodes)
    {
        nodes = null!;
        // Record every line's content bounds before allocating: the fill loop
        // then runs no vectorized search after the array store, which measured
        // several times slower than the same searches beforehand.
        const int MaximumLines = 24;
        Span<int> bounds = stackalloc int[MaximumLines * 3];
        var lineCount = 0;
        var offset = 0;
        while (true)
        {
            if (lineCount == MaximumLines)
            {
                return false;
            }

            var stop = plain[offset..].IndexOfAny(PlainStops);
            var lineEnd = stop < 0 ? plain.Length : offset + stop;
            if (lineEnd < plain.Length && plain[lineEnd] != '\n')
            {
                return false;
            }

            var newline = lineEnd < plain.Length ? stop : -1;
            var line = plain[offset..lineEnd];
            var content = line.IndexOfAnyExcept(' ', '\t');
            if (content < 0 || char.IsAsciiDigit(line[content]) || "#>-+~".Contains(line[content]))
            {
                return false;
            }

            var contentEnd = lineEnd;
            while (plain[contentEnd - 1] is ' ' or '\t')
            {
                contentEnd--;
            }

            if (newline >= 0 && lineEnd - contentEnd >= 2 && plain[lineEnd - 1] == ' ' && plain[lineEnd - 2] == ' ')
            {
                return false;
            }

            bounds[lineCount * 3] = offset + content;
            bounds[(lineCount * 3) + 1] = contentEnd;
            bounds[(lineCount * 3) + 2] = lineEnd;
            lineCount++;
            if (newline < 0)
            {
                break;
            }

            offset = lineEnd + 1;
        }

        if (maximumDepth < 2)
        {
            throw new DocumentationMarkdownLimitException(maximumDepth);
        }

        var start = text.Length - text.AsSpan().TrimStart(" \t").Length;
        nodes = new MarkdownNodeData[(2 * lineCount) + 1];
        nodes[0] = new() { Kind = DocumentationMarkdownKind.Document, End = text.Length, First = 1 };
        nodes[1] = new() { Kind = DocumentationMarkdownKind.Paragraph, Start = start, End = text.Length, First = 2 };
        for (var line = 0; line < lineCount; line++)
        {
            var id = 2 + (2 * line);
            var last = line == lineCount - 1;
            var contentStart = start + bounds[line * 3];
            var contentEnd = start + bounds[(line * 3) + 1];
            nodes[id] = new() { Kind = DocumentationMarkdownKind.Text, Parent = 1, Start = contentStart, End = contentEnd, TextStart = contentStart, TextLength = contentEnd - contentStart, Next = last ? 0 : id + 1 };
            if (!last)
            {
                var lf = start + bounds[(line * 3) + 2];
                nodes[id + 1] = new() { Kind = DocumentationMarkdownKind.SoftBreak, Parent = 1, Start = lf, End = lf + 1, Next = id + 2 };
            }
        }

        return true;
    }
}

/// <summary>A stable node identity: its owning snapshot and an arena index, without a node allocation.</summary>
public readonly record struct DocumentationMarkdownNode
{
    private readonly DocumentationMarkdownDocument document;

    private readonly int id;

    internal DocumentationMarkdownNode(DocumentationMarkdownDocument document, int id)
    {
        this.document = document;
        this.id = id;
    }

    public DocumentationMarkdownDocument Document => this.document;

    public int Id => this.id;

    public DocumentationMarkdownKind Kind => this.Data.Kind;

    public SourceSpan Span => SourceSpan.FromBounds(this.Data.Start, this.Data.End);

    public SourceSpan SourceSpan => this.document.GetSourceSpan(this.Span);

    /// <summary>Gets decoded leaf text, code text, or a code block's language. Containers have empty text.</summary>
    public ReadOnlySpan<char> Text => this.document.GetText(this.Data);

    public int HeadingLevel => this.Kind == DocumentationMarkdownKind.Heading ? this.Data.Argument : 0;

    public int StartNumber => this.Kind == DocumentationMarkdownKind.OrderedList ? this.Data.Argument : 0;

    public bool IsTight => (this.Data.Flags & MarkdownNodeFlags.Loose) == 0;

    public string? Destination => this.Kind is DocumentationMarkdownKind.Link or DocumentationMarkdownKind.AutoLink ? this.document.GetValue(this.Data.Argument) : null;

    public string? Title => this.Kind == DocumentationMarkdownKind.Link && (this.Data.Flags & MarkdownNodeFlags.HasTitle) != 0 ? this.document.GetValue(this.Data.Argument + 1) : null;

    public DocumentationMarkdownNode? Parent => this.id == 0 ? null : new(this.document, this.Data.Parent);

    public DocumentationMarkdownNode? FirstChild => this.Data.First == 0 ? null : new(this.document, this.Data.First);

    public DocumentationMarkdownNode? NextSibling => this.Data.Next == 0 ? null : new(this.document, this.Data.Next);

    public ChildrenEnumerable Children => new(this.document, this.Data.First);

    // The generated record formatter follows Parent/FirstChild recursively.
    // Keep diagnostics and debugger display bounded even for cyclic relations.
    public override string ToString() => this.document is null ? "DocumentationMarkdownNode (default)" : $"{this.Kind} #{this.id} {this.Span}";

    private ref readonly MarkdownNodeData Data => ref this.document.GetData(this.id);

    public readonly struct ChildrenEnumerable : IEnumerable<DocumentationMarkdownNode>
    {
        private readonly DocumentationMarkdownDocument document;

        private readonly int first;

        internal ChildrenEnumerable(DocumentationMarkdownDocument document, int first)
        {
            this.document = document;
            this.first = first;
        }

        public Enumerator GetEnumerator() => new(this.document, this.first);

        IEnumerator<DocumentationMarkdownNode> IEnumerable<DocumentationMarkdownNode>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public struct Enumerator : IEnumerator<DocumentationMarkdownNode>
    {
        private readonly DocumentationMarkdownDocument document;

        private int next;

        private int current;

        internal Enumerator(DocumentationMarkdownDocument document, int first)
        {
            this.document = document;
            this.next = first;
            this.current = 0;
        }

        public DocumentationMarkdownNode Current => new(this.document, this.current);

        object IEnumerator.Current => this.Current;

        public bool MoveNext()
        {
            this.current = this.next;
            if (this.current == 0)
            {
                return false;
            }

            this.next = this.document.GetData(this.current).Next;
            return true;
        }

        public void Dispose()
        {
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}

/// <summary>Reports an explicit resource limit without changing Markdown interpretation.</summary>
public sealed class DocumentationMarkdownLimitException(int maximumDepth) : OperationCanceledException($"Documentation Markdown exceeded the configured depth limit ({maximumDepth}).")
{
    public int MaximumDepth { get; } = maximumDepth;
}

[Flags]
internal enum MarkdownNodeFlags : byte
{
    None = 0,
    Loose = 1,
    HasTitle = 2,
    Detached = 4,
}

// A decoded string, or a source slice materialized only when a string is requested.
internal struct MarkdownValue
{
    internal string? Text;

    internal int Start;

    internal int Length;

    internal MarkdownValue(string text)
    {
        this.Text = text;
    }

    internal MarkdownValue(int start, int length)
    {
        this.Start = start;
        this.Length = length;
    }
}

// No object references per node, and only forward links: parent, first child and
// next sibling. Text is either a source slice or an index into the small
// decoded-value table (negative TextStart). 36 bytes per retained node.
internal struct MarkdownNodeData
{
    internal DocumentationMarkdownKind Kind;

    internal MarkdownNodeFlags Flags;

    internal int Start;

    internal int End;

    internal int TextStart;

    internal int TextLength;

    internal int Parent;

    internal int First;

    internal int Next;

    internal int Argument;
}
