// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi;

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

    internal static string? Validate(ProjectFile file)
    {
        if (file.KotonohaArray is { Length: > 0 })
        {
            return "KotonohaArray is no longer supported. Replace it with Dependencies using PackageId, PackageVersion and a Project or Package source.";
        }

        if ((file.PackageId is not null || file.PackageVersion is not null) && (!ValidPackageId(file.PackageId) || !ValidPackageVersion(file.PackageVersion)))
        {
            return "PackageId and PackageVersion must both be supplied with valid exact identity spellings.";
        }

        if (file.Dependencies is null || file.TestDependencies is null || file.PackageSources is null || file.TestSources is null || file.Alias is null)
        {
            return "Dependency maps and source lists must not be null.";
        }

        if ((ValidateReferences(file.Dependencies) ?? NativeConfiguration.Validate(file)) is { } failure)
        {
            return failure;
        }

        foreach (var alias in file.Alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return "Alias requires nonempty Container paths.";
            }
        }

        foreach (var source in file.PackageSources)
        {
            if (source is null || (source.Store is { } store ? string.IsNullOrWhiteSpace(store) || source.PackageId is not null || source.PackageVersion is not null || source.Package is not null :
                !ValidPackageId(source.PackageId) || !ValidPackageVersion(source.PackageVersion) || string.IsNullOrWhiteSpace(source.Package)))
            {
                return "PackageSources entries require either Store or PackageId, PackageVersion and Package.";
            }
        }

        // Test dependency resolution belongs to the test partition. Configuration syntax
        // still rejects invalid/duplicate source paths without reading those files.
        for (var i = 0; i < file.TestSources.Length; i++)
        {
            var path = file.TestSources[i];
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.AsSpan().IndexOfAny('\0', '*', '?') >= 0)
            {
                return "TestSources requires explicit project-relative paths without globs.";
            }

            for (var j = 0; j < i; j++)
            {
                if (string.Equals(path, file.TestSources[j], StringComparison.Ordinal))
                {
                    return $"Duplicate TestSources path: {path}";
                }
            }
        }

        return null;
    }

    internal static string? ValidateReferences(Dictionary<string, DependencyReference> references)
    {
        foreach (var (name, reference) in references)
        {
            if (!IdentifierHelper.IsValidIdentifier(name) || !TokenHelper.GetKeywordOrIdentifierKind(name).IsIdentifierOrContextualKeyword() || name == "Kimi")
            {
                return $"Invalid or reserved dependency reference name: {name}";
            }

            if (reference is null || !ValidPackageId(reference.PackageId) || !ValidPackageVersion(reference.PackageVersion))
            {
                return $"Dependency '{name}' requires a valid PackageId and exact PackageVersion.";
            }

            if ((reference.Project is not null && reference.Package is not null) ||
                (reference.Project is { } project && (string.IsNullOrWhiteSpace(project) || !project.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase))) ||
                (reference.Package is { } package && (string.IsNullOrWhiteSpace(package) || !package.EndsWith(".kimipkg", StringComparison.OrdinalIgnoreCase))))
            {
                return $"Dependency '{name}' must specify at most one Project (.kimiproj) or Package (.kimipkg) path.";
            }
        }

        return null;
    }
}
