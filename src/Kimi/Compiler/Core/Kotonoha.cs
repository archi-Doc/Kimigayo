// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns one source unit (a module): its documents, merged syntax tree and diagnostics, its sources' logical names (IMPL 20.1) and
// their logical declaration order (SPEC 6.2.1).
// Input: source documents (disk, in-memory, deserialized, generated) and its compilation's project. Output: RootKoto, Syntax,
// DiagnosticCollection, Base (the directory its logical names are relative to), LogicalName(document), which every reader of a
// source's name asks (the location table, the test catalog, documentation, hover, the collection name), and SourceDocuments in
// logical declaration order, the order RootKoto merges their declarations in.
// Invariants: AddSources records and parses one call's documents sorted by logical name under UnicodeNameHelper.ScalarOrder (stable,
// so equal names, which CheckNames reports, keep their given order), after every earlier call's; a producer adds a module's ordinary
// sources in one call, so a generated fragment (CodeContext.Parse, recorded later) follows them all.
// A name is a pure function of the document's path and Base; Base is stored and recomputed only at a change point
// (creation, OnDeserialized, a new Project.Directory value, which the getter detects by reference); a root module whose project has
// no directory takes the current directory its compilation captured when created; names are memoized until Base is recomputed or
// the source analysis is revoked, so a warm read allocates nothing. Must not read the process's current directory itself, and no
// Path call it makes for a name throws.

