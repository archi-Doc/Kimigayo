// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using Arc.Unit;
using SimplePrompt;

public partial class Project
{
    #region FieldAndProperty

    private HashSet<string> targets = new();
    private HashSet<string> globalUse = new();
    private List<string> additionalSource = [];

    public IConsoleService ConsoleService { get; set; }

    public ProjectFile ProjectFile { get; private set; } = new();

    #endregion

    public Project(IConsoleService consoleService)
    {// Console, Log
        this.ConsoleService = SimpleConsole.Instance;
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
            this.ConsoleService.WriteLine(Hashed.Project.NotFound, file);
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
