// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi;

/// <summary>Validates definition-side native requirements and supplies without reading native files (SPEC 20.4).</summary>
internal static class NativeConfiguration
{
    internal const string SupersededBindings = "NativeBindings is superseded. Declare definition-side requirements in NativeRequirements and supplies as NativeLibraries records (SPEC 20.4).";

    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789ABCDEFabcdef");

    // SPEC 23.1.2: each target, entry and requirement is checked independently. Left out as dependent: the entries of a null map,
    // the other checks of a null entry or of a packaged record without a valid Name, and merging a pair with an invalid record.
    internal static void Validate(ProjectFile file, ref List<string>? failures)
    {
        if (file.NativeRequirements is null || file.NativeLibraries is null)
        {
            (failures ??= []).Add("NativeRequirements and NativeLibraries must be target maps.");
            return;
        }

        foreach (var (target, requirements) in file.NativeRequirements)
        {
            if (!ValidName(target) || requirements is null)
            {
                (failures ??= []).Add("NativeRequirements target entries must be mappings.");
                continue;
            }

            foreach (var (name, requirement) in requirements)
            {
                if (!ValidName(name) || requirement is null || requirement.Kind is not ("import" or "static") ||
                    !ValidAssertions(requirement.ContractId, requirement.Sha256))
                {
                    (failures ??= []).Add($"NativeRequirements entry '{name}' requires Kind \"static\" or \"import\"; " +
                        "ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.");
                }
            }
        }

        foreach (var (target, libraries) in file.NativeLibraries)
        {
            if (!ValidName(target) || libraries is null)
            {
                (failures ??= []).Add("NativeLibraries target entries must be mappings.");
                continue;
            }

            file.NativeRequirements.TryGetValue(target, out var requirements);
            foreach (var (name, library) in libraries)
            {
                ValidateSupply(name, library, requirements?.GetValueOrDefault(name), ref failures);
            }

            // Records of one project merge under the same rule as supplies across the graph.
            var packaged = libraries.Packaged;
            for (var i = 0; i < packaged.Length; i++)
            {
                var valid = ValidatePackaged(packaged[i], ref failures);
                for (var j = 0; valid && j < i; j++)
                {
                    if (packaged[j] is { } other && packaged[i].Name == other.Name && packaged[i].Package == other.Package &&
                        (!Agree(packaged[i].ContractId, other.ContractId, StringComparison.Ordinal) ||
                        !Agree(packaged[i].Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase)) && IsValidPackaged(other))
                    {
                        (failures ??= []).Add($"NativeLibraries supplies of '{packaged[i].Name}' for package '{packaged[i].Package!.PackageId}' " +
                            "assert different ContractId or Sha256 values and cannot merge.");
                        break;
                    }
                }
            }
        }
    }

