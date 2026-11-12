// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1600, SA1649 // Rendering configuration and structured URL components.

/// <summary>A logical source target. Null and empty query/fragment are distinct.</summary>
public sealed record DocumentationLinkTarget(object? ProjectIdentity, string LogicalPath, string? Query, string? Fragment);

/// <summary>Deterministic output inputs. Results are not cached across settings or callbacks.</summary>
public sealed record DocumentationHtmlOptions
{
    public int DeclarationHeadingLevel { get; init; } = 1;

    public string? LogicalSourceName { get; init; }

    public object? ProjectIdentity { get; init; }

    /// <summary>Gets the absolute display-page URL, required when an HTML base is specified.</summary>
    public string? PageUrl { get; init; }

    public string? HtmlBaseUrl { get; init; }

    /// <summary>Gets a mapper returning a decoded output path (not a URL); null disables the target.
    /// Default placement uses the same logical path under the output root.</summary>
    public Func<DocumentationLinkTarget, string?> MapSourcePath { get; init; } = static target => "/" + target.LogicalPath;

    public Func<string, string?>? RewriteLink { get; init; }

    /// <summary>Gets an optional ASCII scheme policy. HTTP(S) still requires a valid authority.</summary>
    public Func<string, bool>? AllowScheme { get; init; }
}

/// <summary>Structured source resolution and checked RFC 3986 component serialization.</summary>
public static class DocumentationLinks
{
    private const string PathKept = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-._~!$&'()*+,;=:@/";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly DocumentationHtmlOptions ExternalOptions = new();
    private static readonly DocumentationHtmlOptions FileOptions = new() { AllowScheme = static scheme => scheme == "file" };

    // The ASCII characters a serialized component keeps (RFC 3986): a path keeps pchar and '/'; a query, and a fragment (its
    // grammar), also '?'. An escaped component keeps its '%' escapes; a decoded path encodes every '%'. Others are UTF-8 escaped.
    private static readonly SearchValues<char> KeptInDecodedPath = SearchValues.Create(PathKept);
    private static readonly SearchValues<char> KeptInPath = SearchValues.Create(PathKept + "%");
    private static readonly SearchValues<char> KeptInQueryOrFragment = SearchValues.Create(PathKept + "?%");

    // Internationalized hosts require explicit tool-side conversion; never path-encode them. These ASCII characters are never
    // valid in an HTTP(S) authority.
    private static readonly SearchValues<char> AuthorityExcluded = SearchValues.Create(" <>\"%{}|");

    // Editor placement is separate from HTML output paths. Only mapped source links may acquire the file scheme.
    internal static string? ResolveEditor(
        string destination, string? logicalSourceName, object? projectIdentity, Func<DocumentationLinkTarget, string?> map)
    {
        var valid = Validate(destination, ExternalOptions, out var parts);
        if (valid is null || parts.SchemeLength > 0)
        {
            return valid;
        }

        if (parts.Path.Length == 0 || parts.Path.StartsWith('/'))
        {
            return null;
        }

        var target = ResolveSource(destination, logicalSourceName, projectIdentity);
        var physical = target is null ? null : map(target);
        if (physical is null || !IsPlainFile(physical))
        {
            return null;
        }

        return Validate(Serialize(new(physical, target!.Query, target.Fragment)), FileOptions, out _);
    }

    // An editor target is an absolute file URI of its own, without a query or a fragment: the link's are appended.
    private static bool IsPlainFile(string physical)
        => Uri.TryCreate(physical, UriKind.Absolute, out var uri) && uri.IsFile && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static string? Resolve(string destination, DocumentationHtmlOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        var result = Validate(destination, options, out var parts);
        if (result is null)
        {
            return null;
        }

        var validateOutput = false;
        if (parts.SchemeLength == 0 && parts.Path.Length == 0)
        {
            if (options.HtmlBaseUrl is not null && options.PageUrl is null)
            {
                throw new ArgumentException("An explicit display-page URL is required with an HTML base.", nameof(options));
            }

            if (options.PageUrl is { } page)
            {
                if (Validate(page, options, out var pageParts) is null || pageParts.SchemeLength == 0)
                {
                    throw new ArgumentException("The display-page URL must be an allowed absolute URL.", nameof(options));
                }

                result = Serialize(new(pageParts.Path, parts.Query ?? pageParts.Query, parts.Fragment));
            }
        }
        else if (parts.SchemeLength == 0 && !parts.Path.StartsWith('/'))
        {
            var target = ResolveSource(destination, options.LogicalSourceName, options.ProjectIdentity);
            var outputPath = target is null ? null : options.MapSourcePath(target);
            if (string.IsNullOrEmpty(outputPath) || outputPath.StartsWith("//", StringComparison.Ordinal) || !HasValidCharacters(outputPath))
            {
                return null;
            }

            result = Encode(outputPath, KeptInDecodedPath);
            var slash = result.IndexOf('/');
            if (!result.StartsWith('/') && result.AsSpan(0, slash >= 0 ? slash : result.Length).Contains(':'))
            {
                result = "./" + result;
            }

            result = Serialize(new(result, target!.Query, target.Fragment));
            validateOutput = true;
        }

        if (options.RewriteLink is { } rewrite)
        {
            result = rewrite(result);
            validateOutput = true;
        }

        return result is null || !validateOutput ? result : Validate(result, options, out _);
    }

