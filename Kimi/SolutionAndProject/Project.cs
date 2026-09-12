// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;

/// <summary>
/// Represents one application or library build unit described by a <c>.kimiproj</c> file.
/// </summary>
/// <remarks>A project is the Kimigayo equivalent of a C# project.</remarks>
public partial class Project
{
    /// <summary>Gets the default project-file settings used by implicit projects.</summary>
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = ["x86_64-pc-windows-msvc"];
        projectFile.Alias = ["Kimi.Base",];

        DefaultProjectFile = projectFile;
    }

    /// <summary>Attempts to load a project from a <c>.kimiproj</c> file.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="logger">The load logger.</param>
    /// <param name="path">The project-file path.</param>
    /// <param name="project">The loaded project.</param>
    /// <returns><see langword="true"/> when the project was loaded.</returns>
    public static bool TryCreate(Kimigayo kimigayo, ILogger logger, string path, [MaybeNullWhen(false)] out Project project)
    {
        project = default;
        try
        {
            var utf8 = System.IO.File.ReadAllBytes(path);
            var file = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8);
            if (file is null)
            {
                logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
                return false;
            }

            project = new(kimigayo);
            project.Directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            project.Name = Path.GetFileNameWithoutExtension(path);
            project.ProjectFile = file;
            foreach (var source in System.IO.Directory.EnumerateFiles(project.Directory, "*.kimi", SearchOption.TopDirectoryOnly))
            {
                project.AddKimiFile(source);
            }
        }
        catch
        {
            logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
            return false;
        }

        return true;
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
    public ProjectFile ProjectFile { get; private set; } = new();

    internal string? SolutionLanguageVersion { get; set; }

    #endregion

    /// <summary>Initializes a new instance of the <see cref="Project"/> class.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    public Project(Kimigayo kimigayo)
    {
        this.kimigayo = kimigayo;
        this.ProjectFile = new()
        {
            Targets = DefaultProjectFile.Targets.ToArray(),
            Alias = DefaultProjectFile.Alias.ToArray(),
            KotonohaArray = DefaultProjectFile.KotonohaArray.ToArray(),
        };
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

    /// <summary>Builds this project once for each configured target triple.</summary>
    /// <returns>A task that completes after all configured targets have been attempted.</returns>
    public async Task<bool> Build()
        => await this.BuildCore(false).ConfigureAwait(false);

    /// <summary>Runs front-end checks and publishes checked LLVM/manifest inputs. Does not invoke LLVM, link, or run.</summary>
    /// <returns>Whether every configured target published both artifacts.</returns>
    public async Task<bool> Generate()
        => await this.BuildCore(true).ConfigureAwait(false);

    private async Task<bool> BuildCore(bool emit)
    {
        this.buildMetadata.Clear();
        var targets = this.ProjectFile.Targets.ToArray();
        if (emit && targets.Length == 0)
        {
            this.kimigayo.GlobalDiagnosticCollection.Add(default, DiagnosticCode.GenerationFailed_Kd, "No target is configured.");
            return false;
        }

        var success = true;
        foreach (var x in targets)
        {
            success &= await this.BuildTarget(x, emit).ConfigureAwait(false);
        }

        return success;
    }

    private async Task<bool> BuildTarget(string target, bool emit)
    {
        // Create & Prepare Compilation
        var compilation = new Compilation(this.kimigayo, this);
        // The service retains named diagnostic collections across attempts, but the new compilation
        // must not inherit an earlier target's preparation/publication errors.
        compilation.Kotonoha.DiagnosticCollection.ClearDiagnostic();
        if (!compilation.Prepare(target))
        {
            return false;
        }

        this.buildMetadata.Add(compilation.BuildMetadata!);

        var projectKotonoha = compilation.Kotonoha;

        foreach (var path in this.kimiFiles)
        {
            try
            {
                var sourceDocument = SourceDocument.FromUtf8(path, System.IO.File.ReadAllBytes(path));
                projectKotonoha.AddSource(sourceDocument);
            }
            catch (DecoderFallbackException)
            {
                this.kimigayo.GetOrAddDiagnosticCollection(path).Add(default, DiagnosticCode.InvalidSourceEncoding_Kd);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.kimigayo.GetOrAddDiagnosticCollection(path).Add(default, DiagnosticCode.GenerationFailed_Kd, ex.Message);
                return false;
            }
        }

        foreach (var y in this.additionalSource)
        {
            projectKotonoha.AddSource(y);
        }

        var binding = compilation.Bind();
        compilation.Binding.ReportDiagnostics();
        var startup = compilation.Binding.CheckStartup(this.ProjectFile.OutputKind);
        compilation.Binding.ReportStartupDiagnostics();
        var ownership = compilation.Ownership.Analyze();
        var controlFlow = compilation.Ownership.ControlFlow!;
        controlFlow.ReportDiagnostics();
        compilation.Ownership.ReportDiagnostics();

        var accepted = binding.IsComplete && startup.IsComplete && ownership.IsVerified && !projectKotonoha.HasSourceErrors &&
            !projectKotonoha.DiagnosticCollection.HasErrors;
        if (!accepted || !emit)
        {
            return accepted; // Preserve the legacy front-end-only Build API.
        }

        if (!EmissionArtifacts.Publish(compilation, out var pathIr, out var failure))
        {
            projectKotonoha.DiagnosticCollection.Add(default, DiagnosticCode.GenerationFailed_Kd, failure);
            return false;
        }

        this.kimigayo.WriteLine(DiagnosticSeverity.Information, $"Generated partial Application inputs: {pathIr} and {Path.ChangeExtension(pathIr, ".link.json")}; entry __kimi_start; kernel32 + kimi_backend. LLVM/link/run remain separate.");
        return true;
    }
}
