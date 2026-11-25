// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Command;
using Kimi.Diagnostics;
using Tinyhand.IO;

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
            SolutionFile? file;
            var writer = TinyhandWriter.CreateFromThreadStaticBuffer();
            try
            {
                ConfigurationText.ToBinary(utf8, ref writer);
                var reader = new TinyhandReader(writer);
                file = TinyhandSerializer.Deserialize<SolutionFile>(ref reader, TinyhandSerializerOptions.ConvertToString);
            }
            finally
            {
                writer.Dispose();
            }

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
        catch (Exception ex) when (CheckInputSource.IsReadFailure(ex) || ex is TinyhandException)
        {
            // The solution file's read, its format and its project paths are the selection's input (IMPL 20.3.1); any other exception is
            // the compiler's and propagates.
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

    /// <summary>Builds every selected project according to its OutputKind.</summary>
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

    /// <summary>Checks run's selection before any build or record write (IMPL 20.3), with or without --no-build: exactly one loaded
    /// project and one selected target, each else a command input error, and that target the host's (the project's host rule).</summary>
    /// <returns>Whether the project may be built and run; false after the project's record.</returns>
    public bool PrepareRun()
    {
        if (!this.AllProjectsLoaded() || this.Projects.Count != 1)
        {
            throw new CommandInputException("Run requires exactly one loaded project or an explicit .exe path.");
        }

        var project = this.Projects.Values.Single();
        project.KimiOptions = this.KimiOptions;
        project.SolutionLanguageVersion = this.SolutionFile.Configuration.LangVersion;
        if (project.SelectedTargets().Targets.Length > 1)
        {
            throw new CommandInputException("Run requires one selected target; select one with --Target.");
        }

        return project.RunsOnHost();
    }

    /// <summary>Runs the one project <see cref="PrepareRun"/> accepted without building it.</summary>
    /// <param name="cancellationToken">Cancels the child process.</param>
    /// <returns>The application's exit code.</returns>
    public Task<int> Run(CancellationToken cancellationToken = default)
        => this.Projects.Values.Single().Run(cancellationToken);

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
                    throw new CommandInputException("Cannot load solution: " + x);
                }

                goto SolutionLoaed;
            }
        }

        // Tries to load solution file in directory
        foreach (var x in inputs)
        {
            if (Directory.Exists(x))
            {
                // IMPL 20.3.1: exactly one .kimisln selects that solution, and more than one is an error.
                switch (ListFiles(x, $"*{Constants.KimiSolutionExtension}"))
                {
                    case [var solution]:
                        if (!this.TryReadFile(Path.GetFullPath(solution), logger))
                        {
                            throw new CommandInputException("Cannot load solution: " + solution);
                        }

                        goto SolutionLoaed;
                    case { Length: > 1 } solutions:
                        throw new CommandInputException("More than one solution file in directory: " + string.Join(", ", solutions));
                }

                // Load project file in directory
                var previousCount = projectList.Count;
                foreach (var y in ListFiles(x, $"*{Constants.KimiProjectExtension}"))
                {
                    projectList.Add(Path.GetFullPath(y));
                }

                if (projectList.Count == previousCount)
                {
                    throw new CommandInputException("No solution or project files found in directory: " + x);
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
                    throw new CommandInputException("Source file not found: " + x);
                }

                try
                {
                    this.Projects[x] = Project.CreateFromSource(this.kimigayo, x, this.KimiOptions);
                }
                catch (PlatformNotSupportedException ex)
                {
                    // IMPL 20.3.1: the implicit project's host target cannot be established (as CheckJsonOutput and WorkspaceCheck classify it).
                    throw new CommandInputException(ex.Message);
                }
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

    // IMPL 20.3.1: a selected input that cannot be named or read is the command's Input error, as a missing or unsupported one is.
    internal static string ResolveInputPath(string input)
    {
        try
        {
            return Resolve(input);
        }
        catch (Exception ex) when (CheckInputSource.IsReadFailure(ex))
        {
            throw new CommandInputException(ex.Message);
        }
    }

    private static string Resolve(string input)
    {
        var path = Path.GetFullPath(input);
        var supported = path.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Constants.KimiSolutionExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase);
        if (!TryGetAttributes(path, out var attributes))
        {
            // IMPL 20.3.1: an input ending in .kimi, .kimiproj, .kimisln or .exe selects only that path; any other input A, with or
            // without another extension, tries A.kimiproj, then A.kimi.
            if (!supported && !path.EndsWith(Constants.ExecutableExtension, StringComparison.OrdinalIgnoreCase))
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

                throw new CommandInputException($"Input not found. Tried '{path}', '{project}', and '{source}'.");
            }

            throw new CommandInputException("Input not found: " + path);
        }

        if ((attributes & FileAttributes.Directory) == 0 && !supported)
        {
            throw new CommandInputException("Unsupported input file: " + path + ". Expected .kimiproj, .kimisln, or .kimi.");
        }

        return path;
    }

    // A selected directory's listing (IMPL 20.3.1); its read failure is the command's Input error.
    private static string[] ListFiles(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (CheckInputSource.IsReadFailure(ex))
        {
            throw new CommandInputException(ex.Message);
        }
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
