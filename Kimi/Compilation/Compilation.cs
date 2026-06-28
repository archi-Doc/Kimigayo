// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Collections;

namespace Kimigayo.Language;

public class Compilation
{
    #region FieldAndProperty

    private readonly KimiControl kimiControl;

    private readonly ProjectFile projectFile;

    public KimiOptions KimiOptions { get; private set; }

    public string Target { get; private set; } = string.Empty;

    public TargetTriple TargetTriple { get; private set; } = TargetTriple.Empty;

    public KotonohaIdentifier[] KotonohaArray { get; private set; } = []

    private Utf16Hashtable<Koto[]> namespaceToKoto = new();

    #endregion

    public Compilation(KimiControl kimiControl, KimiOptions kimiOptions, ProjectFile projectFile)
    {
        this.kimiControl = kimiControl;
        this.KimiOptions = kimiOptions;
        this.projectFile = projectFile;
    }

    public bool Prepare(string target)
    {
        if (!TargetTripleParser.TryParse(target, out var targetTriple))
        {
            return false;
        }

        this.Target = target;
        this.TargetTriple = targetTriple;

        // Prepare Kotonoha

        return true;
    }

    public void Parse(UrlAndtext urlAndtext)
    {
        var diagnostic = this.kimiControl.GetOrAddFileDiagnostic(urlAndtext.Url);
        var tokenizer = new Tokenizer(diagnostic);
        tokenizer.Initialize(urlAndtext.Text.AsMemory(), 0, 0);
        var fileRoot = new FileRoot(diagnostic);
        var currentIndentLevel = 0;

        var dumpToken = this.KimiOptions.DumpToken ? new List<Token>() : null;
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
