// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Kimi.Checking;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;

/// <summary>
/// Represents an application or library build unit, configured by a project file or a single-source input.
/// </summary>
/// <remarks>A project is the Kimigayo equivalent of a C# project.</remarks>
public partial class Project
{
    /// <summary>Gets the default settings for programmatically constructed projects.</summary>
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = ["x86_64-pc-windows-msvc"];

        DefaultProjectFile = projectFile;
    }

    /// <summary>Attempts to load a project from a <c>.kimiproj</c> file.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="logger">The load logger.</param>
    /// <param name="path">The project-file path.</param>
    /// <param name="project">The loaded project.</param>
    /// <returns><see langword="true"/> when the project was loaded.</returns>
    public static bool TryCreate(Kimigayo kimigayo, ILogger? logger, string path, [MaybeNullWhen(false)] out Project project)
        => TryCreate(kimigayo, logger, path, CheckInputSource.Disk, out project, out _);

    /// <summary>Attempts to load a project from a <c>.kimiproj</c> file through an input source.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="logger">The load logger.</param>
    /// <param name="path">The project-file path.</param>
    /// <param name="inputs">The input source; the language server supplies open documents.</param>
    /// <param name="project">The loaded project.</param>
    /// <param name="failure">The reason the project did not load.</param>
    /// <returns><see langword="true"/> when the project was loaded.</returns>
    internal static bool TryCreate(Kimigayo kimigayo, ILogger? logger, string path, CheckInputSource inputs, [MaybeNullWhen(false)] out Project project, out string? failure)
    {
        project = default;
        failure = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var file = ProjectFile.Load(inputs.ReadAllBytes(fullPath));
            if (file is null)
            {
                failure = $"Empty project configuration '{path}'.";
                logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
                return false;
            }

            project = new(kimigayo, file);
            project.FilePath = fullPath;
            project.Directory = Path.GetDirectoryName(fullPath)!;
            project.Name = Path.GetFileNameWithoutExtension(path);
            foreach (var source in inputs.GetFiles(project.Directory, "*.kimi"))
            {
                project.AddKimiFile(source);
            }
        }
        catch (TinyhandException ex)
        {
            failure = $"Invalid project configuration '{path}': {ex.Message}";
            kimigayo.WriteLine(DiagnosticSeverity.Error, failure);
            project = default;
            return false;
        }
        catch (Exception ex) when (ex is not PendingInputException)
        {
            failure = $"The project '{path}' could not be loaded: {ex.Message}";
            logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
            project = default;
            return false;
        }

        return true;
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
            if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("Implicit projects currently support only the Windows x64 host target. Specify --Target explicitly for emission to a supported target.");
            }

            target = WindowsProfile.Target;
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

    /// <summary>Adds a generated or in-memory source document.</summary>
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

    /// <summary>Generates LLVM inputs, verifies them and links a native Application.</summary>
    /// <param name="cancellationToken">Cancels generation and native tool processes.</param>
    /// <returns>Whether a new native executable was built successfully.</returns>
    public async Task<bool> Build(CancellationToken cancellationToken = default)
    {
        try
        {
            var paths = ArtifactPaths.Create(this);
            NativeToolchain.Invalidate(paths);
            if (!await this.BuildCore(true, cancellationToken, paths).ConfigureAwait(false))
            {
                return false;
            }

            await NativeToolchain.Build(this, paths, (severity, message) => this.kimigayo.WriteLine(severity, message), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
        {
            this.kimigayo.WriteLine(DiagnosticSeverity.Error, ex.Message);
            return false;
        }
    }

    /// <summary>Executes the last successfully built Application without rebuilding.</summary>
    /// <param name="cancellationToken">Cancels the child process.</param>
    /// <returns>The application's exit code.</returns>
    public Task<int> Run(CancellationToken cancellationToken = default)
        => NativeToolchain.RunProject(this, cancellationToken);

    /// <summary>Runs front-end checks and publishes checked LLVM/manifest inputs. Does not invoke LLVM, link, or run.</summary>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every configured target published both artifacts.</returns>
    public Task<bool> Generate(CancellationToken cancellationToken = default)
        => this.BuildCore(true, cancellationToken);

    /// <summary>Checks one unit through the shared check entry (SPEC 23.3.2), with inputs and failures carried by a context.</summary>
    /// <param name="context">The check context; its caller finalizes the diagnostics.</param>
    /// <param name="cancellationToken">Cancels between compilation targets.</param>
    /// <returns>Whether every selected target passes front-end checks.</returns>
    internal bool Check(CheckContext context, CancellationToken cancellationToken)
        => this.BuildTargetsAndPublish(false, cancellationToken, null, context);

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
    private async Task<bool> BuildCore(bool emit, CancellationToken cancellationToken = default, ArtifactPaths? paths = null)
        => this.BuildTargetsAndPublish(emit, cancellationToken, paths, null);

    private bool BuildTargetsAndPublish(bool emit, CancellationToken cancellationToken, ArtifactPaths? paths, CheckContext? context)
    {
        // A check request's caller finalizes its own diagnostics; a command renders the preparation result here.
        var diagnostics = context?.Diagnostics ?? new DiagnosticOwner();
        try
        {
            return this.BuildTargets(emit, cancellationToken, paths, context, diagnostics);
        }
        finally
        {
            this.Publish(diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Input, context);
        }
    }

    private bool BuildTargets(bool emit, CancellationToken cancellationToken, ArtifactPaths? paths, CheckContext? context, DiagnosticOwner diagnostics)
    {
        this.buildMetadata.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = context?.Inputs ?? CheckInputSource.Disk;
        if (DependencyConfiguration.Validate(this.ProjectFile) is { } configurationFailure)
        {
            this.Fail(diagnostics, configurationFailure);
            return false;
        }

        if (this.FilePath is { } projectPath && this.ProjectFile.Dependencies.Count == 0)
        {
            // Even an empty dependency declaration must validate an existing lock.
            // Nonempty graphs still require the forthcoming semantic integration.
            var root = new DependencyNode("root", new(projectPath, this.ProjectFile, [], []), -1, string.Empty);
            var empty = new DependencyPartition([root]);
            var failure = DependencyLock.Validate(DependencyLock.PathForProject(projectPath), new(empty, empty), false, inputs);
            if (failure is not null)
            {
                this.Fail(diagnostics, failure);
                return false;
            }
        }

        HashSet<string>? testSources = null;
        if (this.ProjectFile.TestSources.Length != 0)
        {
            testSources = new(SourceIdentity.PathComparer);
            try
            {
                var baseDirectory = Path.GetFullPath(this.Directory.Length == 0 ? "." : this.Directory);
                foreach (var source in this.ProjectFile.TestSources)
                {
                    if (!testSources.Add(Path.GetFullPath(source, baseDirectory)))
                    {
                        this.Fail(diagnostics, $"Duplicate resolved TestSources path: {source}");
                        return false;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                this.Fail(diagnostics, $"Invalid TestSources path: {ex.Message}");
                return false;
            }
        }

        var targets = this.ProjectFile.Targets;
        if (targets.Length == 0)
        {
            this.Fail(diagnostics, "At least one compilation target must be configured.");
            return false;
        }

        if (!string.IsNullOrEmpty(this.KimiOptions.Target))
        {
            if (!targets.Contains(this.KimiOptions.Target, StringComparer.Ordinal))
            {
                this.Fail(diagnostics, "The selected target is not configured in this project.");
                return false;
            }

            targets = [this.KimiOptions.Target];
        }

        if (emit && (targets.Length != 1 || targets[0] != WindowsProfile.Target))
        {
            this.Fail(diagnostics, "Emission currently requires exactly one configured Windows x64 target.");
            return false;
        }

        var success = true;
        var rootConfiguration = this.FilePath is not null && this.ProjectFile.Dependencies.Count != 0 ? TinyhandSerializer.SerializeToUtf8(this.ProjectFile) : null;
        foreach (var x in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DependencyPartition? graph = null;
            if (rootConfiguration is not null)
            {
                var resolution = DependencyResolver.Resolve(this.FilePath!, x, this.ProjectFile.LangVersion ?? this.SolutionLanguageVersion ?? Compilation.CurrentLanguageVersion, cancellationToken, rootConfiguration, false, inputs);
                var failure = DependencyLock.Validate(DependencyLock.PathForProject(this.FilePath!), resolution, false, inputs);
                if (failure is not null)
                {
                    this.Fail(diagnostics, failure);
                    success = false;
                    continue;
                }

                graph = resolution.Product;
            }

            success &= this.BuildTarget(x, emit, paths, testSources, graph, null, context);
        }

        return success;
    }

    private bool BuildTarget(string target, bool emit, ArtifactPaths? paths, HashSet<string>? testSources, DependencyPartition? graph, Action<Compilation>? prepared = null, CheckContext? context = null)
    {
        // Create & Prepare Compilation
        var project = graph is null ? this : new Project(this.kimigayo, graph.Nodes[0].Input.Configuration)
        {
            Name = this.Name,
            Directory = this.Directory,
            KimiOptions = this.KimiOptions,
            SolutionLanguageVersion = this.SolutionLanguageVersion,
            FilePath = this.FilePath,
        };

        // A command's target owns its diagnostics, so no earlier target's diagnostics can leak into it;
        // a check request shares its caller's owner.
        var compilation = new Compilation(this.kimigayo, project, context?.Diagnostics)
        {
            IsTestBuild = prepared is not null,
            CollectDocumentation = context?.CollectHover == true,
            CollectHover = context?.CollectHover == true,
        };
        if (this.FilePath is { } file)
        {
            compilation.Diagnostics.RegisterPath(file); // SPEC 23.3.6.3: the project file is consumed before every source.
        }

        var accepted = false;
        var completed = false;
        Exception? fault = null;
        try
        {
            accepted = this.CheckFrontEnd(compilation, target, testSources, graph, prepared, context);
            completed = true;
        }
        catch (Exception ex) when (context is null && this.kimigayo.RendersDiagnostics && ex is not OperationCanceledException)
        {
            // SPEC 23.3.3: a command renders the Faulted result the check entry publishes; the command then fails.
            fault = ex;
        }
        finally
        {
            // The one finalization point of the front-end result (SPEC 23.3.6.8).
            this.Publish(compilation.Diagnostics, DiagnosticPartition.Input, DiagnosticPartition.Ownership, context, completed && !accepted, fault);
        }

        return accepted && emit ? this.Emit(compilation, paths, context) : accepted;
    }

    private bool CheckFrontEnd(Compilation compilation, string target, HashSet<string>? testSources, DependencyPartition? graph, Action<Compilation>? prepared, CheckContext? context)
    {
        if (!(graph is null ? compilation.Prepare(target) : compilation.Prepare(target, graph)))
        {
            return this.PreparationFailed(compilation.Diagnostics);
        }

        this.buildMetadata.Add(compilation.BuildMetadata!);

        var projectKotonoha = compilation.Kotonoha;
        var inputs = context?.Inputs ?? CheckInputSource.Disk;

        // SPEC 23.3.2: every file input is read before parsing, and the reads are consumed in
        // command order, so a failure is reported exactly where the command reported it.
        var reads = graph is null ? this.ReadSources(inputs, testSources) : null;
        var testReads = compilation.IsTestBuild && testSources is not null ? ReadTestSources(inputs, testSources) : null;
        var established = true;

        if (graph is not null)
        {
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var input = graph.Nodes[i].Input;
                foreach (var source in input.Sources)
                {
                    var path = Path.Combine(Path.GetDirectoryName(input.Path)!, source.LogicalPath);
                    AddSource(path, source.Content, null, compilation.SourceModules[i]);
                }
            }
        }
        else
        {
            foreach (var (path, content, exception) in reads!)
            {
                AddSource(path, content, exception, projectKotonoha);
            }
        }

        if (testReads is not null)
        {
            foreach (var (path, content, exception) in testReads)
            {
                AddSource(path, content, exception, projectKotonoha, isTestOnly: true);
            }
        }

        // Every already-consumed input failure must have its own explanation before the blocked check returns.
        // No semantic phase runs against a partial set of established source inputs.
        if (!established)
        {
            return this.PreparationFailed(compilation.Diagnostics);
        }

        foreach (var y in this.additionalSource)
        {
            projectKotonoha.AddSource(y);
        }

        if (context is not null)
        {
            context.Compilation = compilation; // SPEC 23.3.2, step 3: every input is established; the front end runs from here on.
        }

        var binding = compilation.Bind();
        compilation.Binding.ReportDiagnostics();
        var startup = compilation.IsTestBuild ? compilation.Binding.CheckTestStartup() : compilation.Binding.CheckStartup(this.ProjectFile.OutputKind);
        compilation.Binding.ReportStartupDiagnostics();
        var ownership = compilation.Ownership.Analyze();
        var controlFlow = compilation.Ownership.ControlFlow!;
        controlFlow.ReportDiagnostics();
        compilation.Ownership.ReportDiagnostics();

        // SPEC 23.3.3: acceptance reads the error state of every front-end partition, never the displayed list.
        var accepted = binding.IsComplete && startup.IsComplete && ownership.IsVerified && !compilation.Diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership);
        if (accepted && prepared is not null)
        {
            compilation.Tests.Discover(compilation);
            prepared(compilation);
        }

        return accepted;

        void AddSource(string path, SourceContent? content, Exception? failure, Kotonoha module, bool isTestOnly = false)
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
                compilation.Diagnostics.AddInput(document, module);
                module.AddSource(document);
            }
            catch (DecoderFallbackException)
            {
                compilation.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.InvalidSourceEncoding_Kd, path);
                established = false;
            }
        }
    }

    // Emission is a later phase with its own result, rendered after the front-end result.
    private bool Emit(Compilation compilation, ArtifactPaths? paths, CheckContext? context)
    {
        if (!EmissionArtifacts.Publish(compilation, paths, out var pathIr, out var failure))
        {
            // SPEC 21.3.5: an exceeded mandatory generation limit is a resource diagnostic, not a semantic error.
            compilation.Emission.ReportFailure(failure);
            this.Publish(compilation.Diagnostics, DiagnosticPartition.Emission, DiagnosticPartition.Emission, context);
            return false;
        }

        this.kimigayo.WriteLine(DiagnosticSeverity.Information, $"Generated LLVM inputs: {pathIr} and {Path.ChangeExtension(pathIr, ".link.json")}; native build has not yet been performed.");
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

    private void Fail(DiagnosticOwner diagnostics, string message)
        => diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, this.FilePath, note: message);

    // SPEC 23.3.3: input preparation that ends without an Error reports its fallback, in the command and the check entry alike.
    private bool PreparationFailed(DiagnosticOwner diagnostics)
    {
        if (!diagnostics.HasErrorsThrough(DiagnosticPartition.Input))
        {
            this.Fail(diagnostics, "The project inputs could not be established.");
        }

        return false;
    }

    // SPEC 23.3.6.8: a command renders each result once it is finalized; a check request's caller finalizes its own.
    private void Publish(DiagnosticOwner diagnostics, DiagnosticPartition first, DiagnosticPartition last, CheckContext? context, bool rejected = false, Exception? fault = null)
    {
        if (context is null && this.kimigayo.RendersDiagnostics)
        {
            DiagnosticResult result;
            try
            {
                // SPEC 23.3.3: an exception keeps the valid records and adds the fault; a violated contract discards them.
                result = fault is DiagnosticContractException contract ? DiagnosticFaults.Create(contract.Fault, contract.Message, this.FilePath) :
                    fault is not null ? DiagnosticFaults.Create(DiagnosticFault.Exception, fault.Message, this.FilePath, diagnostics.Finalize(first, last)) :
                    diagnostics.Finalize(first, last, rejected);
            }
            catch (DiagnosticContractException ex)
            {
                // SPEC 23.3.3: a violated contract discards every partial record and reports the fault.
                result = DiagnosticFaults.Create(ex.Fault, ex.Message, this.FilePath);
            }

            this.kimigayo.Render(result, this.Directory);
        }
    }
}
