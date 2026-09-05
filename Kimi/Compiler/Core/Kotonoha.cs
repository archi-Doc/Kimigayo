// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text;
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
    /// <summary>The initial depth reserved by the Koto index walk.</summary>
    private const int DefaultKotoIndexCapacity = 64;

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

    /// <summary>
    /// Gets the generated function that owns executable top-level syntax.
    /// </summary>
    [IgnoreMember]
    public FunctionKoto? GeneratedFunction { get; private set; }

    /// <summary>Gets the original source documents in parsing order.</summary>
    [IgnoreMember]
    public IReadOnlyList<SourceDocument> SourceDocuments => this.sourceDocuments;

    [IgnoreMember]
    private readonly UInt64Hashtable<Koto> kotoIdToKoto = new();

    [IgnoreMember]
    private readonly object kotoIndexLock = new();

    [Key(3)]
    private List<SourceDocument> sourceDocuments = new();

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

        this.DiagnosticCollection = compilation.Kimigayo.GetOrAddDiagnosticCollection(name);
        this.Compilation = compilation;
        this.Name = name;
        this.Id = (uint)XxHash3Slim.Hash64(name);
        this.Url = url;

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

        this.DiagnosticCollection = compilation.Kimigayo.GetOrAddDiagnosticCollection(this.Name);
        this.Compilation = compilation;
        this.RootKoto = new(new CodeContext(this), default, default);
        this.GeneratedFunction = null;
        lock (this.kotoIndexLock)
        {
            this.kotoIdToKoto.Clear();
        }

        foreach (var sourceDocument in this.sourceDocuments)
        {
            this.ParseSource(sourceDocument);
        }
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
        => new(this, diagnosticCollection);

    /// <summary>
    /// Attempts to find a Koto node by its identifier.
    /// </summary>
    /// <param name="kotoId">The Koto identifier.</param>
    /// <param name="koto">The matching node, if found.</param>
    /// <returns><see langword="true"/> when a matching node is found.</returns>
    public bool TryGetKoto(ulong kotoId, [MaybeNullWhen(false)] out Koto koto)
    {
        lock (this.kotoIndexLock)
        {
            if (this.kotoIdToKoto.TryGetValue(kotoId, out koto) && this.IsAttachedToRoot(koto))
            {
                return true;
            }

            this.RebuildKotoIndex();
            return this.kotoIdToKoto.TryGetValue(kotoId, out koto);
        }
    }

    /// <summary>
    /// Tokenizes and parses a source document into this source unit.
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

        this.RecordSource(sourceDocument);
        this.ParseSource(sourceDocument);
    }

    /// <summary>Records a document parsed into the root for subsequent serialization.</summary>
    /// <param name="sourceDocument">The original source document.</param>
    internal void RecordSource(SourceDocument sourceDocument)
        => this.sourceDocuments.Add(sourceDocument);

    /// <summary>Adds executable top-level syntax to the generated function.</summary>
    /// <param name="codeContext">The parsing context that produced the syntax.</param>
    /// <param name="item">The syntax node to add.</param>
    /// <param name="hasTrailingExpression">Whether this item is an expression without a semicolon.</param>
    internal void AddGeneratedFunctionItem(CodeContext codeContext, Koto item, bool hasTrailingExpression)
    {
        var generatedFunction = this.GeneratedFunction;
        if (generatedFunction is null)
        {
            generatedFunction = new FunctionKoto(codeContext, Constants.GeneratedFunctionName) { Parent = this.RootKoto, };
            this.GeneratedFunction = generatedFunction;
        }

        generatedFunction.AddGeneratedItem(item, hasTrailingExpression);
    }

    /// <summary>Removes the generated function, if present.</summary>
    internal void ClearGeneratedFunction()
        => this.GeneratedFunction = default;

    private static void DumpToken(string path, ReadOnlySpan<Token> tokens)
    {
        // Enum.ToString() returns the cached member name, so only the builder grows here.
        var sb = new StringBuilder(Math.Min(tokens.Length * 12, 1 << 16));
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Separator)
            {
                sb.AppendLf();
            }
            else
            {
                sb.Append('(').Append(token.Kind.ToString()).Append(')');
            }
        }

        try
        {
            File.WriteAllText(Path.ChangeExtension(path, Constants.TokenExtension), sb.ToString());
        }
        catch
        {
            // Token dumps are diagnostic aids and must not stop compilation.
        }
    }

    private void ParseSource(SourceDocument sourceDocument)
    {
        var path = sourceDocument.Path;
        var directory = this.Compilation.Project.Directory;
        if (path.Length > 0 && directory.Length > 0)
        {// Path.GetRelativePath rejects an empty path.
            path = Path.GetRelativePath(directory, path);
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
                DumpToken(sourceDocument.Path, tokenizer.Tokens);
            }

            // Token to Koto
            var tokenReader = new TokenReader(codeContext, ref tokenizer);
            this.RootKoto.Parse(ref tokenReader);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private bool IsAttachedToRoot(Koto koto)
    {
        while (koto.Parent is { } parent)
        {
            koto = parent;
        }

        return ReferenceEquals(koto, this.RootKoto);
    }

    private void RebuildKotoIndex()
    {
        // Koto IDs depend on the completed parent chain. Build the index lazily after parsing
        // and rebuild on a miss so generated or edited declarations become discoverable.
        var table = this.kotoIdToKoto;
        table.Clear();
        var stack = new Stack<Koto>(DefaultKotoIndexCapacity);
        stack.Push(this.RootKoto);
        while (stack.TryPop(out var koto))
        {
            if (koto is IdentifiableKoto identifiable)
            {
                table.TryAdd(identifiable.KotoId, identifiable);
            }

            foreach (var child in koto.ChildNodes)
            {
                stack.Push(child);
            }
        }
    }
}
