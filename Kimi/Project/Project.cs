// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

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

    #region FieldAndProperty

    private readonly KimiControl kimiControl;
    private HashSet<string> targets = new();
    private HashSet<string> globalUse = new();
    private List<UrlAndtext> additionalSource = [];

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

    public bool TryReadFile(string file)
    {
        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(file);
        }
        catch
        {
            this.kimiControl.GlobalDiagnostic.Add(Range.FromString(file), Hashed.Project.NotFound, file);
            // this.kimiControl.WriteLine(Hashed.Project.NotFound, file);//
            return false;
        }

        return true;
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
        var reader = new Tokenizer(this.kimiControl, diagnostic);
        reader.Initialize(urlAndtext.Text.AsMemory(), 0, 0);

        var sb = new StringBuilder();
        while (true)
        {
            var r = reader.Read();
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

        File.AppendAllText("C:\\App\\lsp.txt", sb.ToString());

    }

    private void Prepare()
    {
        this.targets = this.ProjectFile.Targets.ToHashSet();
        this.globalUse = this.ProjectFile.Use.ToHashSet();
    }
}
