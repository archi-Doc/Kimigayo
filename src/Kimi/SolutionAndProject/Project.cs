// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kimi.Build;
using Kimi.Checking;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;

/// <summary>
/// Represents an application or library build unit, configured by a project file or a single-source input.
/// </summary>
/// <remarks>A project is the Kimigayo equivalent of a C# project. Its input (IMPL 20.3.1), the selected file every build record names, is
/// stored once and derived: the project file of a <c>.kimiproj</c> input, the one source file of a <c>.kimi</c> input's implicit project
/// (no project file), its full path as resolution gives it; none for a project without a project file and with another number of source
/// files (one made in memory).</remarks>
public partial class Project
{
    /// <summary>Gets the default settings for programmatically constructed projects.</summary>
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = [TargetProfile.Windows.Target];

        DefaultProjectFile = projectFile;
    }

    /// <summary>Loads a <c>.kimiproj</c> file. Read and configuration failures return <see langword="false"/>. Other exceptions propagate.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="logger">The load logger.</param>
    /// <param name="path">The project-file path.</param>
    /// <param name="project">The loaded project.</param>
    /// <returns><see langword="true"/> when the project was loaded.</returns>
    public static bool TryCreate(Kimigayo kimigayo, ILogger? logger, string path, [MaybeNullWhen(false)] out Project project)
        => TryCreate(kimigayo, logger, path, CheckInputSource.Disk, out project, out _);

    /// <summary>Loads a <c>.kimiproj</c> file through the input source. Read and configuration failures return <see langword="false"/> with their reasons. Other exceptions propagate.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="logger">The load logger.</param>
    /// <param name="path">The project-file path.</param>
    /// <param name="inputs">The input source; the language server supplies open documents.</param>
    /// <param name="project">The loaded project.</param>
    /// <param name="failures">The reasons the project did not load, one per independent failure (SPEC 23.1.2); empty when loaded.</param>
    /// <returns><see langword="true"/> when the project was loaded.</returns>
    internal static bool TryCreate(
        Kimigayo kimigayo,
        ILogger? logger,
        string path,
        CheckInputSource inputs,
        [MaybeNullWhen(false)] out Project project,
        out IReadOnlyList<string> failures)
    {
        // SPEC 23.2.3 (PLAN G1010): only the load's own calls are classified, each where it reads: the path's naming, the file's read and
        // the source listing (CheckInputSource.IsReadFailure), and the configuration text's parse and rules (ProjectFile.Load:
        // TinyhandException, ProjectConfigurationException; the deserializer's span also runs compiler code, a recorded limit). Any other
        // exception is the compiler's and ends the caller's result as Faulted (the check entry's CheckService.Load).
        project = default;
        string fullPath;
        byte[] bytes;
        try
        {
            fullPath = Path.GetFullPath(path);
            bytes = inputs.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (CheckInputSource.IsReadFailure(ex))
        {
            return Failed([$"The project '{path}' could not be loaded: {ex.Message}"], out failures);
        }

        ProjectFile? file;
        try
        {
            file = ProjectFile.Load(bytes);
        }
        catch (Exception ex) when (ex is TinyhandException or ProjectConfigurationException)
        {
            var notes = (ex as ProjectConfigurationException)?.Failures ?? [ex.Message];
            var reasons = new string[notes.Count];
            for (var i = 0; i < reasons.Length; i++)
            {
                reasons[i] = $"Invalid project configuration '{path}': {notes[i]}";
            }

            return Failed(reasons, out failures);
        }

        if (file is null)
        {
            return Failed([$"Empty project configuration '{path}'."], out failures);
        }

        var loaded = new Project(kimigayo, file) { FilePath = fullPath, Directory = Path.GetDirectoryName(fullPath)!, Name = Path.GetFileNameWithoutExtension(path) };
        string[] sources;
        try
        {
            sources = inputs.GetFiles(loaded.Directory, "*.kimi");
        }
        catch (Exception ex) when (CheckInputSource.IsReadFailure(ex))
        {
            return Failed([$"The project '{path}' could not be loaded: {ex.Message}"], out failures);
        }

        foreach (var source in sources)
        {
            loaded.AddKimiFile(source);
        }

        project = loaded;
        failures = [];
        return true;

        // Each reason once through the service (a silent one writes nothing), and the load's log line.
        bool Failed(string[] reasons, out IReadOnlyList<string> result)
        {
            foreach (var reason in reasons)
            {
                kimigayo.WriteLine(DiagnosticSeverity.Error, reason);
            }

            logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
            result = reasons;
            return false;
        }
    }

    /// <summary>Creates an in-memory Application for exactly one source file without reading its contents.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="path">The selected source-file path.</param>
    /// <param name="options">The explicit command-line settings.</param>
    /// <returns>The implicit project.</returns>
    internal static Project CreateFromSource(Kimigayo kimigayo, string path, KimiOptions options)
    {
        var target = options.Target;
        if (string.IsNullOrEmpty(target))
        {
            target = TargetProfile.Host?.Target ?? throw new PlatformNotSupportedException(
                $"An implicit project takes the host's target, and this host ({TargetProfile.HostName}) has none. Specify --Target explicitly for emission to a supported target.");
        }

        var fullPath = Path.GetFullPath(path);
        var project = new Project(kimigayo, new()
        {
            Targets = [target],
            OutputKind = OutputKind.Application,
            Optimization = "O2",
        })
        {
            Directory = Path.GetDirectoryName(fullPath)!,
            Name = Path.GetFileNameWithoutExtension(fullPath),
            KimiOptions = options,
        };
        project.AddKimiFile(fullPath);
        return project;
    }

    #region FieldAndProperty

    private readonly Kimigayo kimigayo;
    private readonly List<CompilationBuildMetadata> buildMetadata = new();
    private List<SourceDocument> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    /// <summary>Gets the prepared input metadata from the most recent build attempt.</summary>
    public IReadOnlyList<CompilationBuildMetadata> BuildMetadata => this.buildMetadata;

    /// <summary>Gets or sets the compiler options inherited from the solution.</summary>
    public KimiOptions KimiOptions { get; set; } = new();

    /// <summary>Gets or sets the project base directory.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Gets or sets the project name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the project-file settings, including targets and Kotonoha references.</summary>
    public ProjectFile ProjectFile { get; private set; }

    internal string? FilePath { get; private set; }

    // The project's input, derived as the summary states: publication writes it into the link manifest, every native build of the pair
    // names it in its record, and run --no-build runs a build only of this input (PLAN G1256).
    internal string? Input => this.FilePath ?? (this.kimiFiles.Count == 1 ? this.kimiFiles.First() : null);

    /// <summary>Gets the discovered source files.</summary>
    internal IReadOnlyCollection<string> KimiFiles => this.kimiFiles;

    internal string? SolutionLanguageVersion { get; set; }

    #endregion

    /// <summary>Initializes a new instance of the <see cref="Project"/> class.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    public Project(Kimigayo kimigayo)
        : this(kimigayo, new()
        {
            Targets = DefaultProjectFile.Targets.ToArray(),
            Alias = DefaultProjectFile.Alias.ToArray(),
            KotonohaArray = DefaultProjectFile.KotonohaArray.ToArray(),
        })
    {
    }

    private Project(Kimigayo kimigayo, ProjectFile projectFile)
    {
        this.kimigayo = kimigayo;
        this.ProjectFile = projectFile;
    }

    /// <summary>Adds generated or in-memory Kimi source text.</summary>
    /// <param name="url">The source URL or path.</param>
    /// <param name="text">The source text.</param>
    public void AddSource(string url, string text)
        => this.AddSource(new SourceDocument(url, text));

    /// <summary>Adds a generated or in-memory source document, parsed with the project's sources in logical declaration order (SPEC 6.2.1).</summary>
    /// <param name="sourceDocument">The source document.</param>
    public void AddSource(SourceDocument sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        this.additionalSource.Add(sourceDocument);
    }

    /// <summary>Adds a Kimi source-file path to this project.</summary>
    /// <param name="path">The source-file path.</param>
    public void AddKimiFile(string path)
    {
        this.kimiFiles.Add(path);
    }

    /// <summary>Checks source semantics without emitting artifacts or invoking native tools.</summary>
    /// <returns>Whether every configured target passes front-end checks.</returns>
    public Task<bool> Check() => this.Check(default);

    /// <summary>Checks source semantics without emitting artifacts or invoking native tools.</summary>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every configured target passes front-end checks.</returns>
    public Task<bool> Check(CancellationToken cancellationToken)
        => this.BuildCore(false, cancellationToken);

    /// <summary>Checks product sources and records a Library build or links an Application. The toolchain boundary (R9-B2): the
    /// toolchain's classification (<see cref="NativeToolchain.IsToolchainFailure"/>) applies only around the toolchain's calls (the
    /// artifact paths and the build's beginning, which takes the output's lock and invalidates the record; the native build or the
    /// Library record), and such a failure fails the build with its message (IMPL 20.3; SPEC 23.2.2). The compiler's steps run between
    /// them, outside both catches, each result with its own fault path; any other exception under the build (the compiler's read of the
    /// checked metadata, the rendering of a toolchain failure, or one the toolchain leaves) ends the build's own result as Faulted
    /// (DIAGNOSTICS rule 9). The output's lock is held from the beginning until the native build or the Library record returns, on every
    /// outcome. The artifact target is selected first: without one the check sequence reports why (its gate), with no lock taken and no
    /// record written.</summary>
    /// <param name="cancellationToken">Cancels the wait for the output's lock, checking, generation and native tool processes.</param>
    /// <returns>Whether this OutputKind's checks and artifacts succeeded.</returns>
    public async Task<bool> Build(CancellationToken cancellationToken = default)
    {
        try
        {
            if (this.ArtifactTarget(out _) is not { } row)
            {
                await this.BuildCore(true, cancellationToken).ConfigureAwait(false);
                return false;
            }

            Action<DiagnosticSeverity, string> report = (severity, message) => this.kimigayo.WriteLine(severity, message);
            ArtifactPaths paths;
            FileStream output;
            try
            {
                paths = ArtifactPaths.Create(this, row);
                output = await NativeToolchain.Begin(paths.Record, report, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
            {
                return this.ToolchainFailed(ex);
            }

            using (output)
            {
                var application = this.ProjectFile.OutputKind == OutputKind.Application;
                if (!await this.BuildCore(application, cancellationToken, paths).ConfigureAwait(false))
                {
                    return false;
                }

                // A Library's record restates the front end's metadata of its one checked target (BuildTargets).
                var metadata = application ? null : this.buildMetadata.Single();
                try
                {
                    if (application)
                    {
                        await NativeToolchain.Build(this, paths, report, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        NativeToolchain.RecordLibrary(this, metadata!, paths, cancellationToken);
                    }
                }
                catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
                {
                    return this.ToolchainFailed(ex);
                }

                return true;
            }
        }
        catch (Exception ex) when (this.Faults(ex, null))
        {
            // The build's own result, after the results finalized at their own points: an empty owner with the fault, finalized over the
            // Emission partition, the last a build's compiler steps reach (a Library's build has none; an empty owner orders no record).
            this.Publish(new DiagnosticOwner(), DiagnosticPartition.Emission, DiagnosticPartition.Emission, null, fault: ex);
            return false;
        }
    }

    /// <summary>Executes the last successfully built Application without rebuilding.</summary>
    /// <param name="cancellationToken">Cancels the child process.</param>
    /// <returns>The application's exit code.</returns>
    public async Task<int> Run(CancellationToken cancellationToken = default)
    {
        // The toolchain's classification around its call only (PLAN G984): a failure before launch fails the run with its message (IMPL 20.3).
        try
        {
            return await NativeToolchain.RunProject(this, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
        {
            this.ToolchainFailed(ex);
            return 1;
        }
    }

    /// <summary>Runs front-end checks and publishes checked LLVM/manifest inputs. Does not invoke LLVM, link, or run.</summary>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every configured target published both artifacts.</returns>
    public Task<bool> Generate(CancellationToken cancellationToken = default)
        => this.BuildCore(true, cancellationToken);

    /// <summary>Checks one unit through the shared check entry (SPEC 23.2.2), with inputs and failures carried by a context.</summary>
    /// <param name="context">The check context; its caller finalizes the diagnostics.</param>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every selected target passes front-end checks.</returns>
    internal bool Check(CheckContext context, CancellationToken cancellationToken)
        => this.OwnResult((cancellationToken, context), context, static (project, s, diagnostics) => project.BuildTargets(false, s.cancellationToken, null, s.context, diagnostics));

    /// <summary>Gets the targets the project's unit selects (IMPL 20.3), the configured ones or the one --Target names; none, with the
    /// failure, when none is configured or the selected one is not. The one selection rule: SelectTargets records it, and the artifact
    /// target (<see cref="ArtifactTarget"/>) and the host rule read it.</summary>
    /// <returns>The selected targets, and the failure when there are none.</returns>
    internal (string[] Targets, string? Failure) SelectedTargets()
    {
        var targets = this.ProjectFile.Targets;
        var selected = this.KimiOptions.Target;
        return targets.Length == 0 ? ([], "At least one compilation target must be configured.") :
            string.IsNullOrEmpty(selected) ? (targets, null) :
            targets.Contains(selected, StringComparer.Ordinal) ? ([selected], null) :
            ([], "The selected target is not configured in this project.");
    }

    /// <summary>Why a project has no artifact target (<see cref="ArtifactTarget"/>): its selection failed (<see cref="SelectedTargets"/>'s
    /// failure), it selects several targets, or its one selected target has no row in the target table, whose artifacts are Unsupported
    /// (SPEC 22 ¶1).</summary>
    internal enum NoArtifactTarget : byte
    {
        /// <summary>The selection failed.</summary>
        Selection,

        /// <summary>Several targets are selected.</summary>
        Several,

        /// <summary>The one selected target has no row, or a staged row (TargetProfile.Staged, until the Tatara plan's B9g).</summary>
        NoRow,
    }

    /// <summary>Gets the artifact target (IMPL 20.3, SPEC 22 ¶1), published once: the row of the one selected target, or none with why.
    /// Its readers keep their own texts: the gate's records (BuildTargets) and Build, which selects it before the output's paths; run's
    /// selection (Solution.PrepareRun) and run (NativeToolchain.RunProject, the row its build used); the test target (PrepareTests).</summary>
    /// <param name="reason">Why there is none, when there is none.</param>
    /// <returns>The artifact target's row, or null.</returns>
    internal TargetProfile? ArtifactTarget(out NoArtifactTarget reason)
    {
        var (targets, failure) = this.SelectedTargets();
        reason = failure is not null ? NoArtifactTarget.Selection : targets.Length > 1 ? NoArtifactTarget.Several : NoArtifactTarget.NoRow;

        // A staged row is not selectable: the membership test is the temporary of TargetProfile.Staged, which B9g deletes with it.
        return reason == NoArtifactTarget.NoRow && TargetProfile.Find(targets[0]) is { } row && Array.IndexOf(TargetProfile.Rows, row) >= 0 ? row : null;
    }

    /// <summary>The host rule of run and test (IMPL 20.3, 20.6.1; SPEC 22 ¶1: running or testing a program for a target whose executables
    /// this host does not run is Unsupported), before any build, generation or record write: the selected targets are the host's row's
    /// (TargetProfile.Host). Otherwise the project's own result (Input partition) records the selection's failure or the location-only
    /// Unsupported_Kd at the project. Run's several selected targets are its command's input error first (Solution.PrepareRun); a test
    /// project's selection is one target (PrepareTests).</summary>
    /// <returns>Whether the project's program may run on this host.</returns>
    internal bool RunsOnHost()
        => this.OwnResult(0, null, static (project, _, diagnostics) =>
        {
            if (project.SelectTargets(diagnostics) is not { } targets)
            {
                return false;
            }

            if (Array.TrueForAll(targets, static x => x == TargetProfile.Host?.Target))
            {
                return true;
            }

            project.Unsupported(diagnostics);
            return false;
        });

    /// <summary>Publishes the IR and manifest of an accepted compilation through the generation entry, or renders the Emission result:
    /// the generation records, the publication failure, or, for an exception publication does not classify (a broken contract code
    /// generation throws among them), the Faulted result as the front end's (DIAGNOSTICS rule 9; DECISIONS R5-M169). Emission is a later
    /// phase with its own result, rendered after the front-end result, for <c>kimi build</c>, <c>emit</c>, <c>run</c> and <c>test</c> alike
    /// (R5 C0a).</summary>
    /// <param name="compilation">The accepted compilation.</param>
    /// <param name="paths">The artifact paths, or null for the project's.</param>
    /// <param name="context">The check context, or null for a command.</param>
    /// <returns>Whether the artifacts were published.</returns>
    internal bool PublishArtifacts(Compilation compilation, ArtifactPaths? paths, CheckContext? context)
    {
        Exception? fault = null;
        try
        {
            if (EmissionArtifacts.Publish(compilation, paths))
            {
                return true;
            }
        }
        catch (Exception ex) when (this.Faults(ex, context))
        {
            fault = ex;
        }

        // The one finalization point of the Emission result (SPEC 23.3).
        this.Publish(compilation.Diagnostics, DiagnosticPartition.Emission, DiagnosticPartition.Emission, context, fault: fault);
        return false;
    }

    // Test sources were read without a handler; a failure is rethrown where the read used to happen.
    private static List<(string Path, SourceContent? Content, Exception? Failure)> ReadTestSources(CheckInputSource inputs, HashSet<string> testSources)
    {
        var reads = new List<(string Path, SourceContent? Content, Exception? Failure)>(testSources.Count);
        foreach (var path in testSources.Order(StringComparer.Ordinal))
        {
            try
            {
                reads.Add((path, inputs.ReadSource(path), null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reads.Add((path, null, ex));
            }
        }

        return reads;
    }

    // Retain the Task exception/cancellation contract at the public boundary. Each target
    // is synchronous; do not build another async state machine around every compilation.
    // paths requests build artifacts, including a Library's record even when emit is false.
    private async Task<bool> BuildCore(bool emit, CancellationToken cancellationToken = default, ArtifactPaths? paths = null)
        => this.OwnResult((emit, cancellationToken, paths), null, static (project, s, diagnostics) => project.BuildTargets(s.emit, s.cancellationToken, s.paths, null, diagnostics));

    // The project's own result, the product's (its configuration, lock and target-selection records) or the test unit's (PrepareTests:
    // its configuration, TestSources, test-setting, test-identity, test-target and lock records), which body reports to its owner: a check
    // request's caller finalizes its own diagnostics; a command renders it here, with the fault of an exception that ends it, and returns
    // default (DIAGNOSTICS rule 9; R9-B2): the project's own checks', a target's report after its results (Emit), or a target's
    // preparation before its front end (BuildTarget's, whose own records are then not rendered: a known limit).
    private TResult? OwnResult<TState, TResult>(TState state, CheckContext? context, Func<Project, TState, DiagnosticOwner, TResult> body)
    {
        var diagnostics = context?.Diagnostics ?? new DiagnosticOwner();
        Exception? fault = null;
        try
        {
            return body(this, state, diagnostics);
        }
        catch (Exception ex) when (this.Faults(ex, context))
        {
            fault = ex;
            return default;
        }
        finally
        {
            this.Publish(diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Input, context, fault: fault);
        }
    }

    private bool BuildTargets(bool emit, CancellationToken cancellationToken, ArtifactPaths? paths, CheckContext? context, DiagnosticOwner diagnostics)
    {
        this.buildMetadata.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = context?.Inputs ?? CheckInputSource.Disk;

        // The configuration is read by the lock and the graph's resolution, and Compilation.Prepare's directory-free copy of its rule would
        // restate it, so a failed one ends the sequence here (a PLAN open issue).
        if (!this.ValidateConfiguration(diagnostics, out var testSources))
        {
            return false;
        }

        // SPEC 23.1.2: every later check reads only its own input and runs whatever another found, each failure its own record; any failure
        // blocks the front end (SPEC 23.2.2). Only the target selection, which leaves no target to prepare, ends the sequence.
        var valid = true;
        if (this.FilePath is { } projectPath && this.ProjectFile.Dependencies.Count == 0)
        {
            // Even an empty dependency declaration must validate an existing lock.
            // Nonempty graphs still require the forthcoming semantic integration.
            var root = new DependencyNode("root", new(projectPath, this.ProjectFile, [], []), -1, string.Empty);
            var empty = new DependencyPartition([root]);
            valid = !this.Reported(diagnostics, DependencyLock.Validate(DependencyLock.PathForProject(projectPath), new(empty, empty), false, inputs));
        }

        if (this.SelectTargets(diagnostics) is not { } targets)
        {
            return false;
        }

        // Artifact production's gate: the artifact target (several selected: their record; one without a row: Unsupported). The
        // selection's own failure ended the sequence above.
        if ((emit || paths is not null) && this.ArtifactTarget(out var reason) is null)
        {
            if (reason == NoArtifactTarget.NoRow)
            {
                this.Unsupported(diagnostics);
            }
            else
            {
                this.Fail(diagnostics, "Artifact production requires one selected target; select one with --Target.");
            }

            valid = false;
        }

        var success = true;
        foreach (var x in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            success &= this.BuildTarget(x, valid, emit, paths, testSources, cancellationToken, null, context);
        }

        return success;
    }

    // The target's part of the one preparation sequence, for a check, a build and a test build (prepared): the graph's resolution and lock
    // (IMPL 20.5), then Compilation.Prepare, which checks the root whatever the graph holds and the graph only when it resolved
    // (SPEC 23.1.2). The target's records are its result's; valid is the project's own checks' verdict, and any failure blocks the front end
    // (SPEC 23.2.2).
    private bool BuildTarget(string target, bool valid, bool emit, ArtifactPaths? paths, HashSet<string>? testSources, CancellationToken cancellationToken, Action<Compilation>? prepared = null, CheckContext? context = null)
    {
        // A command's target owns its diagnostics, so no earlier target's diagnostics can leak into it;
        // a check request shares its caller's owner.
        var diagnostics = context?.Diagnostics ?? new DiagnosticOwner();
        if (this.FilePath is { } file)
        {
            diagnostics.RegisterPath(file); // SPEC 23.1.2: the project file is consumed before every source.
        }

        DependencyPartition? graph = null;
        if (this.FilePath is { } path && (prepared is not null || this.ProjectFile.Dependencies.Count != 0))
        {
            var test = prepared is not null;
            var inputs = context?.Inputs ?? CheckInputSource.Disk;
            var resolution = DependencyResolver.Resolve(path, target, this.ProjectFile.LangVersion ?? this.SolutionLanguageVersion ?? Compilation.CurrentLanguageVersion, cancellationToken, TinyhandSerializer.SerializeToUtf8(this.ProjectFile), test, inputs);
            valid &= !this.Reported(diagnostics, DependencyLock.Validate(DependencyLock.PathForProject(path), resolution, test, inputs));
            graph = test ? resolution.Test : resolution.Product;
        }

        var project = graph is not { IsResolved: true } ? this : new Project(this.kimigayo, graph.Nodes[0].Input.Configuration)
        {
            Name = this.Name,
            Directory = this.Directory,
            KimiOptions = this.KimiOptions,
            SolutionLanguageVersion = this.SolutionLanguageVersion,
            FilePath = this.FilePath,
        };

        var compilation = new Compilation(this.kimigayo, project, diagnostics)
        {
            IsTestBuild = prepared is not null,
            CollectDocumentation = context?.CollectHover == true,
            CollectHover = context?.CollectHover == true,
        };

        var accepted = false;
        var completed = false;
        Exception? fault = null;
        try
        {
            accepted = this.CheckFrontEnd(compilation, target, valid, testSources, graph, prepared, context);
            completed = true;
        }
        catch (Exception ex) when (this.Faults(ex, context))
        {
            fault = ex;
        }
        finally
        {
            // The one finalization point of the front-end result (SPEC 23.3); a target the project's own checks blocked is rejected by
            // the project's records, in their own result.
            this.Publish(compilation.Diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Ownership, context, completed && !accepted && valid, fault);
        }

        return accepted && emit ? this.Emit(compilation, paths, context) : accepted;
    }

    private bool CheckFrontEnd(Compilation compilation, string target, bool valid, HashSet<string>? testSources, DependencyPartition? graph, Action<Compilation>? prepared, CheckContext? context)
    {
        if (!(graph is null ? compilation.Prepare(target) : compilation.Prepare(target, graph)))
        {
            return this.PreparationFailed(compilation.Diagnostics);
        }

        if (!valid)
        {
            return false;
        }

        this.buildMetadata.Add(compilation.BuildMetadata!);

        // SPEC 23.2.2: a preparation that established no source module (Compilation.AnalyzesNoModule) leaves no source to establish or
        // parse and no phase to run; in Test mode the root's TestSources are skipped too (IMPL 21.3.2: its sources are not checked).
        // The check is Completed, rejected by the Error that preparation recorded.
        if (compilation.AnalyzesNoModule)
        {
            if (context is not null)
            {
                context.Compilation = compilation;
            }

            return false;
        }

        var inputs = context?.Inputs ?? CheckInputSource.Disk;

        // SPEC 23.2.2: every file input is read before parsing, and the reads are consumed in
        // command order, so a failure is reported exactly where the command reported it.
        var reads = graph is null ? this.ReadSources(inputs, testSources) : null;
        var testReads = compilation.IsTestBuild && testSources is not null ? ReadTestSources(inputs, testSources) : null;
        var established = true;
        var documents = Array.ConvertAll(compilation.SourceModules, static _ => new List<SourceDocument>());

        if (graph is not null)
        {
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var input = graph.Nodes[i].Input;
                foreach (var source in input.Sources)
                {
                    var path = Path.Combine(Path.GetDirectoryName(input.Path)!, source.LogicalPath);
                    AddInput(path, source.Content, null, i);
                }
            }
        }
        else
        {
            foreach (var (path, content, exception) in reads!)
            {
                AddInput(path, content, exception, 0);
            }
        }

        if (testReads is not null)
        {
            foreach (var (path, content, exception) in testReads)
            {
                AddInput(path, content, exception, 0, isTestOnly: true);
            }
        }

        // SPEC 6.2.1: declarations follow their documents' logical names, not the read order, so each module's ordinary documents
        // (product, TestSources and in-memory ones) are parsed in one call. Parsing precedes the established check, so a read failure
        // still reports the other documents' syntax errors (SPEC 23.1.2).
        documents[0].AddRange(this.additionalSource);
        for (var i = 0; i < documents.Length; i++)
        {
            compilation.SourceModules[i].AddSources(documents[i]);
        }

        // Every already-consumed input failure must have its own explanation before the blocked check returns.
        // No semantic phase runs against a partial set of established source inputs.
        if (!established)
        {
            return this.PreparationFailed(compilation.Diagnostics);
        }

        // IMPL 20.1: every module's source names, the root's and each dependency's, are checked once its sources are established,
        // and each independent failure is its own record (SPEC 23.1.2).
        var namesFailed = false;
        foreach (var module in compilation.SourceModules)
        {
            foreach (var failure in module.CheckNames())
            {
                this.Fail(compilation.Diagnostics, failure);
                namesFailed = true;
            }
        }

        if (namesFailed)
        {
            return this.PreparationFailed(compilation.Diagnostics);
        }

        if (context is not null)
        {
            // SPEC 23.2.2, step 3: every input is established; the front end runs from here on. The check's other Completed end is
            // AnalyzesNoModule's, above.
            context.Compilation = compilation;
        }

        var accepted = compilation.Check(); // The compilation's phase sequence, from Binding to acceptance and a test build's discovery.
        if (accepted)
        {
            prepared?.Invoke(compilation);
        }

        return accepted;

        // Establishes one read input of a module: its document, registered at read time, or its failure.
        void AddInput(string path, SourceContent? content, Exception? failure, int module, bool isTestOnly = false)
        {
            if (failure is not null)
            {
                var desynchronized = failure is DesynchronizedInputException;
                compilation.Diagnostics.Report(DiagnosticPartition.Input, desynchronized ? DiagnosticCode.DocumentDesynchronized_Kd : DiagnosticCode.SourceReadFailed_Kd, path, note: desynchronized ? null : failure.Message);
                established = false;
                return;
            }

            try
            {
                var document = content!.CreateDocument(path, isTestOnly);
                compilation.Diagnostics.AddInput(document, compilation.SourceModules[module]);
                documents[module].Add(document);
            }
            catch (DecoderFallbackException)
            {
                compilation.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.InvalidSourceEncoding_Kd, path);
                established = false;
            }
        }
    }

    private bool Emit(Compilation compilation, ArtifactPaths? paths, CheckContext? context)
    {
        if (!this.PublishArtifacts(compilation, paths, context))
        {
            return false;
        }

        // The compilation's row, which publication has just found (EmissionArtifacts.Publish).
        paths ??= ArtifactPaths.Create(this, TargetProfile.Find(compilation.TargetTriple.Value)!);
        this.kimigayo.WriteLine(DiagnosticSeverity.Information, $"Generated LLVM inputs: {paths.Ir} and {paths.Manifest}; native build has not yet been performed.");
        return true;
    }

    // Reads the product sources in command order before any parsing, keeping each read's content or failure.
    private List<(string Path, SourceContent? Content, Exception? Failure)> ReadSources(CheckInputSource inputs, HashSet<string>? testSources)
    {
        var reads = new List<(string Path, SourceContent? Content, Exception? Failure)>(this.kimiFiles.Count);
        foreach (var path in this.kimiFiles)
        {
            if (testSources?.Contains(Path.GetFullPath(path)) == true)
            {
                continue;
            }

            try
            {
                reads.Add((path, inputs.ReadSource(path), null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reads.Add((path, null, ex));
            }
        }

        return reads;
    }

    // The selection's record: null after it when the selection fails, which leaves no target to prepare.
    private string[]? SelectTargets(DiagnosticOwner diagnostics)
    {
        var (targets, failure) = this.SelectedTargets();
        if (failure is not null)
        {
            this.Fail(diagnostics, failure);
            return null;
        }

        return targets;
    }

    private void Fail(DiagnosticOwner diagnostics, string message)
        => diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, this.FilePath, note: message);

    // SPEC 22 ¶1, DIAGNOSTICS rule 3: a valid project whose target this implementation does not build, run or test: the location-only
    // Unsupported_Kd at the project.
    private void Unsupported(DiagnosticOwner diagnostics)
        => diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.Unsupported_Kd, this.FilePath);

    // SPEC 23.1.2: each failure is its own record; true when there is one.
    private bool Reported(DiagnosticOwner diagnostics, IReadOnlyList<string> failures)
    {
        for (var i = 0; i < failures.Count; i++)
        {
            this.Fail(diagnostics, failures[i]);
        }

        return failures.Count != 0;
    }

    // IMPL 20.1: the configuration with its TestSources compared at the project directory; a project without one is the current
    // directory's.
    private bool ValidateConfiguration(DiagnosticOwner diagnostics, out HashSet<string>? testSources)
    {
        var directory = Path.GetFullPath(this.Directory.Length == 0 ? "." : this.Directory);
        return !this.Reported(diagnostics, DependencyConfiguration.Validate(this.ProjectFile, directory, out testSources));
    }

    // SPEC 23.2.2: input preparation that ends without an Error reports its fallback, in the command and the check entry alike.
    private bool PreparationFailed(DiagnosticOwner diagnostics)
    {
        if (!diagnostics.HasErrorsThrough(DiagnosticPartition.Input))
        {
            this.Fail(diagnostics, "The project inputs could not be established.");
        }

        return false;
    }

    // SPEC 23.2.2, DIAGNOSTICS rule 9: a command renders an exception that ends a result (the project's own checks', the front end's, the
    // Emission result or the build's) as that result's fault, CheckFaulted_Kd, and then fails; a check request's caller (CheckService)
    // handles its own, and cancellation propagates.
    private bool Faults(Exception exception, CheckContext? context)
        => context is null && this.kimigayo.RendersDiagnostics && exception is not OperationCanceledException;

    // IMPL 20.3, SPEC 23.2.2: a toolchain, output or configuration failure fails the build with the toolchain's message.
    private bool ToolchainFailed(Exception exception)
    {
        this.kimigayo.WriteLine(DiagnosticSeverity.Error, exception.Message);
        return false;
    }

    // SPEC 23.3: a command renders each result once it is finalized; a check request's caller finalizes its own.
    private void Publish(DiagnosticOwner diagnostics, DiagnosticPartition first, DiagnosticPartition last, CheckContext? context, bool rejected = false, Exception? fault = null)
    {
        if (context is null && this.kimigayo.RendersDiagnostics)
        {
            DiagnosticResult result;
            try
            {
                // SPEC 23.2.2: an exception keeps the valid records and adds the fault; a violated contract discards them.
                result = fault is DiagnosticContractException contract ? DiagnosticFaults.Create(contract.Fault, contract, this.FilePath) :
                    fault is not null ? DiagnosticFaults.Create(DiagnosticFault.Exception, fault, this.FilePath, diagnostics.Finalize(first, last)) :
                    diagnostics.Finalize(first, last, rejected);
            }
            catch (DiagnosticContractException ex)
            {
                // SPEC 23.2.2: a violated contract discards every partial record and reports the fault.
                result = DiagnosticFaults.Create(ex.Fault, ex, this.FilePath);
            }

            this.kimigayo.Render(result, this.Directory);
        }
    }
}
