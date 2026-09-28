// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using Kimi.Diagnostics;

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

    /// <summary>Gets the loaded projects keyed by project-file or implicit source-file path.</summary>
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

    /// <summary>Checks every loaded project's front end without emitting artifacts.</summary>
    /// <returns>Whether the loaded projects pass front-end checks.</returns>
    public Task<bool> Check() => this.Check(default);

    /// <summary>Checks every loaded project's front end without emitting artifacts.</summary>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether the loaded projects pass front-end checks.</returns>
    public async Task<bool> Check(CancellationToken cancellationToken)
        => this.AllProjectsLoaded() && await this.BuildCore(false, cancellationToken).ConfigureAwait(false);

    /// <summary>Builds native binaries for all selected projects.</summary>
    /// <param name="cancellationToken">Cancels generation and tool execution.</param>
    /// <returns>Whether all selected projects built successfully.</returns>
    public async Task<bool> Build(CancellationToken cancellationToken = default)
    {
        if (!this.AllProjectsLoaded())
        {
            return false;
        }

        var success = true;
        foreach (var project in this.Projects.Values)
        {
            project.KimiOptions = this.KimiOptions;
            project.SolutionLanguageVersion = this.SolutionFile.Configuration.LangVersion;
            success &= await project.Build(cancellationToken).ConfigureAwait(false);
        }

        return success;
    }

    /// <summary>Runs exactly one selected Application without building it.</summary>
    /// <param name="cancellationToken">Cancels the child process.</param>
    /// <returns>The application's exit code.</returns>
    public Task<int> Run(CancellationToken cancellationToken = default)
    {
        if (!this.AllProjectsLoaded() || this.Projects.Count != 1)
        {
            throw new InvalidDataException("Run requires exactly one loaded project or an explicit .exe path.");
        }

        var project = this.Projects.Values.Single();
        if ((this.KimiOptions.Target.Length != 0 && this.KimiOptions.Target != Compiler.WindowsProfile.Target) ||
            !project.ProjectFile.Targets.Contains(Compiler.WindowsProfile.Target, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Run requires the Windows x64 target.");
        }

        return project.Run(cancellationToken);
    }

    /// <summary>Generates checked LLVM/manifest pairs for the loaded projects.</summary>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every project published its artifacts.</returns>
    public async Task<bool> Generate(CancellationToken cancellationToken = default)
    {
        return this.AllProjectsLoaded() && await this.BuildCore(true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves shared build, emit and run inputs, including implicit single-source projects.</summary>
    /// <param name="logger">The command logger.</param>
    /// <param name="options">The shared compiler options.</param>
    /// <param name="args">Command-line paths.</param>
    public void LoadForBuild(ILogger? logger, KimiOptions options, string[] args)
    {
        var projectList = new List<string>();
        this.SolutionFile = new();
        this.Projects.Clear();
        this.KimiOptions = options;

        var inputs = new string[Math.Max(1, args.Length)];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i] = ResolveInputPath(args.Length == 0 ? Directory.GetCurrentDirectory() : args[i]);
        }

        // Tries to load solution file
        foreach (var x in inputs)
        {
            if (!Directory.Exists(x) && x.EndsWith(Constants.KimiSolutionExtension, StringComparison.OrdinalIgnoreCase))
            {// *.kimisln
                if (!this.TryReadFile(x, logger))
                {
                    throw new InvalidDataException("Cannot load solution: " + x);
                }

                goto SolutionLoaed;
            }
        }

        // Tries to load solution file in directory
        foreach (var x in inputs)
        {
            if (Directory.Exists(x))
            {
                foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiSolutionExtension}", SearchOption.TopDirectoryOnly))
                {
                    if (!this.TryReadFile(Path.GetFullPath(y), logger))
                    {
                        throw new InvalidDataException("Cannot load solution: " + y);
                    }

                    goto SolutionLoaed;
                }

                // Load project file in directory
                var previousCount = projectList.Count;
                foreach (var y in Directory.EnumerateFiles(x, $"*{Constants.KimiProjectExtension}", SearchOption.TopDirectoryOnly))
                {
                    projectList.Add(Path.GetFullPath(y));
                }

                if (projectList.Count == previousCount)
                {
                    throw new InvalidDataException("No solution or project files found in directory: " + x);
                }
            }
        }

        // Load project file
        foreach (var x in inputs)
        {
            if (!Directory.Exists(x) &&
                (x.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase) || x.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase)))
            {
                projectList.Add(x);
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
            logger?.GetWriter(LogLevel.Warning)?.Write(Hashed.Solution.NoProject);
            // this.kimigayo.GlobalDiagnostic.Add(default, Hashed.Solution.NoProject);
        }

        return;
    }

    /// <summary>Loads discovered project files or creates implicit single-source projects.</summary>
    /// <param name="logger">The project-load logger.</param>
    public void PrepareProject(ILogger? logger)
    {
        foreach (var x in this.SolutionFile.Projects)
        {
            if (this.Projects.ContainsKey(x))
            {
                continue;
            }

            if (x.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(x))
                {
                    throw new FileNotFoundException("Source file not found: " + x, x);
                }

                this.Projects[x] = Project.CreateFromSource(this.kimigayo, x, this.KimiOptions);
            }
            else if (Project.TryCreate(this.kimigayo, logger, x, out var project))
            {
                this.Projects[x] = project;
            }

            if (this.Projects.TryGetValue(x, out var loaded))
            {
                var implicitLabel = x.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase) ? ", implicit" : string.Empty;
                var selectedTarget = string.IsNullOrEmpty(this.KimiOptions.Target) ? string.Empty : " | Target: " + this.KimiOptions.Target;
                var settings = loaded.ProjectFile;
                this.kimigayo.WriteLine(
                    DiagnosticSeverity.Information,
                    $"Project: {loaded.Name} ({Path.GetFileName(x)}{implicitLabel}) | Targets: {string.Join(", ", settings.Targets)} | OutputKind: {settings.OutputKind} | Optimization: {settings.Optimization}{selectedTarget}");
            }
        }
    }

    internal static string ResolveInputPath(string input)
    {
        var path = Path.GetFullPath(input);
        if (!TryGetAttributes(path, out var attributes))
        {
            if (!Path.HasExtension(path))
            {
                var project = path + Constants.KimiProjectExtension;
                if (TryGetAttributes(project, out var projectAttributes) && (projectAttributes & FileAttributes.Directory) == 0)
                {
                    return project;
                }

                var source = path + Constants.KimiExtension;
                if (TryGetAttributes(source, out var sourceAttributes) && (sourceAttributes & FileAttributes.Directory) == 0)
                {
                    return source;
                }

                throw new FileNotFoundException($"Input not found. Tried '{path}', '{project}', and '{source}'.", path);
            }

            throw new FileNotFoundException("Input not found: " + path, path);
        }

        if ((attributes & FileAttributes.Directory) == 0 &&
            !path.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(Constants.KimiSolutionExtension, StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Unsupported input file: " + path + ". Expected .kimiproj, .kimisln, or .kimi.");
        }

        return path;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private bool AllProjectsLoaded()
    {
        var loaded = this.Projects.Count != 0 && this.SolutionFile.Projects.All(this.Projects.ContainsKey);
        if (!loaded)
        {
            this.kimigayo.WriteLine(DiagnosticSeverity.Error, "No projects loaded, or a selected project could not be loaded.");
        }

        return loaded;
    }

    private async Task<bool> BuildCore(bool emit, CancellationToken cancellationToken = default)
    {
        var success = true;
        foreach (var x in this.Projects.Values)
        {
            x.KimiOptions = this.KimiOptions;
            x.SolutionLanguageVersion = this.SolutionFile.Configuration.LangVersion;
            success &= emit ? await x.Generate(cancellationToken) : await x.Check(cancellationToken);
        }

        return success;
    }
}
