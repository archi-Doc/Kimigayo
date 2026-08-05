// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

[TinyhandObject]
public sealed partial class Kotonoha
{
    [IgnoreMember]
    public Compilation Compilation { get; }

    [Key(0)]
    public uint Id { get; private set; }

    [Key(1)]
    public string Name { get; private set; } = string.Empty;

    [Key(2)]
    public string Url { get; private set; } = string.Empty;

    [Key(3)]
    public GroupKoto Root { get; private set; }
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
        var codeContext = new CodeContext(compilation, this);

        this.Compilation = compilation;
        this.Name = name;
        this.Id = (uint)XxHash3Slim.Hash64(name);
        this.Url = url;
        this.Root = new(codeContext, default, default);

        codeContext.CurrentGroup = this.Root;
    }

    public Kotonoha(Compilation compilation)
    {
        var codeContext = new CodeContext(compilation, this);
        this.Compilation = compilation;
        this.Root = new(codeContext, default, default);
    }

    public override string ToString()
        => $"Kotonoha: {this.Name}";

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
        var diagnostic = this.Compilation.KimiControl.GetOrAddDiagnosticCollection(pathAndSource.Path);
        var sourceText = pathAndSource.SourceText.AsMemory();
        var tokenizer = new Tokenizer(diagnostic);
        tokenizer.Initialize(sourceText, 0, 0);

        /*var kimiId = this.SourceList.Count;
        var kimiSource = new KimiSource(pathAndSource.Path, [], default);
        this.SourceList.Add(kimiSource);*/

        var codeContext = this.Compilation.CreateCodeContext(this);

        // Tokenize
        var tokenBuilder = new TokenSequenceBuilder();
        try
        {
            tokenizer.ReadAll(ref tokenBuilder);
            var tokenSequence = tokenBuilder.ToReadOnlySequence();
            if (this.Compilation.Project.KimiOptions.DumpToken)
            {// Dump token
                this.DumpToken(pathAndSource.Path, tokenSequence);
            }

            // Token to Koto
            var tokenReader = new TokenReader(diagnostic, codeContext, tokenSequence);
            this.Parse(ref tokenReader);
        }
        finally
        {
            tokenBuilder.Dispose();
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

            if (reader.CurrentToken.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                reader.Advance();
                var list = KotoHelper.ValidateAndGetNamespace2(ref reader);
                var aliasKoto = new AliasKoto(ref reader, list);
                if (KotoParser.ResolveIfAttribute(ref reader, aliasKoto))
                {
                    this.Root.AddLast(aliasKoto);
                }

                continue;
            }
            else
            {// Delegate processing to CurrentGroup because this token is not a top-level keyword.
                this.Root.Parse(ref reader, true);
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
