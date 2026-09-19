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
    private static readonly SearchValues<char> InlineSyntax = SearchValues.Create("*_`[\\&\n\0<]");

    private readonly MarkdownNodeData[] nodes;

    private readonly string[] values;

    private readonly DocumentationText? source;

    internal DocumentationMarkdownDocument(string text, MarkdownNodeData[] nodes, string[] values, DocumentationText? source)
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

    /// <summary>Parses a source-backed comment without changing its existing Markdig processing path.</summary>
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

        return node.TextStart < 0 ? this.values[~node.TextStart].AsSpan(0, node.TextLength) : this.Text.AsSpan(node.TextStart, node.TextLength);
    }

    internal string GetValue(int index) => this.values[index];

    private static DocumentationMarkdownDocument ParseCore(string text, DocumentationText? source, CancellationToken cancellationToken, int maximumDepth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Contains('\r'))
        {
            throw new ArgumentException("Documentation text must use normalized LF line endings.", nameof(text));
        }

        var plain = text.AsSpan().Trim(" \t");
        if (plain.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(text, [new() { Kind = DocumentationMarkdownKind.Document, End = text.Length }], [], source);
        }

        // A single plain paragraph needs no mutable parser or pooled scratch.
        // Conservatively defer every possible block opener and inline marker.
        if (!char.IsAsciiDigit(plain[0]) && !"#>-+~".Contains(plain[0]) && plain.IndexOfAny(InlineSyntax) < 0)
        {
            if (maximumDepth < 2)
            {
                throw new DocumentationMarkdownLimitException(maximumDepth);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var start = text.Length - text.AsSpan().TrimStart(" \t").Length;
            var end = start + plain.Length;
            MarkdownNodeData[] nodes =
            [
                new() { Kind = DocumentationMarkdownKind.Document, End = text.Length, First = 1, Last = 1 },
                new() { Kind = DocumentationMarkdownKind.Paragraph, Start = start, End = text.Length, First = 2, Last = 2 },
                new() { Kind = DocumentationMarkdownKind.Text, Parent = 1, Start = start, End = end, TextStart = start, TextLength = plain.Length },
            ];
            return new(text, nodes, [], source);
        }

        using var parser = new DocumentationMarkdownParser(text, cancellationToken, maximumDepth);
        return parser.Parse(source);
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

// No object references per node. Text is either a source slice or an index into the
// small decoded-value table (negative TextStart). Sibling links make delimiter edits O(1).
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

    internal int Last;

    internal int Previous;

    internal int Next;

    internal int Argument;
}
