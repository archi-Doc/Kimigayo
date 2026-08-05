// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using System.Diagnostics.CodeAnalysis;
using Kimi.Command;
using Kimi.Compiler;

public readonly record struct PathAndSource(string Path, string SourceText);

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

    public static bool TryCreate(Kimigayo kimigayo, ILogger logger, string path, [MaybeNullWhen(false)] out Project project)
    {
        project = default;
        try
        {
            var utf8 = System.IO.File.ReadAllBytes(path);
            var file = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8);
            if (file is null)
            {
                logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
                return false;
            }

            project = new(kimigayo);
            project.Directory = Path.GetDirectoryName(path) ?? string.Empty;
            project.Name = Path.GetFileNameWithoutExtension(path);
            project.File = file;
        }
        catch
        {
            logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
            return false;
        }

        return true;
    }

    #region FieldAndProperty

    private readonly Kimigayo kimigayo;
    private List<PathAndSource> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    public KimiOptions KimiOptions { get; set; } = new();

    public string Directory { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ProjectFile File { get; private set; } = new();

    #endregion

    public Project(Kimigayo kimigayo)
    {
        this.kimigayo = kimigayo;
        this.File = DefaultProjectFile;
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
        var targets = this.File.Targets.ToArray();
        foreach (var x in targets)
        {
            await this.Buildtarget(x).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> Buildtarget(string target)
    {
        // Create & Prepare Compilation
        var compilation = new Compilation(this.kimigayo, this);
        if (!compilation.Prepare(target))
        {
            return false;
        }

        var projectKotonoha = compilation.ProjectKotonoha;

        foreach (var y in this.kimiFiles)
        {
            try
            {
                var st = System.IO.File.ReadAllText(y);
                projectKotonoha.AddSource(new(y, st));
            }
            catch
            {
            }
        }

        foreach (var y in this.additionalSource)
        {
            projectKotonoha.AddSource(y);
        }

        compilation.ScrubForTest();

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
