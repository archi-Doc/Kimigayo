// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;

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

    // [Key(4)]
    // public List<KimiSource> SourceList { get; private set; } = [];

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

        /*var kimiId = this.SourceList.Count;
        var kimiSource = new KimiSource(pathAndSource.Path, [], default);
        this.SourceList.Add(kimiSource);*/

        var codeContext = compilation.CreateCodeContext(this);

        // Tokenize
        var tokenBuilder = new TokenSequenceBuilder();
        try
        {
            tokenizer.ReadAll(ref tokenBuilder);
            var tokenSequence = tokenBuilder.ToReadOnlySequence();
            if (compilation.KimiOptions.DumpToken)
            {// Dump token
                this.DumpToken(pathAndSource.Path, tokenSequence);
            }

            // Token to Koto
            var tokenReader = new TokenReader(diagnostic, codeContext, tokenSequence);
            this.Parse(ref tokenReader);
            compilation.Parse(ref tokenReader);
            // fileRoot.Parse(ref tokenReader);
        }
        finally
        {
            tokenBuilder.Dispose();
        }
    }

    private void Parse(ref TokenReader reader)
    {
        while (reader.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }
            else if (token.Kind == TokenKind.Sharp)
            {// @Attribute
                _ = KotoParser.ConsumeAttribute(ref reader);
                /*if (koto is not null)
                {
                    this.CurrentGroup.Add(koto);

                    var sb = new StringBuilder();
                    using var writer = new StringWriter(sb);
                    KotoHelper.Dump(koto, writer);
                    var st = sb.ToString();
                }*/
            }
            else if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                /*if (!this.allowTopLevelKeyword)
                {
                    // goto UnexpectedTopLevelKeyword;
                }*/

                reader.Advance();
                var list = KotoHelper.ValidateAndGetNamespace2(ref reader);
                var aliasKoto = new AliasKoto(ref reader, list);
                this.CurrentGroup.Add(aliasKoto);
                // this.alias.Add(qualifiedName);
            }
            else
            {// Delegate processing to CurrentGroup because this token is not a top-level keyword.
                break;
            }

            this.CurrentGroup.Parse(ref reader);
        }
    }

    private void DumpToken(string path, ReadOnlySequence<Token> sequence)
    {
        if (Path.ChangeExtension(path, Constants.TokenExtension) is { } tokenPath)
        {
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
}
