// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Project;

using Arc.Unit;
using SimplePrompt;

public partial class Project
{
    #region FieldAndProperty

    public IConsoleService ConsoleService { get; set; }

    public ProjectFile ProjectFile { get; private set; } = new();

    public string[] Targets => this.ProjectFile.Targets;

    public string[] GlobalUse => this.ProjectFile.Use;

    #endregion

    public Project()
    {
        this.ConsoleService = SimpleConsole.Instance;
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
        return true;
    }
}
