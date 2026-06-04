// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimigayo;

public class Solution
{
    private readonly KimiControl kimiControl;

    public SolutionFile SolutionFile { get; private set; } = new();

    public string KimiFile { get; private set; } = string.Empty;

    public SolutionOptions SolutionOptions { get; private set; } = new();

    public Dictionary<string, Project> Projects { get; private set; } = new();

    public Solution(KimiControl kimiControl)
    {
        this.kimiControl = kimiControl;
    }

    public bool TryReadFile(string path, ILogger? logger = default)
    {
        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(path);
            var file = TinyhandSerializer.DeserializeFromUtf8<SolutionFile>(utf8);
            if (file is null)
            {
                logger?.GetWriter()?.Write(Hashed.Solution.NotLoaded, path);
                return false;
            }

            var baseDirectory = Path.GetDirectoryName(path);
            if (baseDirectory is not null)
            {// Relative path to absolute path
                for (var i = 0; i < file.Projects.Count; i++)
                {
                    if (!Path.IsPathFullyQualified(file.Projects[i]))
                    {
                        file.Projects[i] = Path.GetFullPath(file.Projects[i], baseDirectory);
                    }
                }
            }

            this.SolutionFile = file;
        }
        catch
        {
            logger?.GetWriter()?.Write(Hashed.Solution.NotLoaded, path);
            return false;
        }

        logger?.GetWriter()?.Write(Hashed.Solution.Loaded, path);

        return true;
    }

    public async Task<bool> Build()
    {
        foreach (var x in this.Projects.Values)
        {
            x.SolutionOptions = this.SolutionOptions;
            await x.Build();
        }

        return true;
    }

    public void LoadForBuild(ILogger logger, SolutionOptions options, string[] args)
    {
        var projectList = new List<string>();
        this.SolutionOptions = options;

        var currentDirectory = Directory.GetCurrentDirectory();
        if (args.Length == 0)
        {// If not specified, the current directory is used.
            args = [currentDirectory,];
        }

        // Tries to load solution file
        foreach (var x in args)
        {
            if (x.EndsWith(Constants.KimiSolutionExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimisln
                if (this.TryReadFile(x, logger))
                {
                    goto SolutionLoaed;
                }
            }
        }

        // Tries to load solution file in directory
        foreach (var x in args)
        {
            if (Directory.Exists(x))
            {
                foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiSolutionExtension}", SearchOption.TopDirectoryOnly))
                {
                    if (this.TryReadFile(y, logger))
                    {
                        goto SolutionLoaed;
                    }
                }

                // Load project file in directory
                foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiProjectExtension}", SearchOption.TopDirectoryOnly))
                {
                    projectList.Add(y);
                }
            }
        }

        // Load project file
        foreach (var x in args)
        {
            if (x.EndsWith(Constants.KimiProjectExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimiproj
                if (Path.IsPathFullyQualified(x))
                {
                    projectList.Add(x);
                }
                else
                {
                    projectList.Add(Path.GetFullPath(x, currentDirectory));
                }
            }
        }

        foreach (var x in projectList)
        {
            if (!this.SolutionFile.Projects.Contains(x))
            {
                this.SolutionFile.Projects.Add(x);
            }
        }

SolutionLoaed:

        if (this.SolutionFile.Projects.Count == 0)
        {
            logger.GetWriter(LogLevel.Warning)?.Write(Hashed.Solution.NoProject);
            // this.kimiControl.GlobalDiagnostic.Add(default, Hashed.Solution.NoProject);
        }

        var sb = new StringBuilder();
        sb.Append(HashedString.Get(Hashed.Solution.TargetProjects));
        foreach (var x in this.SolutionFile.Projects)
        {
            sb.Append(Path.GetFileName(x));
            sb.Append(", ");
        }

        logger.GetWriter()?.Write(sb.ToString());

        return;
    }

    public void LoadForRun(ILogger logger, SolutionOptions options, string[] args)
    {
        string kimiFile = string.Empty;
        this.SolutionOptions = options;

        var currentDirectory = Directory.GetCurrentDirectory();
        if (args.Length == 0)
        {// If not specified, the current directory is used.
            args = [currentDirectory,];
        }

        // Load project or kimi file
        foreach (var x in args)
        {
            if (x.EndsWith(Constants.KimiProjectExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimiproj
                if (Path.IsPathFullyQualified(x))
                {
                    this.SolutionFile.Projects.Add(x);
                }
                else
                {
                    this.SolutionFile.Projects.Add(Path.GetFullPath(x, currentDirectory));
                }

                break;
            }
            else if (string.IsNullOrEmpty(kimiFile) &&
                x.EndsWith(Constants.KimiExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimi
                if (Path.IsPathFullyQualified(x))
                {
                    kimiFile = x;
                }
                else
                {
                    kimiFile = Path.GetFullPath(x, currentDirectory);
                }
            }
        }

        if (this.SolutionFile.Projects.Count == 0)
        {
            // Tries to load project file in directory
            foreach (var x in args)
            {
                if (Directory.Exists(x))
                {
                    // Load project file in directory
                    foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiProjectExtension}", SearchOption.TopDirectoryOnly))
                    {
                        this.SolutionFile.Projects.Add(y);
                        break;
                    }

                    if (string.IsNullOrEmpty(kimiFile))
                    {
                        foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiExtension}", SearchOption.TopDirectoryOnly))
                        {
                            kimiFile = y;
                        }
                    }
                }
            }
        }

        this.KimiFile = kimiFile;
        if (this.SolutionFile.Projects.Count == 0 &&
            string.IsNullOrEmpty(this.KimiFile))
        {
            logger.GetWriter(LogLevel.Warning)?.Write(Hashed.Solution.NoRunTarget);
        }

        return;
    }

    public void PrepareProject(ILogger logger)
    {
        foreach (var x in this.SolutionFile.Projects)
        {
            if (!this.Projects.ContainsKey(x))
            {
                if (Project.TryCreate(this.kimiControl, logger, x, out var project))
                {
                    this.Projects[x] = project;
                }
            }
        }

        if (this.Projects.Count == 0 &&
            !string.IsNullOrEmpty(this.KimiFile))
        {
            if (File.Exists(this.KimiFile))
            {
                var project = new Project(this.kimiControl);
                project.AddKimiFile(this.KimiFile);
                this.Projects[this.KimiFile] = project;
            }
            else
            {
                logger.GetWriter(LogLevel.Error)?.Write(Hashed.Project.NoKimiFile, this.KimiFile);
            }
        }
    }
}
