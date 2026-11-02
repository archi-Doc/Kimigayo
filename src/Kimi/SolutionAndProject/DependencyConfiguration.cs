// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi;

/// <summary>The one owner of a project configuration's rules (IMPL 20.1, 20.2, SPEC 18.4), TestSources included.</summary>
/// <remarks>
/// Input: a <see cref="ProjectFile"/>, and for the TestSources comparison the project directory. Output: one note per
/// independent failure (SPEC 23.1.2), empty and allocation-free when valid; a failure only another causes is left out.
/// Callers report each note as its own record of their code and never re-check a rule; the notes' texts are written here.
/// </remarks>
internal static class DependencyConfiguration
{
    internal static bool ValidPackageId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        var separator = false;
        foreach (var c in value)
        {
            if (c is '.' or '-')
            {
                if (separator)
                {
                    return false;
                }

                separator = true;
            }
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                separator = false;
            }
            else
            {
                return false;
            }
        }

        return !separator;
    }

    internal static bool ValidPackageVersion(string? value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '+'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets every independent failure of the directory-free configuration rules, empty when valid (SPEC 23.1.2).</summary>
    /// <param name="file">The configuration.</param>
    /// <returns>One note per failure; a failure that only another causes (a check reading a null map) is left out.</returns>
    internal static IReadOnlyList<string> Validate(ProjectFile file)
    {
        List<string>? failures = null;
        Collect(file, ref failures);
        return (IReadOnlyList<string>?)failures ?? [];
    }

    /// <summary>Gets every independent configuration failure at the project directory, with the TestSources entries compared as
    /// resolved paths (IMPL 20.1), and the resolved TestSources.</summary>
    /// <param name="file">The configuration.</param>
    /// <param name="directory">The full path of the project directory.</param>
    /// <param name="testSources">The resolved TestSources, or null when none is listed.</param>
    /// <returns>One note per failure, empty when valid.</returns>
    internal static IReadOnlyList<string> Validate(ProjectFile file, string directory, out HashSet<string>? testSources)
    {
        List<string>? failures = null;
        Collect(file, ref failures);
        testSources = null;
        foreach (var source in file.TestSources ?? [])
        {
            // An entry that breaks the path syntax is reported above; comparing it would depend on that failure.
            if (!ValidTestSource(source))
            {
                continue;
            }

            try
            {
                if (!(testSources ??= new(SourceIdentity.PathComparer)).Add(Path.GetFullPath(source, directory)))
                {
                    (failures ??= []).Add($"Duplicate resolved TestSources path: {source}");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                (failures ??= []).Add($"Invalid TestSources path: {ex.Message}");
            }
        }

        return (IReadOnlyList<string>?)failures ?? [];
    }

    // One reference's name, identity and source are independent; a null reference has no source to check.
    internal static void ValidateReference(string name, DependencyReference? reference, ref List<string>? failures)
    {
        if (!IdentifierHelper.IsValidIdentifier(name) || !TokenHelper.GetKeywordOrIdentifierKind(name).IsIdentifierOrContextualKeyword() || name == "Kimi")
        {
            (failures ??= []).Add($"Invalid or reserved dependency reference name: {name}");
        }

        if (reference is null || !ValidPackageId(reference.PackageId) || !ValidPackageVersion(reference.PackageVersion))
        {
            (failures ??= []).Add($"Dependency '{name}' requires a valid PackageId and exact PackageVersion.");
        }

        if (reference is not null && ((reference.Project is not null && reference.Package is not null) ||
            (reference.Project is { } project && (string.IsNullOrWhiteSpace(project) || !project.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase))) ||
            (reference.Package is { } package && (string.IsNullOrWhiteSpace(package) || !package.EndsWith(".kimipkg", StringComparison.OrdinalIgnoreCase)))))
        {
            (failures ??= []).Add($"Dependency '{name}' must specify at most one Project (.kimiproj) or Package (.kimipkg) path.");
        }
    }

    private static bool ValidTestSource(string? path)
        => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && path.AsSpan().IndexOfAny('\0', '*', '?') < 0;

    // Each check below is independent of the others; a null map is reported once and only the checks that read it are left out.
    private static void Collect(ProjectFile file, ref List<string>? failures)
    {
        if (file.KotonohaArray is { Length: > 0 })
        {
            (failures ??= []).Add(
                "KotonohaArray is no longer supported. Replace it with Dependencies using PackageId, PackageVersion and a Project or Package source.");
        }

        if ((file.PackageId is not null || file.PackageVersion is not null) && (!ValidPackageId(file.PackageId) || !ValidPackageVersion(file.PackageVersion)))
        {
            (failures ??= []).Add("PackageId and PackageVersion must both be supplied with valid exact identity spellings.");
        }

        if (file.Dependencies is null || file.TestDependencies is null || file.PackageSources is null || file.TestSources is null || file.Alias is null)
        {
            (failures ??= []).Add("Dependency maps and source lists must not be null.");
        }

        if (file.Dependencies is { } dependencies)
        {
            foreach (var (name, reference) in dependencies)
            {
                ValidateReference(name, reference, ref failures);
            }
        }

        NativeConfiguration.Validate(file, ref failures);
        foreach (var alias in file.Alias ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                (failures ??= []).Add("Alias requires nonempty Container paths.");
            }
        }

        foreach (var source in file.PackageSources ?? [])
        {
            if (source is null || (source.Store is { } store ? string.IsNullOrWhiteSpace(store) || source.PackageId is not null || source.PackageVersion is not null || source.Package is not null :
                !ValidPackageId(source.PackageId) || !ValidPackageVersion(source.PackageVersion) || string.IsNullOrWhiteSpace(source.Package)))
            {
                (failures ??= []).Add("PackageSources entries require either Store or PackageId, PackageVersion and Package.");
            }
        }

        // Test dependency resolution belongs to the test partition; the TestSources syntax is checked without reading those files.
        foreach (var path in file.TestSources ?? [])
        {
            if (!ValidTestSource(path))
            {
                (failures ??= []).Add("TestSources requires explicit project-relative paths without globs.");
            }
        }
    }
}
