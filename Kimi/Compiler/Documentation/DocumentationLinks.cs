// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;

namespace Kimi.Compiler.Documentation;

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
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string? Resolve(string destination, DocumentationHtmlOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        var result = Validate(destination, options, out var parts, out var scheme);
        if (result is null)
        {
            return null;
        }

        var validateOutput = false;

        if (scheme is null && parts.Path.Length == 0)
        {
            if (options.HtmlBaseUrl is not null && options.PageUrl is null)
            {
                throw new ArgumentException("An explicit display-page URL is required with an HTML base.", nameof(options));
            }

            if (options.PageUrl is { } page)
            {
                if (Validate(page, options, out var pageParts, out var pageScheme) is null || pageScheme is null)
                {
                    throw new ArgumentException("The display-page URL must be an allowed absolute URL.", nameof(options));
                }

                result = Serialize(new(pageParts.Path, parts.Query ?? pageParts.Query, parts.Fragment));
            }
        }
        else if (scheme is null && !parts.Path.StartsWith('/'))
        {
            var target = ResolveSource(destination, options.LogicalSourceName, options.ProjectIdentity);
            var outputPath = target is null ? null : options.MapSourcePath(target);
            if (string.IsNullOrEmpty(outputPath) || outputPath.StartsWith("//", StringComparison.Ordinal) || !ValidCharacters(outputPath, percentEncoding: false, checkEdges: false))
            {
                return null;
            }

            result = Encode(outputPath, path: true, preserveEscapes: false);
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

        return result is null || !validateOutput ? result : Validate(result, options, out _, out _);
    }

    public static DocumentationLinkTarget? ResolveSource(string destination, string? logicalSourceName, object? projectIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (logicalSourceName is null || !ValidCharacters(destination, percentEncoding: true))
        {
            return null;
        }

        var parts = Split(destination);
        if (parts.Path.Length == 0 || parts.Path.StartsWith('/') || Scheme(parts.Path) is not null)
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

    private static string? Validate(string value, DocumentationHtmlOptions options, out Parts parts, out string? scheme)
    {
        parts = Split(value);
        scheme = Scheme(parts.Path);
        if (!ValidCharacters(value, percentEncoding: true) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (scheme is not null)
        {
            var lower = scheme.ToLowerInvariant();
            if (!(options.AllowScheme?.Invoke(lower) ?? (lower is "http" or "https" or "mailto")))
            {
                return null;
            }

            if (lower is "http" or "https")
            {
                var authorityStart = scheme.Length + 3;
                if (!value.AsSpan(scheme.Length).StartsWith("://") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
                {
                    return null;
                }

                var end = parts.Path.IndexOf('/', authorityStart);
                var authority = end < 0 ? parts.Path.AsSpan(authorityStart) : parts.Path.AsSpan(authorityStart, end - authorityStart);
                // Internationalized hosts require explicit tool-side conversion; never path-encode them.
                foreach (var c in authority)
                {
                    if (c > 127 || c is ' ' or '<' or '>' or '"' or '%' or '{' or '}' or '|')
                    {
                        return null;
                    }
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
        var query = value.AsSpan(0, hash < 0 ? value.Length : hash).IndexOf('?');
        var end = query >= 0 ? query : hash >= 0 ? hash : value.Length;
        return new(value[..end], query < 0 ? null : value[(query + 1)..(hash < 0 ? value.Length : hash)], hash < 0 ? null : value[(hash + 1)..]);
    }

    private static string? Scheme(string path)
    {
        var colon = path.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(path[0]))
        {
            return null;
        }

        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(path[i]) && path[i] is not ('+' or '-' or '.'))
            {
                return null;
            }
        }

        return path[..colon];
    }

    private static bool ValidCharacters(string value, bool percentEncoding, bool checkEdges = true)
    {
        if (checkEdges && (value.StartsWith(' ') || value.EndsWith(' ')))
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            // Unicode 15 Cc is exactly these two ranges.
            if (c <= 0x1f || c is >= '\x7f' and <= '\x9f' || c == '\\')
            {
                return false;
            }

            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i]))
                {
                    return false;
                }
            }
            else if (percentEncoding && c == '%')
            {
                if (i + 2 >= value.Length || !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2]))
                {
                    return false;
                }

                i += 2;
            }
        }

        return true;
    }

    private static bool TryDecodeSegment(string value, out string decoded)
    {
        decoded = value;
        if (value.Contains('%'))
        {
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

        return !decoded.Contains('/') && ValidCharacters(decoded, percentEncoding: false, checkEdges: false);
    }

    private static string Serialize(Parts parts)
        => EncodePath(parts.Path)
        + (parts.Query is null ? string.Empty : "?" + Encode(parts.Query, path: false, preserveEscapes: true))
        + (parts.Fragment is null ? string.Empty : "#" + Encode(parts.Fragment, path: false, preserveEscapes: true));

    private static string EncodePath(string path)
    {
        var scheme = Scheme(path);
        if (scheme is not null && path.AsSpan(scheme.Length).StartsWith("://"))
        {
            var slash = path.IndexOf('/', scheme.Length + 3);
            return slash < 0 ? path : path[..slash] + Encode(path[slash..], path: true, preserveEscapes: true);
        }

        return Encode(path, path: true, preserveEscapes: true);
    }

    private static string Encode(string value, bool path, bool preserveEscapes)
    {
        StringBuilder? output = null;
        var copied = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var c = rune.Value;
            var allowed = c < 128 && (char.IsAsciiLetterOrDigit((char)c) || "-._~!$&'()*+,;=:@/".Contains((char)c) || (!path && c == '?') || (preserveEscapes && c == '%'));
            if (!allowed)
            {
                output ??= new StringBuilder(value.Length + 16).Append(value.AsSpan(0, copied));
                output.Append(Uri.EscapeDataString(rune.ToString()));
            }
            else if (output is not null)
            {
                output.Append((char)c);
            }

            copied += rune.Utf16SequenceLength;
        }

        return output?.ToString() ?? value;
    }

    private readonly record struct Parts(string Path, string? Query, string? Fragment);
}
