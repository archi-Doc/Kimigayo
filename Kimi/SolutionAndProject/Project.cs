// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using System.Diagnostics.CodeAnalysis;
using Kimigayo.Language;

public readonly record struct PathAndSource(string Path, string Source);

public partial class Project
{
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = ["x86_64-pc-windows-msvc"];
        projectFile.Alias = ["Kimi.Base",];

        DefaultProjectFile = projectFile;
    }

    public static bool TryCreate(KimiControl kimiControl, ILogger logger, string path, [MaybeNullWhen(false)] out Project project)
    {
        project = default;
        try
        {
            var utf8 = File.ReadAllBytes(path);
            var file = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8);
            if (file is null)
            {
                logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
                return false;
            }

            project = new(kimiControl);
            project.ProjectFile = file;
            project.ProjectName = Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
            return false;
        }

        return true;
    }

    #region FieldAndProperty

    private readonly KimiControl kimiControl;
    private List<PathAndSource> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    public KimiOptions KimiOptions { get; set; } = new();

    public string ProjectName { get; set; } = string.Empty;

    public ProjectFile ProjectFile { get; private set; } = new();

    #endregion

    public Project(KimiControl kimiControl)
    {
        this.kimiControl = kimiControl;
        this.ProjectFile = DefaultProjectFile;
    }

    public void AddSource(string url, string text)
    {
        this.additionalSource.Add(new(url, text));
    }

    public void AddKimiFile(string path)
    {
        this.kimiFiles.Add(path);
    }

    public async Task<bool> Build()
    {
        var targets = this.ProjectFile.Targets.ToArray();
        foreach (var x in targets)
        {
            await this.Buildtarget(x).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> Buildtarget(string target)
    {
        // Create & Prepare Compilation
        var compilation = new Compilation(this.kimiControl, this.KimiOptions, this.ProjectFile, this.ProjectName);
        if (!compilation.Prepare(target))
        {
            return false;
        }

        var projectKotonoha = compilation.ProjectKotonoha;

        foreach (var y in this.kimiFiles)
        {
            try
            {
                var st = File.ReadAllText(y);
                projectKotonoha.AddSource(compilation, new(y, st));
            }
            catch
            {
            }
        }

        foreach (var y in this.additionalSource)
        {
            projectKotonoha.AddSource(compilation, y);
        }

        // Resolve shared let & @Attribute

        // Prepare CodeContext

        // Resolve

        // Mods

        // Validate

        // Emit

        // Compile

        // Link

        return true;
    }
}
