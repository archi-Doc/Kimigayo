// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

[TinyhandObject]
public sealed partial class Kotonoha
{
    [IgnoreMember]
    public DiagnosticCollection DiagnosticCollection { get; private set; }

    [IgnoreMember]
    public Compilation Compilation { get; private set; }

    [Key(0)]
    public uint Id { get; private set; }

    [Key(1)]
    public string Name { get; private set; } = string.Empty;

    [Key(2)]
    public string Url { get; private set; } = string.Empty;

    [Key(3)]
    public GroupKoto RootKoto { get; private set; }
    // public Utf16Hashtable<NamespaceKoto> Namespaces { get; private set; } = new();

    // [Key(4)]
    // public List<KimiSource> SourceList { get; private set; } = [];

    [IgnoreMember]
    private readonly Utf16Hashtable<GroupKoto> qualifiedNameToGroupKoto = new();

    [IgnoreMember]
    private readonly UInt64Hashtable<Koto> kotoIdToKoto = new();
    // private Koto[] kotoArray = [];

    public Kotonoha(Compilation compilation, string name, string url)
    {
        this.DiagnosticCollection = compilation.Kimigayo.GetOrAddDiagnosticCollection(name);
        this.Compilation = compilation;
        this.Name = name;
        this.Id = (uint)XxHash3Slim.Hash64(name);
        this.Url = url;

        var codeContext = new CodeContext(this);
        this.RootKoto = new(codeContext, default, default);

        // codeContext.CurrentGroup = this.Root;
    }

    public Kotonoha(Compilation compilation)
        : this(compilation, "test", "test")
    {
    }

    public void OnDeserialized(Compilation compilation)
    {
        this.DiagnosticCollection = compilation.Kimigayo.GetOrAddDiagnosticCollection(this.Name);
        this.Compilation = compilation;
    }

    public override string ToString()
        => $"Kotonoha: {this.Name}";

    public CodeContext CreateCodeContext(DiagnosticCollection? diagnosticCollection = null)
    {
        return new(this, diagnosticCollection);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKoto(ulong kotoId, [MaybeNullWhen(false)] out Koto koto)
    {
        return this.kotoIdToKoto.TryGetValue(kotoId, out koto);

        /*var kotoId = (uint)id;
        if (this.Id != (uint)(id >> 32) ||
            kotoId >= this.kotoArray.Length)
        {
            koto = default;
            return false;
        }

        koto = this.kotoArray[kotoId];
        return true;*/
    }

    public void AddSource(PathAndSource pathAndSource)
    {
        var diagnostic = this.Compilation.Kimigayo.GetOrAddDiagnosticCollection(pathAndSource.Path);
        var sourceText = pathAndSource.SourceText.AsSpan();
        var tokenizer = new Tokenizer(diagnostic, sourceText);

        /*var kimiId = this.SourceList.Count;
        var kimiSource = new KimiSource(pathAndSource.Path, [], default);
        this.SourceList.Add(kimiSource);*/

        var codeContext = this.CreateCodeContext(diagnostic);

        // Tokenize
        try
        {
            tokenizer.ReadAll();
            if (this.Compilation.Project.KimiOptions.DumpToken)
            {// Dump token
                this.DumpToken(pathAndSource.Path, tokenizer.ToReadOnlySequence());
            }

            // Token to Koto
            var tokenReader = new TokenReader(codeContext, ref tokenizer);
            this.Parse(ref tokenReader);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private void Parse(ref TokenReader reader)
    {
        while (reader.CanRead)
        {
            // Consume attributes and modifiers
            KotoParser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
            if (isEnd)
            {
                return;
            }

            if (reader.CurrentTokenKind == TokenKind.Alias)
            {// alias
                reader.Advance();
                var list = KotoHelper.ValidateAndGetNamespace2(ref reader);
                var aliasKoto = new AliasKoto(ref reader, list);
                // if (KotoParser.ResolveIfAttribute(ref reader, aliasKoto))
                if (!reader.IsExcluded)
                {
                    this.RootKoto.AddLast(aliasKoto);
                }

                continue;
            }
            else
            {// Delegate processing to CurrentGroup because this token is not a top-level keyword.
                this.RootKoto.Parse(ref reader);
            }
        }
    }

    private void DumpToken(string path, ReadOnlySequence<Token> sequence)
    {
        var tokenPath = Path.ChangeExtension(path, Constants.TokenExtension);
        if (tokenPath is null)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var y in sequence)
        {
            foreach (var x in y.Span)
            {
                if (x.Kind == TokenKind.Separator)
                {
                    sb.AppendLf();
                }
                else
                {
                    sb.Append(x.ToString());
                }
            }
        }

        try
        {
            File.WriteAllText(tokenPath, sb.ToString());
        }
        catch
        {
        }
    }
}
