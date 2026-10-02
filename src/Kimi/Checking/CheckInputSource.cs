// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;

namespace Kimi.Checking;

/// <summary>Supplies every file input of a check (SPEC 23.3.4): project and lock files, source files and directory listings.</summary>
/// <remarks>
/// Commands read the disk. The language server supplies open documents in place of their files and records each read.
/// Failures surface as the exceptions the disk would raise, so the command paths report them unchanged.
/// </remarks>
internal abstract class CheckInputSource
{
    /// <summary>Gets the source that reads the disk directly.</summary>
    public static CheckInputSource Disk { get; } = new DiskInputSource();

    /// <summary>Reads a whole project or lock file.</summary>
    /// <param name="path">The full path.</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="IOException">The file is missing or unreadable.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public abstract byte[] ReadAllBytes(string path);

    /// <summary>Reads a source file.</summary>
    /// <param name="path">The full path.</param>
    /// <returns>The source content.</returns>
    /// <exception cref="IOException">The file is missing or unreadable.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public abstract SourceContent ReadSource(string path);

    /// <summary>Lists the files of one directory that match a pattern.</summary>
    /// <param name="directory">The full directory path.</param>
    /// <param name="pattern">The file pattern, such as <c>*.kimi</c>.</param>
    /// <returns>Full paths in ordinal order.</returns>
    public abstract string[] GetFiles(string directory, string pattern);

    private sealed class DiskInputSource : CheckInputSource
    {
        public override byte[] ReadAllBytes(string path)
            => File.ReadAllBytes(path);

        public override SourceContent ReadSource(string path)
            => SourceContent.FromBytes(File.ReadAllBytes(path));

        public override string[] GetFiles(string directory, string pattern)
        {
            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}

/// <summary>The content of one source input: disk bytes, or the text of an open document with its file's BOM.</summary>
/// <remarks>Text is kept as text, so an open document is never encoded or decoded unless a byte comparison needs it.</remarks>
internal sealed class SourceContent
{
    // Byte identity only; the tokenizer reports unencodable text, so replacement here never hides it.
    private static readonly UTF8Encoding Utf8 = new(false, false);
    private byte[]? bytes;

    private SourceContent(byte[]? bytes, string? text, bool hasBom)
    {
        this.bytes = bytes;
        this.Text = text;
        this.HasBom = hasBom;
    }

    /// <summary>Gets the document text, or null for disk bytes.</summary>
    public string? Text { get; }

    /// <summary>Gets a value indicating whether the bytes begin, or the file of a document begins, with a UTF-8 BOM.</summary>
    public bool HasBom { get; }

    /// <summary>Creates content from disk bytes.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The content.</returns>
    public static SourceContent FromBytes(byte[] bytes)
        => new(bytes, null, bytes.AsSpan().StartsWith("\uFEFF"u8));

    /// <summary>Creates content from the text of an open document.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="hasBom">Whether the document's file begins with a UTF-8 BOM.</param>
    /// <returns>The content.</returns>
    public static SourceContent FromText(string text, bool hasBom)
        => new(null, text, hasBom);

    /// <summary>Gets the compiler bytes: the file bytes, or the BOM followed by the UTF-8 text (SPEC 23.3.4).</summary>
    /// <returns>The bytes, computed once.</returns>
    public byte[] GetBytes()
    {
        if (this.bytes is { } existing)
        {
            return existing;
        }

        var text = this.Text!;
        var prefix = this.HasBom ? 3 : 0;
        var encoded = new byte[prefix + Utf8.GetByteCount(text)];
        if (this.HasBom)
        {
            "\uFEFF"u8.CopyTo(encoded);
        }

        Utf8.GetBytes(text, encoded.AsSpan(prefix));
        return this.bytes = encoded;
    }

    /// <summary>Compares the compiler bytes of two contents without encoding text that can be compared as text.</summary>
    /// <param name="other">The other content.</param>
    /// <returns><see langword="true"/> when both denote the same bytes.</returns>
    public bool SameBytes(SourceContent other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (this.Text is { } text && other.Text is { } otherText)
        {
            return this.HasBom == other.HasBom && string.Equals(text, otherText, StringComparison.Ordinal);
        }

        return this.GetBytes().AsSpan().SequenceEqual(other.GetBytes());
    }

    /// <summary>Creates the source document of this content.</summary>
    /// <param name="path">The document path.</param>
    /// <param name="isTestOnly">Whether the source belongs exclusively to tests.</param>
    /// <returns>The document.</returns>
    /// <exception cref="DecoderFallbackException">The disk bytes are not valid UTF-8.</exception>
    public SourceDocument CreateDocument(string path, bool isTestOnly = false)
    {
        if (this.Text is { } text)
        {
            // The tokenizer reports text that UTF-8 cannot hold, such as a lone surrogate, as InvalidSourceEncoding_Kd.
            return new(path, text, isTestOnly);
        }

        return SourceDocument.FromUtf8(path, this.bytes!, isTestOnly);
    }
}

/// <summary>Signals a read of an open document that is out of sync (SPEC 23.4.2); the front end reports it as <c>DocumentDesynchronized_Kd</c>.</summary>
internal sealed class DesynchronizedInputException : IOException
{
    /// <summary>The observed failure of a desynchronized document, which is its unestablished identity.</summary>
    public const string Failure = "DocumentDesynchronized: the editor document is out of sync";

    /// <summary>Initializes a new instance of the <see cref="DesynchronizedInputException"/> class.</summary>
    public DesynchronizedInputException()
        : base(Failure)
    {
    }
}

/// <summary>Signals that a check needs an input with an event after its base (SPEC 23.4.6); the check takes no effect.</summary>
/// <remarks>It is no <see cref="IOException"/>, so a read handler that reports read failures lets it pass through to the state owner.</remarks>
internal sealed class PendingInputException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PendingInputException"/> class.</summary>
    /// <param name="path">The path of the pending input.</param>
    public PendingInputException(string path)
        : base("Pending input: " + path)
    {
    }
}
