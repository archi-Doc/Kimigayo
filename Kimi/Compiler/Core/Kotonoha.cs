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

/// <summary>
/// Represents a named source unit and its parsed Koto tree.
/// </summary>
[TinyhandObject]
public sealed partial class Kotonoha
{
    /// <summary>
    /// Gets the diagnostics associated with this source unit.
    /// </summary>
    [IgnoreMember]
    public DiagnosticCollection DiagnosticCollection { get; private set; }

    /// <summary>
    /// Gets the compilation that owns this source unit.
    /// </summary>
    [IgnoreMember]
    public Compilation Compilation { get; private set; }

    /// <summary>
    /// Gets the stable identifier derived from the source unit name.
    /// </summary>
    [Key(0)]
    public uint Id { get; private set; }

    /// <summary>
    /// Gets the source unit name.
    /// </summary>
    [Key(1)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the source unit URL or path.
    /// </summary>
    [Key(2)]
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the root of the parsed Koto tree.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Kotonoha"/> class.
    /// </summary>
    /// <param name="compilation">The owning compilation.</param>
    /// <param name="name">The source unit name.</param>
    /// <param name="url">The source unit URL or path.</param>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Kotonoha"/> class for testing.
    /// </summary>
    /// <param name="compilation">The owning compilation.</param>
    public Kotonoha(Compilation compilation)
        : this(compilation, "test", "test")
    {
    }

    /// <summary>
    /// Restores runtime-only state after deserialization.
    /// </summary>
    /// <param name="compilation">The compilation that will own the restored source unit.</param>
    public void OnDeserialized(Compilation compilation)
    {
        this.DiagnosticCollection = compilation.Kimigayo.GetOrAddDiagnosticCollection(this.Name);
        this.Compilation = compilation;
        this.RootKoto.RestoreAfterDeserialization(new CodeContext(this), default);
    }

    /// <summary>
    /// Returns a display string for this source unit.
    /// </summary>
    /// <returns>The source unit name prefixed by its kind.</returns>
    public override string ToString()
        => $"Kotonoha: {this.Name}";

    /// <summary>
    /// Creates a parsing context for this source unit.
    /// </summary>
    /// <param name="diagnosticCollection">An optional diagnostic destination.</param>
    /// <returns>A new code context.</returns>
    public CodeContext CreateCodeContext(DiagnosticCollection? diagnosticCollection = null)
    {
        return new(this, diagnosticCollection);
    }

    /// <summary>
    /// Attempts to find a Koto node by its identifier.
    /// </summary>
    /// <param name="kotoId">The Koto identifier.</param>
    /// <param name="koto">The matching node, if found.</param>
    /// <returns><see langword="true"/> when a matching node is found.</returns>
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

    /// <summary>
    /// Tokenizes and parses a source document into this source unit.
    /// </summary>
    /// <param name="sourceDocument">The source document to add.</param>
    public void AddSource(SourceDocument sourceDocument)
    {
        var path = sourceDocument.Path;
        if (!string.IsNullOrEmpty(this.Compilation.Project.Directory))
        {
            path = Path.GetRelativePath(this.Compilation.Project.Directory, path);
        }

        var diagnosticCollection = this.Compilation.Kimigayo.GetOrAddDiagnosticCollection(path);
        var tokenizer = new Tokenizer(diagnosticCollection, sourceDocument);
        var codeContext = this.CreateCodeContext(diagnosticCollection);

        // Tokenize
        try
        {
            tokenizer.ReadAll();
            if (this.Compilation.Project.KimiOptions.DumpToken)
            {
                this.DumpToken(sourceDocument.Path, tokenizer.ToReadOnlySequence());
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
            // Attributes and modifiers belong to the declaration that follows them.
            Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
            if (isEnd)
            {
                return;
            }

            if (reader.CurrentTokenKind == TokenKind.Alias)
            {
                reader.Advance();
                var list = KotoHelper.ParseQualifiedNameSegments(ref reader);
                var aliasKoto = new AliasKoto(ref reader, list);
                // if (KotoParser.ResolveIfAttribute(ref reader, aliasKoto))
                if (!reader.IsExcluded)
                {
                    this.RootKoto.AddLast(aliasKoto);
                }

                continue;
            }
            else
            {
                // Let the root group handle every other top-level declaration.
                this.RootKoto.Parse(ref reader, true);
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
            // Token dumps are diagnostic aids and must not stop compilation.
        }
    }
}
