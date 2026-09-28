// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi;

/// <summary>Validates definition-side native requirements and supplies without reading native files (SPEC 20.8.2.1).</summary>
internal static class NativeConfiguration
{
    internal const string SupersededBindings = "NativeBindings is superseded. Declare definition-side requirements in NativeRequirements and supplies as NativeLibraries records (SPEC 20.8.2.1).";

    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789ABCDEFabcdef");

    internal static string? Validate(ProjectFile file)
    {
        if (file.NativeRequirements is null || file.NativeLibraries is null)
        {
            return "NativeRequirements and NativeLibraries must be target maps.";
        }

        foreach (var (target, requirements) in file.NativeRequirements)
        {
            if (!ValidName(target) || requirements is null)
            {
                return "NativeRequirements target entries must be mappings.";
            }

            foreach (var (name, requirement) in requirements)
            {
                if (!ValidName(name) || requirement is null || requirement.Kind is not ("import" or "static") || !ValidAssertions(requirement.ContractId, requirement.Sha256))
                {
                    return $"NativeRequirements entry '{name}' requires Kind \"static\" or \"import\"; ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.";
                }
            }
        }

        foreach (var (target, libraries) in file.NativeLibraries)
        {
            if (!ValidName(target) || libraries is null)
            {
                return "NativeLibraries target entries must be mappings.";
            }

            file.NativeRequirements.TryGetValue(target, out var requirements);
            foreach (var (name, library) in libraries)
            {
                if (!ValidName(name) || library is null || library.Package is not null || (library.Name is not null && library.Name != name) || !ValidAssertions(library.ContractId, library.Sha256))
                {
                    return "NativeLibraries requires nonempty logical names; ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.";
                }

                if (name == Kernel32Imports.LibraryName)
                {
                    return "kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.";
                }

                // A self-targeted record expands into a requirement and a supply; overlapping explicit fields must agree.
                var requirement = requirements?.GetValueOrDefault(name);
                if (requirement is not null &&
                    (!Agree(library.Kind, requirement.Kind, StringComparison.Ordinal) ||
                    !Agree(library.ContractId, requirement.ContractId, StringComparison.Ordinal) ||
                    !Agree(library.Sha256, requirement.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"NativeLibraries entry '{name}' disagrees with its NativeRequirements Kind, ContractId or Sha256.";
                }

                if ((library.Kind ?? requirement?.Kind) is not ("import" or "static"))
                {
                    return $"NativeLibraries entry '{name}' requires Kind \"static\" or \"import\" after requirement expansion.";
                }
            }

            // A supply for another module cannot create or change that module's requirement, so it carries no Kind.
            foreach (var library in libraries.Packaged)
            {
                if (library is null || !ValidName(library.Name) || library.Package is not { } package ||
                    !DependencyConfiguration.ValidPackageId(package.PackageId) || !DependencyConfiguration.ValidPackageVersion(package.PackageVersion) ||
                    !ValidAssertions(library.ContractId, library.Sha256))
                {
                    return "NativeLibraries records targeting a Package require a nonempty Name and a valid PackageId and PackageVersion; ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.";
                }

                if (library.Name == Kernel32Imports.LibraryName)
                {
                    return "kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.";
                }

                // The reserved backend is one compiler-managed supply; only a project's own legacy override may name it.
                if (library.Name == WindowsProfile.BackendLibrary)
                {
                    return "kimi_backend is supplied by the compiler toolchain and cannot be supplied for another package.";
                }

                if (library.Kind is not null)
                {
                    return $"NativeLibraries record '{library.Name}' for package '{package.PackageId}' cannot declare Kind; Kind is allowed only in self-targeted records.";
                }
            }

            // Records of one project merge under the same rule as supplies across the graph.
            var packaged = libraries.Packaged;
            for (var i = 1; i < packaged.Length; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (packaged[i].Name == packaged[j].Name && packaged[i].Package == packaged[j].Package &&
                        (!Agree(packaged[i].ContractId, packaged[j].ContractId, StringComparison.Ordinal) || !Agree(packaged[i].Sha256, packaged[j].Sha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"NativeLibraries supplies of '{packaged[i].Name}' for package '{packaged[i].Package!.PackageId}' assert different ContractId or Sha256 values and cannot merge.";
                    }
                }
            }
        }

        return null;
    }

    // Checks supplies that target resolved dependency modules against those modules' own requirements (SPEC 20.8.2.1).
    // A supply can neither create nor change another module's requirement, so a supply for a name that module does not require stays unused.
    internal static string? ValidatePackageSupplies(ProjectFile file, DependencyNode[] nodes)
    {
        foreach (var (target, supplies) in file.NativeLibraries)
        {
            foreach (var supply in supplies.Packaged)
            {
                var package = supply.Package!;
                foreach (var node in nodes)
                {
                    var module = node.Input.Configuration;
                    if (module.PackageId != package.PackageId || module.PackageVersion != package.PackageVersion ||
                        !TryGetRequirement(module, target, supply.Name!, out var contractId, out var sha256))
                    {
                        continue;
                    }

                    if (contractId is not null && supply.ContractId != contractId)
                    {
                        return $"NativeLibraries record '{supply.Name}' for package '{package.PackageId}' must assert the required ContractId '{contractId}'.";
                    }

                    if (!Agree(supply.ContractId, contractId, StringComparison.Ordinal) || !Agree(supply.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"NativeLibraries record '{supply.Name}' for package '{package.PackageId}' disagrees with that module's ContractId or Sha256 requirement.";
                    }
                }
            }
        }

        return null;
    }

    // Several supplies of one module's native name merge only when their contracts agree (SPEC 20.8.2.1). Content
    // agreement needs the actual files; ContractId and Sha256 assertions that can never both hold are rejected here.
    internal static string? ValidateSupplyAgreement(DependencyNode[] nodes, out int module)
    {
        Dictionary<(string Target, string PackageId, string PackageVersion, string Name), (string? ContractId, string? Sha256)>? supplies = null;
        for (module = 0; module < nodes.Length; module++)
        {
            var file = nodes[module].Input.Configuration;
            foreach (var (target, libraries) in file.NativeLibraries)
            {
                if (file.PackageId is { } packageId && file.PackageVersion is { } packageVersion)
                {
                    foreach (var (name, library) in libraries)
                    {
                        if (!Merge(ref supplies, (target, packageId, packageVersion, name), library))
                        {
                            return Conflict(name, packageId);
                        }
                    }
                }

                foreach (var library in libraries.Packaged)
                {
                    if (!Merge(ref supplies, (target, library.Package!.PackageId, library.Package.PackageVersion, library.Name!), library))
                    {
                        return Conflict(library.Name, library.Package.PackageId);
                    }
                }
            }
        }

        module = -1;
        return null;

        static string Conflict(string? name, string packageId)
            => $"NativeLibraries supplies of '{name}' for package '{packageId}' assert different ContractId or Sha256 values and cannot merge.";

        static bool Merge(ref Dictionary<(string, string, string, string), (string? ContractId, string? Sha256)>? supplies, (string, string, string, string) key, NativeLibraryInput library)
        {
            supplies ??= new();
            ref var merged = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(supplies, key, out var exists);
            if (exists && (!Agree(merged.ContractId, library.ContractId, StringComparison.Ordinal) || !Agree(merged.Sha256, library.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            merged = (merged.ContractId ?? library.ContractId, merged.Sha256 ?? library.Sha256);
            return true;
        }
    }

    // Gets the expanded requirement fields of a validated self-targeted supply.
    internal static (string Kind, string? Sha256) Expand(ProjectFile file, string target, string name, NativeLibraryInput library)
    {
        var requirement = file.NativeRequirements.TryGetValue(target, out var requirements) ? requirements.GetValueOrDefault(name) : null;
        return ((library.Kind ?? requirement?.Kind)!, library.Sha256 ?? requirement?.Sha256);
    }

    // Gets a module's own requirement, declared directly or by a self-targeted combined supply.
    private static bool TryGetRequirement(ProjectFile file, string target, string name, out string? contractId, out string? sha256)
    {
        var requirement = file.NativeRequirements.TryGetValue(target, out var requirements) ? requirements.GetValueOrDefault(name) : null;
        var library = file.NativeLibraries.TryGetValue(target, out var libraries) ? libraries.GetValueOrDefault(name) : null;
        contractId = requirement?.ContractId ?? library?.ContractId;
        sha256 = requirement?.Sha256 ?? library?.Sha256;
        return requirement is not null || library is not null;
    }

    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Contains('\0');

    private static bool ValidAssertions(string? contractId, string? sha256)
        => (contractId is null || (contractId.Length != 0 && !contractId.Contains('\0'))) && (sha256 is null || (sha256.Length == 64 && !sha256.AsSpan().ContainsAnyExcept(HexDigits)));

    private static bool Agree(string? left, string? right, StringComparison comparison)
        => left is null || right is null || string.Equals(left, right, comparison);
}
