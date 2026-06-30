// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class Kotonoha
{
    [Key(0)]
    public uint Id { get; private set; }

    [Key(1)]
    public string Name { get; private set; } = string.Empty;

    [Key(2)]
    public string Url { get; private set; } = string.Empty;

    [Key(3)]
    public Utf16Hashtable<NamespaceKoto> Namespaces { get; private set; } = new();

    [Key(4)]
    public List<KimiSource> SourceList { get; private set; } = [];

    [IgnoreMember]
    private Koto[] kotoArray = [];

    public Kotonoha(string name, string url)
    {
        this.Name = name;
        this.Id = (uint)XxHash3Slim.Hash64(name);
        this.Url = url;
    }

    public override string ToString()
        => $"Kotonoha: {this.Name}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKoto(ulong id, [MaybeNullWhen(false)] out Koto koto)
    {
        var kotoId = (uint)id;
        if (this.Id != (uint)(id >> 32) ||
            kotoId >= this.kotoArray.Length)
        {
            koto = default;
            return false;
        }

        koto = this.kotoArray[kotoId];
        return true;
    }

    public void AddSource(Compilation compilation, PathAndSource pathAndSource)
    {
        var diagnostic = compilation.KimiControl.GetOrAddFileDiagnostic(pathAndSource.Path);
        var tokenizer = new Tokenizer(diagnostic);
        tokenizer.Initialize(pathAndSource.Source.AsMemory(), 0, 0);
        var fileRoot = new FileRoot(diagnostic);
        var currentIndentLevel = 0;

        var kimiId = this.SourceList.Count;
        var kimiFile = new KimiSource(pathAndSource.Path, [], default);
        this.SourceList.Add(kimiFile);

        var codeContext = compilation.CreateCodeContext();

        // Tokenize
        var dumpToken = compilation.KimiOptions.DumpToken ? new List<Token>() : null;
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
            var tokenReader = new TokenReader(diagnostic, list, codeContext);
            fileRoot.Parse(ref tokenReader);
        }

        if (dumpToken is not null &&
            Path.ChangeExtension(pathAndSource.Path, Constants.TokenExtension) is { } tokenPath)
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
