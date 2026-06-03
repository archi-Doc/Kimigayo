// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public readonly record struct UrlAndtext(string Url, string Text);

public partial class Project
{
    public static readonly ProjectFile DefaultProjectFile;

    static Project()
    {
        var projectFile = new ProjectFile();
        projectFile.Targets = ["x86_64-pc-windows-msvc"];
        projectFile.Use = ["Kimi.Base",];

        DefaultProjectFile = projectFile;
    }

    public static bool TryCreate(KimiControl kimiControl, ILogger logger, string path, [MaybeNullWhen(false)] out Project project)
    {
        project = default;
        try
        {
            var utf8 = File.ReadAllBytes(path);
            var file = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8);
            if (file is null)
            {
                logger?.GetWriter()?.Write(Hashed.Project.NotLoaded, path);
                return false;
            }

            project = new(kimiControl);
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

    private readonly KimiControl kimiControl;
    private HashSet<string> targets = new();
    private HashSet<string> globalUse = new();
    private List<UrlAndtext> additionalSource = [];
    private HashSet<string> kimiFiles = new();

    public string ProjectPath { get; private set; } = string.Empty;

    public ProjectFile ProjectFile { get; private set; } = new();

    #endregion

    public Project(KimiControl kimiControl)
    {
        this.kimiControl = kimiControl;
        this.ProjectFile = DefaultProjectFile;
    }

    public void AddSource(string url, string text)
    {
        this.additionalSource.Add(new(url, text));
    }

    public void AddKimiFile(string path)
    {
        this.kimiFiles.Add(path);
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

    private void Build(UrlAndtext urlAndtext)
    {
        var diagnostic = this.kimiControl.GetOrAddFileDiagnostic(urlAndtext.Url);
        var tokenizer = new Tokenizer(this.kimiControl, diagnostic);
        tokenizer.Initialize(urlAndtext.Text.AsMemory(), 0, 0);

        var sb = new StringBuilder();
        while (true)
        {
            var r = tokenizer.Read();
            if (r.Count == 0)
            {
                break;
            }

            foreach (var x in r.List.Slice(0, r.Count))
            {
                sb.Append(x.ToString());
                sb.Append(", ");
            }

            sb.AppendLine();
        }

        File.WriteAllText("C:\\App\\lsp.txt", sb.ToString());

    }

    private void Prepare()
    {
        this.targets = this.ProjectFile.Targets.ToHashSet();
        this.globalUse = this.ProjectFile.Use.ToHashSet();
    }
}