    public static DocumentationLinkTarget? ResolveSource(string destination, string? logicalSourceName, object? projectIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (logicalSourceName is null || !IsValidReference(destination))
        {
            return null;
        }

        var parts = Split(destination);
        if (parts.Path.Length == 0 || parts.Path.StartsWith('/') || parts.SchemeLength > 0)
        {
            return null;
        }

        var segments = new List<string>();
        var parent = logicalSourceName.LastIndexOf('/');
        if (parent >= 0)
        {
            foreach (var segment in logicalSourceName[..parent].Split('/'))
            {
                if (!Push(segments, segment))
                {
                    return null;
                }
            }
        }

        var directory = parts.Path.EndsWith('/');
        foreach (var segment in parts.Path.Split('/'))
        {
            if (!TryDecodeSegment(segment, out var decoded) || !Push(segments, decoded))
            {
                return null;
            }

            directory = segment.Length == 0 || decoded is "." or "..";
        }

        var path = string.Join('/', segments);
        if (directory && path.Length > 0)
        {
            path += "/";
        }

        return new(projectIdentity, path, parts.Query, parts.Fragment);
    }

    private static bool Push(List<string> segments, string segment)
    {
        if (segment is "" or ".")
        {
            return true;
        }

        if (segment == "..")
        {
            if (segments.Count == 0)
            {
                return false;
            }

            segments.RemoveAt(segments.Count - 1);
        }
        else
        {
            segments.Add(segment);
        }

        return true;
    }

    private static string? Validate(string value, DocumentationHtmlOptions options, out Parts parts)
    {
        parts = Split(value);
        if (!IsValidReference(value) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (parts.SchemeLength > 0)
        {
            var lower = parts.Path[..parts.SchemeLength].ToLowerInvariant();
            if (!(options.AllowScheme?.Invoke(lower) ?? (lower is "http" or "https" or "mailto")))
            {
                return null;
            }

            if (lower is "http" or "https")
            {
                var authorityStart = parts.SchemeLength + 3;
                if (!value.AsSpan(parts.SchemeLength).StartsWith("://") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
                {
                    return null;
                }

                var end = parts.Path.IndexOf('/', authorityStart);
                var authority = end < 0 ? parts.Path.AsSpan(authorityStart) : parts.Path.AsSpan(authorityStart, end - authorityStart);
                if (!Ascii.IsValid(authority) || authority.ContainsAny(AuthorityExcluded))
                {
                    return null;
                }
            }
        }
        else
        {
            if (parts.Path.Contains('%'))
            {
                foreach (var segment in parts.Path.Split('/'))
                {
                    if (!TryDecodeSegment(segment, out _))
                    {
                        return null;
                    }
                }
            }

            var slash = parts.Path.IndexOf('/');
            if (parts.Path.AsSpan(0, slash < 0 ? parts.Path.Length : slash).Contains(':'))
            {
                return null;
            }
        }

        return Serialize(parts);
    }

    private static Parts Split(string value)
    {
        var hash = value.IndexOf('#');
        var beforeFragment = hash < 0 ? value.Length : hash;
        var query = value.AsSpan(0, beforeFragment).IndexOf('?');
        var end = query < 0 ? beforeFragment : query;
        return new(value[..end], query < 0 ? null : value[(query + 1)..beforeFragment], hash < 0 ? null : value[(hash + 1)..]);
    }

    // The length of a path's scheme (RFC 3986: a letter, then letters, digits, '+', '-' or '.', before the first ':'); 0 for a
    // relative reference.
    private static int SchemeLength(ReadOnlySpan<char> path)
    {
        var colon = path.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(path[0]))
        {
            return 0;
        }

        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(path[i]) && path[i] is not ('+' or '-' or '.'))
            {
                return 0;
            }
        }

