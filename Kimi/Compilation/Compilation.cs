// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimigayo.Language;
using Kimigayo.Language;

namespace Kimigayo.Compilation;

public class Compilation
{
    private readonly KimiControl kimiControl;

    public Project Project { get; }

    public string Target { get; }

    public TargetTriple TargetTriple { get; private set; }

    public Compilation(KimiControl kimiControl, Project project, string target)
    {
        this.kimiControl = kimiControl;
        this.Project = project;
        this.Target = target;
        TargetTripleParser.TryParse(this.Target, out var targetTriple);
        this.TargetTriple = targetTriple;
    }

    public bool Prepare()
    {
        return true;
    }

    public void Build(UrlAndtext urlAndtext)
    {
        var diagnostic = this.kimiControl.GetOrAddFileDiagnostic(urlAndtext.Url);
        var tokenizer = new Tokenizer(diagnostic);
        tokenizer.Initialize(urlAndtext.Text.AsMemory(), 0, 0);
        var fileRoot = new FileRoot(diagnostic);
        var currentIndentLevel = 0;

        var dumpToken = this.Project.SolutionOptions.DumpToken ? new List<Token>() : null;
        while (true)
        {
            // Read token
            var list = tokenizer.Read(ref currentIndentLevel);
            if (list.Count == 0)
            {
                break;
            }

            // Dump token
            if (dumpToken is not null)
            {
                dumpToken.AddRange(list);
                dumpToken.Add(Token.Invalid);
            }

            // Token to Koto
            var tokenReader = new TokenReader(diagnostic, list);
            fileRoot.Parse(ref tokenReader);
        }

        if (dumpToken is not null &&
            Path.ChangeExtension(urlAndtext.Url, Constants.TokenExtension) is { } tokenPath)
        {
            var sb = new StringBuilder();
            foreach (var x in dumpToken)
            {
                if (x.Kind == TokenKind.Invalid)
                {
                    sb.AppendLf();
                }
                else
                {
                    sb.Append(x.ToString());
                }
            }

            try
            {
                File.WriteAllText(tokenPath, sb.ToString());

                // var b = TinyhandSerializer.Serialize(dumpToken);
                // var t = TinyhandSerializer.Deserialize<List<Token>>(b);
            }
            catch
            {
            }
        }
    }
}
