// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>One resolved artifact set shared by emission, native build and execution.</summary>
internal sealed record ArtifactPaths(string Ir, string Manifest, string Record, string Stem)
{
    internal string Executable => this.Stem + ".exe";

    internal static ArtifactPaths Create(Project project)
    {
        if (project.ProjectFile.Optimization is not ("O0" or "O2"))
        {
            throw new InvalidDataException("Optimization must be O0 or O2.");
        }

        var directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
        var ir = ArtifactFiles.ResolvePath(project.ProjectFile.OutputPath ?? Path.Combine("bin", WindowsProfile.Target, project.Name + ".ll"), directory);
        if (!Path.GetExtension(ir).Equals(".ll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OutputPath must have a .ll extension.");
        }

        var manifest = Path.ChangeExtension(ir, ".link.json");
        return new(ir, manifest, Path.ChangeExtension(manifest, ".build.json"), Path.Combine(Path.GetDirectoryName(ir)!, Path.GetFileNameWithoutExtension(ir) + "." + project.ProjectFile.Optimization));
    }
}
