// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;

namespace Kimi.Diagnostics;

/// <summary>
/// Owns the diagnostic facts of one check request and finalizes them into records (SPEC 23.3.6, docs/dev/DIAGNOSTICS.md §4).
/// Facts are recorded in partitions; recording an Error sets the error state before any suppression, and
/// finalization forms explanations only for published records. Analysis is single-threaded, so nothing is locked.
/// </summary>
public sealed class DiagnosticOwner
{
    private const int MaxExcerptLines = 4;
    private const int MaxExcerptWidth = 160;
    private const int ExcerptLead = 40;

    private readonly List<SourceEntry> sources = [];
    private readonly Dictionary<SourceDocument, int> documentSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> pathSources = new(StringComparer.Ordinal);
    private readonly Dictionary<Kotonoha, int> modules = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> units = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, int Module), DiagnosticCollection> targets = [];
    private readonly List<DiagnosticFact>[] partitions = new List<DiagnosticFact>[(int)DiagnosticPartition.Emission + 1];
    private readonly int[] partitionErrors = new int[(int)DiagnosticPartition.Emission + 1];
    private readonly List<List<DiagnosticFact>> syntax = [];
    private readonly List<int> syntaxErrors = [];
    private int unattributed = -1;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticOwner"/> class.</summary>
    public DiagnosticOwner()
    {
        for (var i = 0; i < this.partitions.Length; i++)
        {
            this.partitions[i] = [];
        }
    }

    /// <summary>Gets a value indicating whether any valid partition holds an Error, including facts that finalization suppresses.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var count in this.partitionErrors)
            {
                if (count != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the recording target of a module, named like the former diagnostic collection for the transitional start-offset filter.</summary>
    /// <param name="name">The unit name.</param>
    /// <param name="module">The module whose syntax the target records, or <see langword="null"/> for inputs.</param>
    /// <returns>The recording target.</returns>
    public DiagnosticCollection GetOrAddCollection(string name, Kotonoha? module = null)
    {
        var index = module is null ? -1 : this.ModuleIndex(module);
        if (!this.targets.TryGetValue((name, index), out var collection))
        {
            collection = new(this, name, this.UnitOf(name), index);
            this.targets.Add((name, index), collection);
        }

        return collection;
    }

    /// <summary>Gets a value indicating whether a partition holds an Error.</summary>
    /// <param name="partition">The partition; for syntax, all modules.</param>
    /// <returns><see langword="true"/> when the partition holds an Error.</returns>
    public bool HasErrorsIn(DiagnosticPartition partition)
        => this.partitionErrors[(int)partition] != 0;

    /// <summary>Gets a value indicating whether a partition up to and including the last one holds an Error.</summary>
    /// <param name="last">The last partition in phase order.</param>
    /// <returns><see langword="true"/> when an Error precedes or belongs to <paramref name="last"/>.</returns>
    public bool HasErrorsThrough(DiagnosticPartition last)
    {
        for (var i = 0; i <= (int)last; i++)
        {
            if (this.partitionErrors[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets a value indicating whether a module's syntax holds an Error.</summary>
    /// <param name="module">The module.</param>
    /// <returns><see langword="true"/> when the module has a lexical or syntax Error.</returns>
    public bool HasSyntaxErrors(Kotonoha module)
        => this.modules.TryGetValue(module, out var index) && index < this.syntaxErrors.Count && this.syntaxErrors[index] != 0;

    /// <summary>Records a problem that concerns a whole input, or no source, such as a configuration or generation failure.</summary>
    /// <param name="partition">The partition of the phase that reports it.</param>
    /// <param name="code">The code.</param>
    /// <param name="path">The input it concerns, or <see langword="null"/> for none.</param>
    /// <param name="first">The first message argument.</param>
    /// <param name="second">The second message argument.</param>
    public void Report(DiagnosticPartition partition, DiagnosticCode code, string? path, object? first = null, object? second = null)
    {
        var entry = DiagnosticCollection.Validate(default, code, first, second, null);
        var source = path is null ? -1 : this.PathSource(path);
        var unit = this.UnitOf(path ?? string.Empty);
        var module = partition == DiagnosticPartition.Syntax ? this.UnattributedModule() : -1;
        this.Record(partition, module, new(code, source, 0, -1, Capture(first), Capture(second), null, unit), entry.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Discards a partition's facts and error state; syntax is discarded for every module.</summary>
    /// <param name="partition">The partition.</param>
    public void Invalidate(DiagnosticPartition partition)
    {
        if (partition == DiagnosticPartition.Syntax)
        {
            for (var i = 0; i < this.syntax.Count; i++)
            {
                this.InvalidateSyntax(i);
            }

            return;
        }

        this.partitions[(int)partition].Clear();
        this.partitionErrors[(int)partition] = 0;
    }

    /// <summary>Discards the facts of every semantic analysis: Binding, startup, control flow and ownership.</summary>
    public void InvalidateSemantics()
    {
        for (var i = DiagnosticPartition.Binding; i <= DiagnosticPartition.Ownership; i++)
        {
            this.Invalidate(i);
        }
    }

    /// <summary>Discards a module's syntax facts when its syntax tree is rebuilt.</summary>
    /// <param name="module">The module.</param>
    public void InvalidateSyntax(Kotonoha module)
    {
        if (this.modules.TryGetValue(module, out var index))
        {
            this.InvalidateSyntax(index);
        }
    }

    /// <summary>Finalizes the valid facts of a range of partitions into records (SPEC 23.3.6.5, 23.3.6.6).</summary>
    /// <param name="first">The first partition.</param>
    /// <param name="last">The last partition.</param>
    /// <returns>The records in result order and their source table.</returns>
    public DiagnosticResult Finalize(DiagnosticPartition first = DiagnosticPartition.Input, DiagnosticPartition last = DiagnosticPartition.Ownership)
    {
        // D2a: the former start-offset suppression per collection, keeping the first fact in recording order
        // (partitions in phase order, then arrival). It is replaced by prerequisites in D2b.
        var published = new List<DiagnosticFact>();
        var seen = new HashSet<(int Unit, int Start)>();
        for (var partition = first; partition <= last; partition++)
        {
            if (partition == DiagnosticPartition.Syntax)
            {
                foreach (var list in this.syntax)
                {
                    Publish(list, published, seen);
                }
            }
            else
            {
                Publish(this.partitions[(int)partition], published, seen);
            }
        }

        if (published.Count == 0)
        {
            return DiagnosticResult.Empty;
        }

        // Stable: equal keys keep recording order until problem identities order them (D2b).
        var order = new int[published.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) =>
        {
            var comparison = Compare(published[x], published[y]);
            return comparison != 0 ? comparison : x.CompareTo(y);
        });

        var table = new List<DiagnosticSource>();
        var remap = new Dictionary<int, int>();
        var records = new CheckDiagnostic[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            var fact = published[order[i]];
            var source = -1;
            if (fact.Source >= 0 && !remap.TryGetValue(fact.Source, out source))
            {
                source = table.Count;
                remap.Add(fact.Source, source);
                var entry = this.sources[fact.Source];
                table.Add(new(entry.Path, entry.IsInput));
            }

            records[i] = this.CreateRecord(fact, source);
        }

        return new(records, table.ToArray());
    }

    internal static object? Capture(object? value)
        => value is null or string or int or long or bool or Enum ? value : value.ToString();

    internal void Record(DiagnosticPartition partition, int module, in DiagnosticFact fact, bool isError)
    {
        if (partition == DiagnosticPartition.Syntax)
        {
            this.syntax[module].Add(fact);
            if (isError)
            {
                this.syntaxErrors[module]++;
                if (fact.Source >= 0)
                {
                    this.sources[fact.Source].SyntaxErrors++;
                }
            }
        }
        else
        {
            this.partitions[(int)partition].Add(fact);
        }

        if (isError)
        {
            this.partitionErrors[(int)partition]++;
        }
    }

    /// <summary>Gets the source table index of a document, registering it in consumption order.</summary>
    /// <param name="document">The document.</param>
    /// <param name="module">The module that consumes it.</param>
    /// <param name="isInput">Whether the document is an input the check read.</param>
    /// <returns>The index.</returns>
    internal int DocumentSource(SourceDocument document, int module, bool isInput = false)
    {
        if (!this.documentSources.TryGetValue(document, out var index))
        {
            index = this.sources.Count;
            this.sources.Add(new(document.Path, isInput, document, module));
            this.documentSources.Add(document, index);
        }
        else if (isInput)
        {
            this.sources[index].IsInput = true;
        }

        return index;
    }

    /// <summary>Registers a document the check read as an input, before it is parsed.</summary>
    /// <param name="document">The document.</param>
    /// <param name="module">The module that consumes it.</param>
    internal void AddInput(SourceDocument document, Kotonoha module)
        => this.DocumentSource(document, this.ModuleIndex(module), true);

    /// <summary>Gets a value indicating whether the syntax of one document holds an Error; a decision inside one parse uses it.</summary>
    /// <param name="document">The document.</param>
    /// <returns><see langword="true"/> when lexing or parsing the document recorded an Error.</returns>
    internal bool HasSyntaxErrors(SourceDocument document)
        => this.documentSources.TryGetValue(document, out var index) && this.sources[index].SyntaxErrors != 0;

    /// <summary>Finds a registered document by path; tests read the text under a record's span through it.</summary>
    /// <param name="path">The display path.</param>
    /// <returns>The first document with that path, or <see langword="null"/>.</returns>
    internal SourceDocument? FindDocument(string path)
        => this.sources.Find(x => x.Document is not null && x.Path == path)?.Document;

    internal int ModuleIndex(Kotonoha module)
    {
        if (!this.modules.TryGetValue(module, out var index))
        {
            index = this.AddModule();
            this.modules.Add(module, index);
        }

        return index;
    }

    /// <summary>Gets the syntax list of facts reported through a target without a module, such as a test's own target.</summary>
    /// <returns>The module index.</returns>
    internal int UnattributedModule()
        => this.unattributed >= 0 ? this.unattributed : this.unattributed = this.AddModule();

    private static void Publish(List<DiagnosticFact> facts, List<DiagnosticFact> published, HashSet<(int Unit, int Start)> seen)
    {
        foreach (var fact in facts)
        {
            if (seen.Add((fact.Unit, fact.Length < 0 ? 0 : fact.Start)))
            {
                published.Add(fact);
            }
        }
    }

    // SPEC 23.3.6.6: source table order, then span (none first), then an order that never uses text or arrival.
    private static int Compare(in DiagnosticFact left, in DiagnosticFact right)
    {
        var order = (left.Source < 0 ? int.MaxValue : left.Source).CompareTo(right.Source < 0 ? int.MaxValue : right.Source);
        if (order == 0)
        {
            order = (left.Length < 0 ? -1 : left.Start).CompareTo(right.Length < 0 ? -1 : right.Start);
        }

        if (order == 0)
        {
            order = left.Length.CompareTo(right.Length);
        }

        return order == 0 ? string.CompareOrdinal(left.Code.ToString(), right.Code.ToString()) : order;
    }

    private static int DisplayWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var c in text)
        {
            width += c == '\t' ? Constants.IndentationSpaces - (width % Constants.IndentationSpaces) : 1;
        }

        return width;
    }

    private static string ExpandTabs(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('\t') < 0)
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c == '\t')
            {
                builder.Append(' ', Constants.IndentationSpaces - (builder.Length % Constants.IndentationSpaces));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static DiagnosticExcerptLine ExcerptLine(SourceDocument document, int line, int startCharacter, int endCharacter)
    {
        var text = document.GetLineSpan(line);
        startCharacter = Math.Clamp(startCharacter, 0, text.Length);
        endCharacter = Math.Clamp(endCharacter, startCharacter, text.Length);
        var expanded = ExpandTabs(text);
        var start = DisplayWidth(text[..startCharacter]);
        var length = Math.Max(1, DisplayWidth(text[..endCharacter]) - start);
        if (expanded.Length <= MaxExcerptWidth)
        {
            return new(line + 1, expanded, start, length);
        }

        // Clip a long line to a window around the underline, marking each cut with an ellipsis.
        var from = Math.Clamp(start - ExcerptLead, 0, expanded.Length);
        var to = Math.Min(expanded.Length, from + MaxExcerptWidth);
        var clipped = string.Concat(from > 0 ? "…" : string.Empty, expanded.AsSpan(from, to - from), to < expanded.Length ? "…" : string.Empty);
        var clippedStart = start - from + (from > 0 ? 1 : 0);
        return new(line + 1, clipped, clippedStart, Math.Max(1, Math.Min(length, clipped.Length - clippedStart)));
    }

    private int UnitOf(string name)
    {
        if (!this.units.TryGetValue(name, out var unit))
        {
            unit = this.units.Count;
            this.units.Add(name, unit);
        }

        return unit;
    }

    private int AddModule()
    {
        this.syntax.Add([]);
        this.syntaxErrors.Add(0);
        return this.syntax.Count - 1;
    }

    private void InvalidateSyntax(int module)
    {
        this.partitionErrors[(int)DiagnosticPartition.Syntax] -= this.syntaxErrors[module];
        this.syntaxErrors[module] = 0;
        this.syntax[module].Clear();
        foreach (var entry in this.sources)
        {
            if (entry.Module == module)
            {
                entry.SyntaxErrors = 0;
            }
        }
    }

    private int PathSource(string path)
    {
        if (!this.pathSources.TryGetValue(path, out var index))
        {
            index = this.sources.Count;
            this.sources.Add(new(path, !path.StartsWith(Checking.SourceIdentity.BuiltInPrefix, StringComparison.Ordinal), null, -1));
            this.pathSources.Add(path, index);
        }

        return index;
    }

    private CheckDiagnostic CreateRecord(in DiagnosticFact fact, int source)
    {
        DiagnosticEntries.TryGet(fact.Code, out var entry);
        var message = entry!.FormatMessage(fact.First, fact.Second);
        if (fact.Hint is not null)
        {
            message = string.Concat(message, " ", fact.Hint);
        }

        SourceSpan? span = fact.Length < 0 ? null : new SourceSpan(fact.Start, fact.Length);
        DiagnosticDisplay? display = null;
        if (span is { } primary && fact.Source >= 0 && this.sources[fact.Source].Document is { } document)
        {
            var range = document.GetSourceRange(primary);
            var lastLine = range.End.Line;
            if (lastLine > range.Start.Line && range.End.Character == 0)
            {
                lastLine--;
            }

            var lines = new List<DiagnosticExcerptLine>();
            for (var line = range.Start.Line; line <= lastLine && line < document.LineCount; line++)
            {
                if (lines.Count == MaxExcerptLines - 1 && line < lastLine)
                {
                    line = lastLine; // Keep the first lines and the last one.
                }

                var start = line == range.Start.Line ? range.Start.Character : 0;
                var end = line == range.End.Line ? range.End.Character : int.MaxValue;
                lines.Add(ExcerptLine(document, line, start, end));
            }

            display = new(range, lines.ToArray());
        }

        return new(entry.Name, entry.Severity, entry.Category, message, source, span)
        {
            Label = entry.Label,
            Note = entry.Note,
            Advice = entry.Advice,
            Display = display,
        };
    }

    private sealed class SourceEntry(string path, bool isInput, SourceDocument? document, int module)
    {
        public string Path { get; } = path;

        public bool IsInput { get; set; } = isInput;

        public SourceDocument? Document { get; } = document;

        public int Module { get; } = module;

        public int SyntaxErrors { get; set; }
    }
}

/// <summary>One recorded fact (docs/dev/DIAGNOSTICS.md §4.1). Problem identities and prerequisites arrive in D2b.</summary>
/// <param name="Code">The code.</param>
/// <param name="Source">The source table index, or -1.</param>
/// <param name="Start">The span start.</param>
/// <param name="Length">The span length, or -1 without a span.</param>
/// <param name="First">The first message argument, captured as a value.</param>
/// <param name="Second">The second message argument, captured as a value.</param>
/// <param name="Hint">Text appended to the message until D2b moves it into Notes and Advice.</param>
/// <param name="Unit">The former collection, for the transitional start-offset filter.</param>
internal readonly record struct DiagnosticFact(DiagnosticCode Code, int Source, int Start, int Length, object? First, object? Second, string? Hint, int Unit);