    // The graph's supplies (SPEC 20.4), each failure its own note at its module. A supply that targets a resolved module is checked
    // against that module's requirement: it can neither create nor change it, so a supply for a name the module does not require stays
    // unused. Supplies of one module's native name merge only when their contracts agree; content agreement needs the actual files,
    // and ContractId and Sha256 assertions that can never both hold are rejected here. A conflicting supply joins no merge.
    internal static void ValidateGraphSupplies(DependencyNode[] nodes, ref List<(int Module, string Note)>? failures)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            foreach (var (target, supplies) in nodes[i].Input.Configuration.NativeLibraries)
            {
                foreach (var supply in supplies.Packaged)
                {
                    ValidateTargetedSupply(target, supply, nodes, i, ref failures);
                }
            }
        }

        Dictionary<(string Target, string PackageId, string PackageVersion, string Name), (string? ContractId, string? Sha256)>? merged = null;
        for (var i = 0; i < nodes.Length; i++)
        {
            var file = nodes[i].Input.Configuration;
            foreach (var (target, libraries) in file.NativeLibraries)
            {
                if (file.PackageId is { } packageId && file.PackageVersion is { } packageVersion)
                {
                    foreach (var (name, library) in libraries)
                    {
                        Merge(ref merged, (target, packageId, packageVersion, name), library, i, ref failures);
                    }
                }

                foreach (var library in libraries.Packaged)
                {
                    Merge(ref merged, (target, library.Package!.PackageId, library.Package.PackageVersion, library.Name!), library, i, ref failures);
                }
            }
        }

        static void Merge(
            ref Dictionary<(string, string, string, string), (string? ContractId, string? Sha256)>? merged,
            (string Target, string PackageId, string PackageVersion, string Name) key,
            NativeLibraryInput library,
            int module,
            ref List<(int Module, string Note)>? failures)
        {
            merged ??= new();
            ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(merged, key, out var exists);
            if (exists && (!Agree(entry.ContractId, library.ContractId, StringComparison.Ordinal) ||
                !Agree(entry.Sha256, library.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                (failures ??= []).Add((module, $"NativeLibraries supplies of '{key.Name}' for package '{key.PackageId}' " +
                    "assert different ContractId or Sha256 values and cannot merge."));
                return;
            }

            entry = (entry.ContractId ?? library.ContractId, entry.Sha256 ?? library.Sha256);
        }
    }

    // Gets the expanded requirement fields of a validated self-targeted supply.
    internal static (string Kind, string? Sha256) Expand(ProjectFile file, string target, string name, NativeLibraryInput library)
    {
        var requirement = file.NativeRequirements.TryGetValue(target, out var requirements) ? requirements.GetValueOrDefault(name) : null;
        return ((library.Kind ?? requirement?.Kind)!, library.Sha256 ?? requirement?.Sha256);
    }

    // A supply for another package checked against the resolved module of that identity: a missing or different required ContractId
    // and a disagreeing Sha256 are independent.
    private static void ValidateTargetedSupply(
        string target, NativeLibraryInput supply, DependencyNode[] nodes, int owner, ref List<(int Module, string Note)>? failures)
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

            // A ContractId that disagrees is always a missing or different required one, so the second check reads Sha256 alone.
            if (contractId is not null && supply.ContractId != contractId)
            {
                (failures ??= []).Add((owner, $"NativeLibraries record '{supply.Name}' for package '{package.PackageId}' " +
                    $"must assert the required ContractId '{contractId}'."));
            }

            if (!Agree(supply.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                (failures ??= []).Add((owner, $"NativeLibraries record '{supply.Name}' for package '{package.PackageId}' " +
                    "disagrees with that module's ContractId or Sha256 requirement."));
            }
        }
    }

    // A self-targeted supply's failures, each its own note; a null entry has nothing else to check.
    private static void ValidateSupply(string name, NativeLibraryInput? library, NativeRequirement? requirement, ref List<string>? failures)
    {
        if (library is null || !ValidName(name) || library.Package is not null || (library.Name is not null && library.Name != name) ||
            !ValidAssertions(library.ContractId, library.Sha256))
        {
            (failures ??= []).Add("NativeLibraries requires nonempty logical names; " +
                "ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.");
            if (library is null)
            {
                return;
            }
        }

        if (name == WindowsProfile.Kernel32Library)
        {
            (failures ??= []).Add("kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.");
        }

        // A self-targeted record expands into a requirement and a supply; overlapping explicit fields must agree.
        if (requirement is not null &&
            (!Agree(library.Kind, requirement.Kind, StringComparison.Ordinal) ||
            !Agree(library.ContractId, requirement.ContractId, StringComparison.Ordinal) ||
            !Agree(library.Sha256, requirement.Sha256, StringComparison.OrdinalIgnoreCase)))
        {
            (failures ??= []).Add($"NativeLibraries entry '{name}' disagrees with its NativeRequirements Kind, ContractId or Sha256.");
        }

        if ((library.Kind ?? requirement?.Kind) is not ("import" or "static"))
        {
            (failures ??= []).Add($"NativeLibraries entry '{name}' requires Kind \"static\" or \"import\" after requirement expansion.");
        }
    }

    // A supply record for another package: each failure its own note; true when the record is valid. A record without a valid Name
    // has nothing else to check.
    private static bool ValidatePackaged(NativeLibraryInput? library, ref List<string>? failures)
    {
        var count = failures?.Count ?? 0;
        if (library is null || !ValidName(library.Name) || library.Package is not { } package ||
            !DependencyConfiguration.ValidPackageId(package.PackageId) || !DependencyConfiguration.ValidPackageVersion(package.PackageVersion) ||
            !ValidAssertions(library.ContractId, library.Sha256))
        {
            (failures ??= []).Add("NativeLibraries records targeting a Package require a nonempty Name and a valid PackageId and PackageVersion; " +
                "ContractId must be nonempty and Sha256 must be 64 hexadecimal digits when present.");
            if (library is null || !ValidName(library.Name))
            {
                return false;
            }
        }

        if (library.Name == WindowsProfile.Kernel32Library)
        {
            (failures ??= []).Add("kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.");
        }

        // The reserved backend is one compiler-managed supply; only a project's own legacy override may name it.
        if (library.Name == WindowsProfile.BackendLibrary)
        {
            (failures ??= []).Add("kimi_backend is supplied by the compiler toolchain and cannot be supplied for another package.");
        }

        if (library.Kind is not null)
        {
            (failures ??= []).Add($"NativeLibraries record '{library.Name}' for package '{library.Package?.PackageId}' cannot declare Kind; " +
                "Kind is allowed only in self-targeted records.");
        }

        return (failures?.Count ?? 0) == count;
    }

    // Whether a packaged record is valid, without a note (reported where it was validated); allocation-free when it is.
    private static bool IsValidPackaged(NativeLibraryInput record)
    {
        List<string>? ignored = null;
        return ValidatePackaged(record, ref ignored);
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
