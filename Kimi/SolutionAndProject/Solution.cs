// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Command;

namespace Kimi;

/// <summary>
/// Represents a build solution containing one or more Kimigayo projects.
/// </summary>
/// <remarks>A solution is the Kimigayo equivalent of a C# solution.</remarks>
public class Solution
{
    private readonly Kimigayo kimigayo;

    /// <summary>Gets the deserialized solution-file settings.</summary>
    public SolutionFile SolutionFile { get; private set; } = new();

    /// <summary>Gets the command-line options shared by projects in this solution.</summary>
    public KimiOptions KimiOptions { get; private set; } = new();

    /// <summary>Gets the standalone Kimi source selected for an implicit project.</summary>
    public string SingleFile { get; private set; } = string.Empty;

    /// <summary>Gets the loaded projects keyed by project-file path.</summary>
    public Dictionary<string, Project> Projects { get; private set; } = new();

    /// <summary>Initializes a new instance of the <see cref="Solution"/> class.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    public Solution(Kimigayo kimigayo)
    {
        this.kimigayo = kimigayo;
    }

    /// <summary>Attempts to read a <c>.kimisln</c> file.</summary>
    /// <param name="path">The solution-file path.</param>
    /// <param name="logger">The optional load logger.</param>
    /// <returns><see langword="true"/> when the file was loaded.</returns>
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

    /// <summary>Builds every loaded project using the solution options.</summary>
    /// <returns>A task whose result indicates whether build dispatch completed.</returns>
    public async Task<bool> Build()
    {
        foreach (var x in this.Projects.Values)
        {
            x.KimiOptions = this.KimiOptions;
            await x.Build();
        }

        return true;
    }

    /// <summary>Discovers solution and project files for a build command.</summary>
    /// <param name="logger">The command logger.</param>
    /// <param name="options">The shared compiler options.</param>
    /// <param name="args">Command-line paths.</param>
    public void LoadForBuild(ILogger logger, KimiOptions options, string[] args)
    {
        var projectList = new List<string>();
        this.KimiOptions = options;

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
            // this.kimigayo.GlobalDiagnostic.Add(default, Hashed.Solution.NoProject);
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

    /// <summary>Discovers a project or standalone Kimi source for a run command.</summary>
    /// <param name="logger">The command logger.</param>
    /// <param name="options">The shared compiler options.</param>
    /// <param name="args">Command-line paths.</param>
    public void LoadForRun(ILogger logger, KimiOptions options, string[] args)
    {
        string kimiFile = string.Empty;
        this.KimiOptions = options;

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

        this.SingleFile = kimiFile;
        if (this.SolutionFile.Projects.Count == 0 &&
            string.IsNullOrEmpty(this.SingleFile))
        {
            logger.GetWriter(LogLevel.Warning)?.Write(Hashed.Solution.NoRunTarget);
        }

        return;
    }

    /// <summary>Loads discovered projects and creates an implicit project for a standalone source.</summary>
    /// <param name="logger">The project-load logger.</param>
    public void PrepareProject(ILogger logger)
    {
        foreach (var x in this.SolutionFile.Projects)
        {
            if (!this.Projects.ContainsKey(x))
            {
                if (Project.TryCreate(this.kimigayo, logger, x, out var project))
                {
                    this.Projects[x] = project;
                }
            }
        }

        if (this.Projects.Count == 0 &&
            !string.IsNullOrEmpty(this.SingleFile))
        {
            if (File.Exists(this.SingleFile))
            {
                var project = new Project(this.kimigayo);
                project.Name = Path.GetFileNameWithoutExtension(this.SingleFile);
                project.Directory = Path.GetDirectoryName(this.SingleFile) ?? string.Empty;
                project.AddKimiFile(this.SingleFile);
                this.Projects[this.SingleFile] = project;
            }
            else
            {
                logger.GetWriter(LogLevel.Error)?.Write(Hashed.Project.NoKimiFile, this.SingleFile);
            }
        }
    }
}