using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Represents a named library source unit built from one or more Kimi source documents.
/// </summary>
/// <remarks>
/// A Kotonoha is the compiler's library boundary. It owns one merged Koto syntax tree,
/// diagnostics, and any generated function used to contain executable top-level syntax.
/// A project's application output is represented by its primary Kotonoha; referenced
/// libraries are represented by additional Kotonoha instances.
/// Serialization stores the original source documents, including their paths and text.
/// Syntax-tree edits are not persisted; <see cref="OnDeserialized"/> reparses the documents.
/// </remarks>
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
    [IgnoreMember]
    public GroupKoto RootKoto { get; private set; }

    /// <summary>Gets the SyntaxId numbering of this source unit's syntax.</summary>
    [IgnoreMember]
    public SyntaxTable Syntax { get; private set; } = new();

    /// <summary>
    /// Gets the generated function that owns executable top-level syntax.
    /// </summary>
    [IgnoreMember]
    public FunctionKoto? GeneratedFunction { get; private set; }

    /// <summary>Gets the original source documents in logical declaration order (SPEC 6.2.1), which is their parsing order: each
    /// <see cref="AddSources"/> call's documents by logical name, after every earlier call's.</summary>
    [IgnoreMember]
    public IReadOnlyList<SourceDocument> SourceDocuments => this.sourceDocuments;

    /// <summary>Gets the syntax Errors at which the parser skipped a declaration of this source unit; a check over the member set rests on them.</summary>
    [IgnoreMember]
    internal IReadOnlyList<DiagnosticKey>? OmittedDeclarations => this.omittedDeclarations;

    /// <summary>Gets the base of this module's logical source names (IMPL 20.1): its project's directory, normalized; for a
    /// module without a <see cref="Url"/> (the root) and a project without a directory, the current directory its compilation
    /// captured when created; empty for a module whose <see cref="Url"/> is not a full path (library, <c>$abort</c>, tests).
    /// It is stored and recomputed only at a change point: creation, <see cref="OnDeserialized"/>, and a new
    /// <see cref="Project.Directory"/> value, which this getter itself detects by reference, so no reader sees a stale base.</summary>
    [IgnoreMember]
    internal string Base
    {
        get
        {
            if (this.Url.Length == 0 && !ReferenceEquals(this.baseSource, this.Compilation.Project.Directory))
            {
                this.SetBase();
            }

            return this.baseDirectory;
        }
    }

    /// <summary>Gets the name hover shows for a source, asked live: its logical name, but none for a built-in (<c>compiler://</c>) or
    /// Mod-generated document, which hover identifies otherwise. No path makes it throw.</summary>
    /// <param name="document">A document of this module.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    internal string? HoverSourceName(SourceDocument document)
    {
        if (document.Path.StartsWith(Checking.SourceIdentity.BuiltInPrefix, StringComparison.Ordinal) ||
            this.generatedSourceLocations?.ContainsKey(this.sourceDocuments.IndexOf(document)) == true)
        {
            return null;
        }

        return this.LogicalName(document);
    }

    /// <summary>Gets a document's logical name (IMPL 20.1): a fully qualified path under <see cref="Base"/> is its remainder, any other
    /// path (outside it, another drive, relative, <c>compiler://</c>) its own text, each with the host's separators as <c>/</c>.
    /// Memoized per document until the base is recomputed or the source analysis is revoked; no path makes it throw.</summary>
    /// <param name="document">A document of this module.</param>
    /// <returns>The logical name.</returns>
    internal string LogicalName(SourceDocument document)
    {
        var directory = this.Base; // First: a new base empties the memo.
        var names = this.names ??= new(ReferenceEqualityComparer.Instance);
        if (!names.TryGetValue(document, out var name))
        {
            var start = Checking.SourceIdentity.Under(document.Path, directory);
            name = start == 0 ? document.Path : document.Path[start..];
            name = Path.DirectorySeparatorChar == '/' ? name : name.Replace(Path.DirectorySeparatorChar, '/');
            names.Add(document, name);
        }

        return name;
    }

    /// <summary>Forgets the memoized names when the source analysis is revoked, so the memo holds only current documents.</summary>
    internal void ForgetNames()
    {
        this.names?.Clear();
        this.nameFailures = null;
    }

    /// <summary>Checks this module's source names (IMPL 20.1) and reports each independent failure (SPEC 23.1.2): every name not in NFC,
    /// and every group of names equal under simple case folding, twins included, as one failure naming the whole group; a name not in
    /// NFC joins no group. Memoized with the names and emptied when a document is recorded, so a repeated check allocates nothing.</summary>
    /// <returns>The failures, as project preparation notes naming this module, in document order; empty when the names are valid.</returns>
    internal IReadOnlyList<string> CheckNames()
    {
        _ = this.Base; // First: a new base empties the memo.
        if (this.nameFailures is null)
        {
            List<string>? failures = null;
            var first = new Dictionary<string, string>(this.sourceDocuments.Count, Helper.UnicodeNameHelper.SimpleCaseFolding);
            Dictionary<string, List<string>>? groups = null;
            foreach (var document in this.sourceDocuments)
            {
                var name = this.LogicalName(document);
                if (!Helper.UnicodeNameHelper.IsNfc(name))
                {
                    (failures ??= []).Add($"Module '{this.Name}': source name '{name}' is not in Unicode Normalization Form C (IMPL 20.1).");
                }
                else if (!first.TryAdd(name, name))
                {
                    var key = first[name];
                    if (!(groups ??= new(StringComparer.Ordinal)).TryGetValue(key, out var group))
                    {
                        groups.Add(key, group = [key]);
                    }

                    group.Add(name);
                }
            }

            foreach (var group in groups?.Values ?? Enumerable.Empty<List<string>>())
            {
                var names = string.Join(", ", group.Select(static x => $"'{x}'"));
                (failures ??= []).Add($"Module '{this.Name}': source names {names} are equal under simple case folding (IMPL 20.1).");
            }

            this.nameFailures = failures?.ToArray() ?? [];
        }

        return this.nameFailures;
    }

    [IgnoreMember]
    private string baseDirectory = string.Empty;

    [IgnoreMember]
    private string? baseSource;

    [IgnoreMember]
    private Dictionary<SourceDocument, string>? names;

    [IgnoreMember]
    private string[]? nameFailures;

    private List<DocumentationSource>? documentationSources;

    [IgnoreMember]
    private List<DiagnosticKey>? omittedDeclarations;

    /// <summary>Gets optional source-backed documentation, including generated source parses.</summary>
    [IgnoreMember]
    public IReadOnlyList<DocumentationSource> DocumentationSources => (IReadOnlyList<DocumentationSource>?)this.documentationSources ?? [];

    internal void RecordDocumentation(DocumentationSource? documentation)
    {
        if (documentation is not null)
        {
            try
            {
                (this.documentationSources ??= new()).Add(documentation);
            }
            catch (Exception ex) when (this.Compilation.CollectHover && Compilation.OptionalHoverFailure(ex))
            {
                this.Compilation.RecordDocumentationFailure(documentation.Source, ex.Message);
            }
        }
    }

    [Key(3)]
    private List<SourceDocument> sourceDocuments = new();

    [Key(5)]
    private int sourceFormat;

    [Key(6)]
    private string? sourceLanguageVersion;

    [Key(7)]
    private string? sourceCompilerVersion;

    [Key(4)]
    private Dictionary<int, (string ModId, int AdditionOrder)>? generatedSourceLocations;

    /// <summary>
    /// Initializes a new instance of the <see cref="Kotonoha"/> class.
    /// </summary>
    /// <param name="compilation">The owning compilation.</param>
    /// <param name="name">The source unit name.</param>
    /// <param name="url">The source unit URL or path.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public Kotonoha(Compilation compilation, string name, string url)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(url);

        this.DiagnosticCollection = compilation.Diagnostics.GetOrAddCollection(name, this);
        this.Compilation = compilation;
        this.sourceFormat = 1;
        this.sourceLanguageVersion = Compilation.CurrentLanguageVersion;
        this.sourceCompilerVersion = Compilation.CompilerVersion;
        this.Name = name;
        this.Id = (uint)XxHash3Slim.Hash64(name);
        this.Url = url;
        this.SetBase();

        var codeContext = new CodeContext(this);
        this.RootKoto = new(codeContext, default, default);
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
    /// Rebuilds the syntax tree from the saved source documents using the supplied compilation.
    /// </summary>
    /// <param name="compilation">The compilation that will own the restored source unit.</param>
    public void OnDeserialized(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        // Even an empty snapshot replaces the tree that earlier analyses certified.
        compilation.InvalidateSourceAnalysis();

        this.DiagnosticCollection = compilation.Diagnostics.GetOrAddCollection(this.Name, this);
        compilation.Diagnostics.InvalidateSyntax(this);
        this.Compilation = compilation;
        this.SetBase(); // A change point: the compilation, so its project, and the deserialized Url are new.
        this.RootKoto = new(new CodeContext(this), default, default);
        this.Syntax = new();
        this.GeneratedFunction = null;
        this.documentationSources = null;

        if (this.sourceFormat != 1 || this.sourceLanguageVersion != Compilation.CurrentLanguageVersion ||
            this.sourceCompilerVersion != Compilation.CompilerVersion)
        {
            compilation.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.IncompatibleSerializedSource_Kd, null);
            return;
        }

        for (var i = 0; i < this.sourceDocuments.Count; i++)
        {
            var location = this.generatedSourceLocations?.GetValueOrDefault(i) ?? default;
            this.ParseSource(this.sourceDocuments[i], location.ModId, location.AdditionOrder);
        }
    }

    /// <summary>
    /// Returns a display string for this source unit.
    /// </summary>
    /// <returns>The source unit name prefixed by its kind.</returns>
    public override string ToString()
        => $"Kotonoha: {this.Name}";

    /// <summary>
    /// Gets a source-less parsing context for this source unit.
    /// </summary>
    /// <param name="diagnosticCollection">An optional diagnostic destination.</param>
    /// <returns>The shared root context, or a new context for a different diagnostic destination.</returns>
    public CodeContext CreateCodeContext(DiagnosticCollection? diagnosticCollection = null)
        => diagnosticCollection is null || ReferenceEquals(diagnosticCollection, this.DiagnosticCollection)
            ? this.RootKoto.CodeContext
            : new(this, diagnosticCollection);

    /// <summary>
    /// Tokenizes and parses a source document into this source unit, after every document added earlier;
    /// the one-document form of <see cref="AddSources"/>.
    /// </summary>
    /// <param name="sourceDocument">The source document to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceDocument"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Declarations are merged into <see cref="RootKoto"/>. Executable top-level local bindings,
    /// statements, expressions, and functions are placed in <see cref="GeneratedFunction"/>.
    /// </remarks>
    public void AddSource(SourceDocument sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        this.Add(sourceDocument);
    }

    /// <summary>
    /// Tokenizes and parses source documents into this source unit in logical declaration order (SPEC 6.2.1): sorted by logical name
    /// as UTF-8 byte sequences (a stable sort: equal names, which <see cref="CheckNames"/> reports, keep their given order), after every
    /// document added by an earlier call. A producer adds a module's ordinary sources in one call.
    /// </summary>
    /// <param name="documents">The source documents to add, in any order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="documents"/> or one of its documents is <see langword="null"/>.</exception>
    public void AddSources(IReadOnlyList<SourceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document, nameof(documents));
        }

        foreach (var document in documents.OrderBy(this.LogicalName, Helper.UnicodeNameHelper.ScalarOrder))
        {
            this.Add(document);
        }
    }

    /// <summary>Records a document parsed into the root for subsequent serialization.</summary>
    /// <param name="sourceDocument">The original source document.</param>
    /// <param name="modId">The generating Mod, or null.</param>
    /// <param name="additionOrder">The addition order within the Mod.</param>
    internal void RecordSource(SourceDocument sourceDocument, string? modId = null, int additionOrder = 0)
    {
        if (modId is not null)
        {
            (this.generatedSourceLocations ??= new()).Add(this.sourceDocuments.Count, (modId, additionOrder));
        }

        this.sourceDocuments.Add(sourceDocument);
        this.nameFailures = null;
    }

    /// <summary>Records that the parser skipped a declaration at a syntax Error, so the member set of this source unit is incomplete.</summary>
    /// <param name="cause">The key of the syntax Error.</param>
    internal void RecordOmission(DiagnosticKey cause)
        => (this.omittedDeclarations ??= []).Add(cause);

    /// <summary>Adds executable top-level syntax to the generated function.</summary>
    /// <param name="codeContext">The parsing context that produced the syntax.</param>
    /// <param name="item">The syntax node to add.</param>
    internal void AddGeneratedFunctionItem(CodeContext codeContext, Koto item)
    {
        var generatedFunction = this.GeneratedFunction;
        if (generatedFunction is null)
        {
            generatedFunction = new FunctionKoto(codeContext, Constants.GeneratedFunctionName) { Parent = this.RootKoto, };
            this.GeneratedFunction = generatedFunction;
        }

        generatedFunction.AddGeneratedItem(item);
    }

    /// <summary>Removes the generated function, if present.</summary>
    internal void ClearGeneratedFunction()
        => this.GeneratedFunction = default;

    // A fully qualified, NUL-free directory is normalized once; any other value is used as given, so no Path call throws (an
    // API value: Project.TryCreate never produces one), and such a base matches only paths with its exact text.
    private static string Normalize(string directory)
        => Path.IsPathFullyQualified(directory) && !directory.Contains('\0') ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) : directory;

    [TinyhandOnDeserializing]
    private void ResetSourceCompatibility()
    {
        // Missing fields must not inherit the destination instance's current version.
        this.sourceFormat = 0;
        this.sourceLanguageVersion = null;
        this.sourceCompilerVersion = null;
    }

    private void Add(SourceDocument sourceDocument)
    {
        this.RecordSource(sourceDocument);
        this.ParseSource(sourceDocument);
    }

    private void ParseSource(SourceDocument sourceDocument, string? modId = null, int additionOrder = 0)
    {
        if (!CompilerThread.IsCurrent)
        {
            CompilerThread.Run((this, sourceDocument, modId, additionOrder), static s =>
            {
                s.Item1.ParseSource(s.sourceDocument, s.modId, s.additionOrder);
                return default(ValueTuple);
            });
            return;
        }

        this.Compilation.BeginSourceParsing(this);

        // One target bound to the document serves the lexer and the parser, so parser recovery can rest on a lexical Error.
        var diagnosticCollection = this.Compilation.Diagnostics.GetOrAddCollection(this.LogicalName(sourceDocument), this).For(sourceDocument);
        var tokenizer = new Tokenizer(diagnosticCollection, sourceDocument) { CollectDocumentation = this.Compilation.CollectDocumentation, OptionalHoverOwner = this.Compilation.CollectHover ? this.Compilation : null };
        var codeContext = new CodeContext(this, diagnosticCollection, sourceDocument);

        // Tokenize and parse
        try
        {
            tokenizer.ReadAll();
            codeContext.Documentation = tokenizer.Documentation;
            var tokenReader = new TokenReader(codeContext, ref tokenizer);
            this.RootKoto.Parse(ref tokenReader);
            SyntaxNesting.Bound(codeContext);
            codeContext.Documentation?.SetLocation(this.LogicalName(sourceDocument), modId, additionOrder);
            codeContext.Documentation?.Finish(this.Compilation.Diagnostics.HasSyntaxErrors(sourceDocument));
            this.RecordDocumentation(codeContext.Documentation);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private void SetBase()
    {
        this.baseSource = this.Compilation.Project.Directory;
        this.baseDirectory = Normalize(this.BaseOrigin());
        this.names?.Clear();
        this.nameFailures = null;
    }

    // The directory the base comes from: for the root (no Url), its project's directory, else the current directory its compilation
    // captured; for another module, its own project's (its Url's); none for a Url that is not a full path.
    private string BaseOrigin()
    {
        if (this.Url.Length == 0)
        {
            return this.baseSource!.Length > 0 ? this.baseSource : this.Compilation.CurrentDirectory;
        }

        if (!Path.IsPathFullyQualified(this.Url))
        {
            return string.Empty;
        }

        return Path.GetDirectoryName(this.Url) ?? string.Empty;
    }
}
