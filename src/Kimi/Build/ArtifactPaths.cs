// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>One resolved artifact set shared by emission, build records and native execution. It alone names an output's files (IMPL
/// 20.3.2): for the IR <c>Name.ll</c>, the manifest <c>Name.link.json</c>, the record <c>Name.link.build.json</c> (Begin keeps its lock
/// beside it) and the stem <c>Name.O0</c> or <c>Name.O2</c> of the native build's own files, among them the folder
/// <c>Name.O0.native</c> of its staged inputs and the executable, the stem with its row's format's extension (COFF <c>.exe</c>, ELF
/// none; the Tatara plan's B7b7).</summary>
internal sealed record ArtifactPaths(string Ir, string Manifest, string Record, string Stem, string ExecutableExtension)
{
    private const string ManifestSuffix = ".link.json";

    internal string Executable => this.Stem + this.ExecutableExtension;

    internal string Native => this.Stem + ".native";

    // A project's output: its OutputPath, bin/<target>/<Name>.ll by default; windows-x64's until B7b8 opens the commands for every row.
    internal static ArtifactPaths Create(Project project)
    {
        if (project.ProjectFile.Optimization is not ("O0" or "O2"))
        {
            throw new InvalidDataException("Optimization must be O0 or O2.");
        }

        var directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
        var ir = ArtifactFiles.ResolvePath(project.ProjectFile.OutputPath ?? Path.Combine("bin", TargetProfile.Windows.Target, project.Name + ".ll"), directory);
        if (!Path.GetExtension(ir).Equals(".ll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OutputPath must have a .ll extension.");
        }

        return Create(ir, project.ProjectFile.Optimization, TargetProfile.Windows);
    }

    // The output of the IR ir, a full path with a .ll extension, for a row: its executable's extension is the row's format's, mapped
    // here to the extension, as the native build's Link maps it to its writer (IMPL 20.3.2); another format is a broken contract.
    internal static ArtifactPaths Create(string ir, string optimization, TargetProfile row)
    {
        var extension = row.Format switch
        {
            "COFF" => ".exe",
            "ELF" => string.Empty,
            _ => throw new InvalidOperationException($"No executable format '{row.Format}'."),
        };
        return new(ir, Path.ChangeExtension(ir, ManifestSuffix), RecordOf(ir), Path.ChangeExtension(ir, "." + optimization), extension);
    }

    // build --Manifest's IR: the Name.ll beside the manifest Name.link.json, as emit writes the pair, so the manifest's name is its output's;
    // a manifest of another name names no output (G1203).
    internal static string IrOf(string manifest) => manifest.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase)
        ? manifest[..^ManifestSuffix.Length] + ".ll"
        : throw new InvalidDataException("A link manifest is named Name.link.json, as emit writes it.");

    // The record of the IR ir's output, which build --Manifest takes (Begin) before it reads the manifest.
    internal static string RecordOf(string ir) => Path.ChangeExtension(ir, ".link.build.json");
}
