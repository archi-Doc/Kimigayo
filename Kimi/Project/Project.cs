// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using Arc.Unit;
using SimplePrompt;

public partial class Project
{
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = ["x86_64-pc-windows-msvc"];
        projectFile.Use = ["Kimi.Base",];

        DefaultProjectFile = projectFile;
    }

    #region FieldAndProperty

    private readonly IConsoleService consoleService;
    private HashSet<string> targets = new();
    private HashSet<string> globalUse = new();
    private List<string> additionalSource = [];

    public ProjectFile ProjectFile { get; private set; } = new();

    #endregion

    public Project(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
        this.ProjectFile = DefaultProjectFile;
    }

    public void AddSource(string source)
    {
        this.additionalSource.Add(source);
    }

    public bool TryReadFile(string file)
    {
        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(file);
        }
        catch
        {
            this.consoleService.WriteLine(Hashed.Project.NotFound, file);
            return false;
        }

        return true;
    }

    public async Task<bool> Build()
    {
        this.Prepare();

        foreach (var x in this.additionalSource)
        {
            this.Build(x);
        }

        return true;
    }

    private void Build(ReadOnlySpan<char> source)
    {
    }

    private void Prepare()
    {
        this.targets = this.ProjectFile.Targets.ToHashSet();
        this.globalUse = this.ProjectFile.Use.ToHashSet();
    }
}
