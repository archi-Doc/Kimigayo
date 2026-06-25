// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using System.Diagnostics.CodeAnalysis;
using Kimigayo.Language;

public readonly record struct UrlAndtext(string Url, string Text);

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
    private HashSet<string> targets = new();
    private HashSet<string> globalUse = new();
    private List<UrlAndtext> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    public SolutionOptions SolutionOptions { get; set; } = new();

    public string ProjectPath { get; private set; } = string.Empty;

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
        this.Prepare();

        foreach (var x in this.targets)
        {
            var compilation = new Compilation(this.kimiControl, this.SolutionOptions, x);
            compilation.Prepare();
            foreach (var y in this.kimiFiles)
            {
                try
                {
                    var st = File.ReadAllText(y);
                    compilation.Build(new(y, st));
                }
                catch
                {
                }
            }

            foreach (var y in this.additionalSource)
            {
                compilation.Build(y);
            }
        }

        return true;
    }

    private void Prepare()
    {
        this.targets = this.ProjectFile.Targets.ToHashSet();
        this.globalUse = this.ProjectFile.Alias.ToHashSet();
    }
}