        return colon;
    }

    // A written reference: no edge space, valid characters, and a two-digit hex escape after every '%'.
    private static bool IsValidReference(string value)
        => !value.StartsWith(' ') && !value.EndsWith(' ') && HasValidCharacters(value) && HasValidEscapes(value);

    // The characters of a reference, and all of a decoded path or segment: no control character or backslash; paired surrogates.
    private static bool HasValidCharacters(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            // Unicode 15 Cc is exactly these two ranges.
            if (c <= 0x1f || c is >= '\x7f' and <= '\x9f' || c == '\\')
            {
                return false;
            }

            if (char.IsSurrogate(c) && (!char.IsHighSurrogate(c) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i])))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidEscapes(ReadOnlySpan<char> value)
    {
        for (var i = value.IndexOf('%'); i >= 0; i = value.IndexOf('%'))
        {
            if (i + 2 >= value.Length || !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2]))
            {
                return false;
            }

            value = value[(i + 3)..];
        }

        return true;
    }

    // A path segment of a source link: strictly percent-decoded, with no '/' and valid decoded characters.
    private static bool TryDecodeSegment(string value, out string decoded)
    {
        decoded = value;
        if (value.Contains('%') && !TryPercentDecode(value, out decoded))
        {
            return false;
        }

        return !decoded.Contains('/') && HasValidCharacters(decoded);
    }

    // Each escape is two hex digits and the bytes are well-formed UTF-8.
    private static bool TryPercentDecode(string value, out string decoded)
    {
        decoded = value;
        var bytes = ArrayPool<byte>.Shared.Rent(StrictUtf8.GetMaxByteCount(value.Length));
        try
        {
            var count = 0;
            for (var i = 0; i < value.Length;)
            {
                if (value[i] == '%')
                {
                    if (i + 2 >= value.Length || !byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                    {
                        return false;
                    }

                    bytes[count++] = b;
                    i += 3;
                }
                else
                {
                    var start = i++;
                    while (i < value.Length && value[i] != '%')
                    {
                        i++;
                    }

                    count += StrictUtf8.GetBytes(value.AsSpan(start, i - start), bytes.AsSpan(count));
                }
            }

            decoded = StrictUtf8.GetString(bytes, 0, count);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    // By reference: a 32-byte by-value copy of Parts goes through a ymm register, and the framework code this member calls then
    // pays an AVX-SSE transition (about 100 ns per link on the measured host; the T-133 diagnosis, R9-M1 part 2 design review 3).
    private static string Serialize(in Parts parts)
        => EncodePath(parts.Path)
        + (parts.Query is null ? string.Empty : "?" + Encode(parts.Query, KeptInQueryOrFragment))
        + (parts.Fragment is null ? string.Empty : "#" + Encode(parts.Fragment, KeptInQueryOrFragment));

    private static string EncodePath(string path)
    {
        var scheme = SchemeLength(path);
        if (scheme > 0 && path.AsSpan(scheme).StartsWith("://"))
        {
            var slash = path.IndexOf('/', scheme + 3);
            return slash < 0 ? path : path[..slash] + Encode(path[slash..], KeptInPath);
        }

        return Encode(path, KeptInPath);
    }

    private static string Encode(string value, SearchValues<char> kept)
    {
        StringBuilder? output = null;
        var copied = 0;
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in value.EnumerateRunes())
        {
            var c = rune.Value;
            if (c >= 128 || !kept.Contains((char)c))
            {
                output ??= new StringBuilder(value.Length + 16).Append(value.AsSpan(0, copied));
                var length = rune.EncodeToUtf8(utf8);
                const string Hex = "0123456789ABCDEF";
                for (var i = 0; i < length; i++)
                {
                    output.Append('%').Append(Hex[utf8[i] >> 4]).Append(Hex[utf8[i] & 15]);
                }
            }
            else if (output is not null)
            {
                output.Append((char)c);
            }

            copied += rune.Utf16SequenceLength;
        }

        return output?.ToString() ?? value;
    }

    // A reference split into its components. SchemeLength, computed when the Parts is built, is the length of Path's scheme (0
    // for a relative reference), so a reader tests for a scheme without allocating it. 32 bytes: Serialize takes it by reference.
    private readonly record struct Parts(string Path, string? Query, string? Fragment)
    {
        public int SchemeLength { get; } = DocumentationLinks.SchemeLength(Path);
    }
}
