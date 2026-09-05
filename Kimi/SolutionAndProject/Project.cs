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
            project.Directory = Path.GetDirectoryName(path) ?? string.Empty;
            project.Name = Path.GetFileNameWithoutExtension(path);
            project.ProjectFile = file;
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
    private List<SourceDocument> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    /// <summary>Gets or sets the compiler options inherited from the solution.</summary>
    public KimiOptions KimiOptions { get; set; } = new();

    /// <summary>Gets or sets the project base directory.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Gets or sets the project name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the project-file settings, including targets and Kotonoha references.</summary>
    public ProjectFile ProjectFile { get; private set; } = new();

    #endregion

    /// <summary>Initializes a new instance of the <see cref="Project"/> class.</summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    public Project(Kimigayo kimigayo)
    {
        this.kimigayo = kimigayo;
        this.ProjectFile = DefaultProjectFile;
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
    {
        var targets = this.ProjectFile.Targets.ToArray();
        var success = true;
        foreach (var x in targets)
        {
            success &= await this.BuildTarget(x).ConfigureAwait(false);
        }

        return success;
    }

    private async Task<bool> BuildTarget(string target)
    {
        // Create & Prepare Compilation
        var compilation = new Compilation(this.kimigayo, this);
        if (!compilation.Prepare(target))
        {
            return false;
        }

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
            catch
            {
            }
        }

        foreach (var y in this.additionalSource)
        {
            projectKotonoha.AddSource(y);
        }

        // Resolve shared let & @Attribute

        // Prepare CodeContext

        // Resolve

        // Mods

        // Validate control flow using facts available before general Binding.
        // Pending obligations are retained by the analysis API for later Binding passes.
        var controlFlow = compilation.AnalyzeControlFlow();
        controlFlow.ReportDiagnostics();

        // Emit

        // Compile

        // Link

        return controlFlow.Issues.Count == 0 && !projectKotonoha.HasSourceErrors &&
            !projectKotonoha.DiagnosticCollection.GetArray().Any(x => x.Entry.Severity == DiagnosticSeverity.Error);
    }
}
