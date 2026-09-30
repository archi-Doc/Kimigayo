// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;

namespace Kimi.Diagnostics;

/// <summary>
/// Owns the diagnostic facts of one check request and finalizes them into records (SPEC 23.3.6, docs/dev/DIAGNOSTICS.md §4).
/// Facts are recorded in partitions and aggregated by problem; recording an Error sets the error state before any
/// suppression, and finalization forms explanations only for published records. Analysis is single-threaded, so nothing is locked.
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
    private readonly FactList[] partitions = new FactList[(int)DiagnosticPartition.Emission + 1];
    private readonly int[] partitionErrors = new int[(int)DiagnosticPartition.Emission + 1];
    private readonly List<FactList> syntax = [];
    private int unattributed = -1;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticOwner"/> class.</summary>
    public DiagnosticOwner()
    {
        for (var i = 0; i < this.partitions.Length; i++)
        {
            this.partitions[i] = new();
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
        => this.modules.TryGetValue(module, out var index) && index < this.syntax.Count && this.syntax[index].Errors != 0;

    /// <summary>Records a problem that concerns a whole input, or no source, such as a configuration or generation failure.</summary>
    /// <param name="partition">The partition of the phase that reports it.</param>
    /// <param name="code">The code.</param>
    /// <param name="path">The input it concerns, or <see langword="null"/> for none.</param>
    /// <param name="first">The first message argument.</param>
    /// <param name="second">The second message argument.</param>
    /// <remarks>The observed failure, its arguments, is the context of the problem (SPEC 23.3.4): two failures of one input are two problems.</remarks>
    public void Report(DiagnosticPartition partition, DiagnosticCode code, string? path, object? first = null, object? second = null)
    {
        var entry = DiagnosticCollection.Validate(default, code, first, second, null, null);
        var source = path is null ? -1 : this.PathSource(path);
        var module = partition == DiagnosticPartition.Syntax ? this.UnattributedModule() : -1;
        var context = first is null ? null : second is null ? first.ToString() : string.Concat(first.ToString(), "\u001f", second.ToString());
        var key = new DiagnosticKey(null, source, 0, -1, new(partition, 0), 0, context);
        this.Record(partition, module, new(code, key, source, 0, -1, Capture(first), Capture(second), null, null, null, this.UnitOf(path ?? string.Empty), false), entry.Severity == DiagnosticSeverity.Error);
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

    /// <summary>
    /// Finalizes the valid facts of a range of partitions into records (SPEC 23.3.6.4–23.3.6.6): derived problems whose
    /// prerequisites all lead to published direct Errors are suppressed, and the rest are ordered by source table, span and identity.
    /// </summary>
    /// <param name="first">The first partition.</param>
    /// <param name="last">The last partition.</param>
    /// <returns>The records in result order and their source table.</returns>
    /// <exception cref="DiagnosticContractException">Two distinct problems have no defined order.</exception>
    public DiagnosticResult Finalize(DiagnosticPartition first = DiagnosticPartition.Input, DiagnosticPartition last = DiagnosticPartition.Ownership)
    {
        var facts = this.Candidates(first, last);
        if (facts.Count == 0)
        {
            return DiagnosticResult.Empty;
        }

        var explained = Explain(facts);
        var order = new List<int>(facts.Count);
        for (var i = 0; i < facts.Count; i++)
        {
            if (!explained[i])
            {
                order.Add(i);
            }
        }

        order.Sort((x, y) =>
        {
            var comparison = Compare(facts[x], facts[y]);
            return comparison != 0 ? comparison : x.CompareTo(y);
        });

        for (var i = 1; i < order.Count; i++)
        {
            var previous = facts[order[i - 1]];
            var current = facts[order[i]];
            if (!previous.Legacy && !current.Legacy && Compare(previous, current) == 0)
            {
                throw new DiagnosticContractException(DiagnosticFault.UndefinedOrder, $"{previous.Code} and {current.Code} at {current.Start} have no defined order.");
            }
        }

        var table = new List<DiagnosticSource>();
        var remap = new Dictionary<int, int>();
        var records = new CheckDiagnostic[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            records[i] = this.CreateRecord(facts[order[i]], table, remap);
        }

        return new(records, table.ToArray());
    }

    internal static object? Capture(object? value)
        => value is null or string or int or long or bool or Enum ? value : value.ToString();

    internal void Record(DiagnosticPartition partition, int module, in DiagnosticFact fact, bool isError)
    {
        var list = partition == DiagnosticPartition.Syntax ? this.syntax[module] : this.partitions[(int)partition];
        if (!fact.Legacy)
        {
            // One problem, one fact: a repeated report merges its prerequisites and must agree on location and facts.
            list.Problems ??= [];
            if (list.Problems.TryGetValue((fact.Key, fact.Code), out var index))
            {
                var existing = list.Facts[index];
                // A Note or Advice that one report supplies merges; two different ones conflict like different facts.
                if (existing.Source != fact.Source || existing.Start != fact.Start || existing.Length != fact.Length ||
                    !Equals(existing.First, fact.First) || !Equals(existing.Second, fact.Second) || Conflicts(existing.Note, fact.Note) || Conflicts(existing.Advice, fact.Advice))
                {
                    throw new DiagnosticContractException(DiagnosticFault.ConflictingProblem, $"{fact.Code} was reported twice with different locations or facts: [{existing.Start}+{existing.Length}] {existing.First} {existing.Second} {existing.Note} and [{fact.Start}+{fact.Length}] {fact.First} {fact.Second} {fact.Note}.");
                }

                list.Facts[index] = existing with
                {
                    Note = existing.Note ?? fact.Note,
                    Advice = existing.Advice ?? fact.Advice,
                    DerivedFrom = fact.DerivedFrom is { } derived ? Union(existing.DerivedFrom, derived) : existing.DerivedFrom,
                };

                return;
            }

            list.Problems.Add((fact.Key, fact.Code), list.Facts.Count);
        }

        list.Facts.Add(fact);
        if (isError)
        {
            list.Errors++;
            this.partitionErrors[(int)partition]++;
            if (partition == DiagnosticPartition.Syntax && fact.Source >= 0)
            {
                this.sources[fact.Source].SyntaxErrors++;
            }
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

    private static bool Conflicts(string? left, string? right)
        => left is not null && right is not null && left != right;

    private static DiagnosticKey[] Union(DiagnosticKey[]? left, DiagnosticKey[] right)
    {
        if (left is null)
        {
            return right;
        }

        var union = new List<DiagnosticKey>(left);
        foreach (var key in right)
        {
            if (!union.Contains(key))
            {
                union.Add(key);
            }
        }

        return union.Count == left.Length ? left : union.ToArray();
    }

    // SPEC 23.3.6.4: a derived fact is explained when every prerequisite leads, without an unresolved link or a cycle, to a
    // direct Error. Satisfied keys propagate through a queue: each derived fact counts its unsatisfied prerequisites, and one
    // that reaches zero satisfies its own key. This is linear in facts and prerequisites; a cycle never becomes explained.
    private static bool[] Explain(List<DiagnosticFact> facts)
    {
        var explained = new bool[facts.Count];
        Dictionary<DiagnosticKey, List<int>>? watchers = null;
        int[]? pending = null;
        for (var i = 0; i < facts.Count; i++)
        {
            if (facts[i] is { Legacy: false, DerivedFrom: { } prerequisites })
            {
                watchers ??= [];
                pending ??= new int[facts.Count];
                foreach (var key in prerequisites)
                {
                    // An unresolved mark is never satisfied, so the fact stays pending.
                    pending[i]++;
                    if (!key.IsUnresolved)
                    {
                        if (!watchers.TryGetValue(key, out var list))
                        {
                            watchers.Add(key, list = []);
                        }

                        list.Add(i);
                    }
                }
            }
        }

        if (watchers is null)
        {
            return explained;
        }

        var satisfied = new HashSet<DiagnosticKey>();
        var queue = new Queue<DiagnosticKey>();
        foreach (var fact in facts)
        {
            if (!fact.Legacy && fact.DerivedFrom is null && DiagnosticEntries.TryGet(fact.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error && satisfied.Add(fact.Key))
            {
                queue.Enqueue(fact.Key);
            }
        }

        while (queue.TryDequeue(out var key))
        {
            if (!watchers.TryGetValue(key, out var list))
            {
                continue;
            }

            foreach (var index in list)
            {
                if (--pending![index] == 0)
                {
                    explained[index] = true;
                    if (satisfied.Add(facts[index].Key))
                    {
                        queue.Enqueue(facts[index].Key);
                    }
                }
            }
        }

        return explained;
    }

    // SPEC 23.3.6.6: source table order, then span (none first), then the problem's identity: subject, code, requirement,
    // condition and context. Facts of recorders not yet migrated (D2b) compare by code and then keep recording order.
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

        if (order == 0 && !left.Legacy && !right.Legacy)
        {
            order = left.Key.CompareSubject(right.Key);
        }

        if (order == 0)
        {
            order = string.CompareOrdinal(left.Code.ToString(), right.Code.ToString());
        }

        return order == 0 && !left.Legacy && !right.Legacy ? left.Key.CompareCheck(right.Key) : order;
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

    // D2a's start-offset suppression per former collection, kept for recorders not yet migrated (docs/dev/DIAGNOSTICS.md §8):
    // in recording order, a legacy fact is dropped when an earlier fact occupies its offset; migrated facts are never dropped.
    private List<DiagnosticFact> Candidates(DiagnosticPartition first, DiagnosticPartition last)
    {
        var candidates = new List<DiagnosticFact>();
        HashSet<(int Unit, int Start)>? seen = null;
        for (var partition = first; partition <= last; partition++)
        {
            if (partition == DiagnosticPartition.Syntax)
            {
                foreach (var list in this.syntax)
                {
                    Add(list.Facts, candidates, ref seen);
                }
            }
            else
            {
                Add(this.partitions[(int)partition].Facts, candidates, ref seen);
            }
        }

        return candidates;

        static void Add(List<DiagnosticFact> facts, List<DiagnosticFact> candidates, ref HashSet<(int Unit, int Start)>? seen)
        {
            foreach (var fact in facts)
            {
                if ((seen ??= []).Add((fact.Unit, fact.Length < 0 ? 0 : fact.Start)) || !fact.Legacy)
                {
                    candidates.Add(fact);
                }
            }
        }
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
        this.syntax.Add(new());
        return this.syntax.Count - 1;
    }

    private void InvalidateSyntax(int module)
    {
        this.partitionErrors[(int)DiagnosticPartition.Syntax] -= this.syntax[module].Errors;
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

    private int TableIndex(int source, List<DiagnosticSource> table, Dictionary<int, int> remap)
    {
        if (source < 0)
        {
            return -1;
        }

        if (!remap.TryGetValue(source, out var index))
        {
            index = table.Count;
            remap.Add(source, index);
            var entry = this.sources[source];
            table.Add(new(entry.Path, entry.IsInput));
        }

        return index;
    }

    private (SourceSpan? Span, SourceRange? Range) Locate(int source, int start, int length)
    {
        if (length < 0)
        {
            return (null, null);
        }

        var span = new SourceSpan(start, length);
        return (span, source >= 0 && this.sources[source].Document is { } document ? document.GetSourceRange(span) : null);
    }

    private CheckDiagnostic CreateRecord(in DiagnosticFact fact, List<DiagnosticSource> table, Dictionary<int, int> remap)
    {
        DiagnosticEntries.TryGet(fact.Code, out var entry);
        var (span, range) = this.Locate(fact.Source, fact.Start, fact.Length);
        DiagnosticDisplay? display = null;
        if (range is { } primary && this.sources[fact.Source].Document is { } document)
        {
            var lastLine = primary.End.Line;
            if (lastLine > primary.Start.Line && primary.End.Character == 0)
            {
                lastLine--;
            }

            var lines = new List<DiagnosticExcerptLine>();
            for (var line = primary.Start.Line; line <= lastLine && line < document.LineCount; line++)
            {
                if (lines.Count == MaxExcerptLines - 1 && line < lastLine)
                {
                    line = lastLine; // Keep the first lines and the last one.
                }

                var start = line == primary.Start.Line ? primary.Start.Character : 0;
                var end = line == primary.End.Line ? primary.End.Character : int.MaxValue;
                lines.Add(ExcerptLine(document, line, start, end));
            }

            display = new(range, lines.ToArray());
        }

        DiagnosticValue[]? reason = null;
        DiagnosticRelated[]? related = null;
        if (fact.DerivedFrom is { } prerequisites)
        {
            // SPEC 23.3.6.4: the requirement left undecided, what it needs, and where each prerequisite is.
            var requirement = fact.Key.Requirement;
            reason =
            [
                new("requirement", DiagnosticValueKind.Enumeration, requirement.Name),
                new("condition", DiagnosticValueKind.Text, DiagnosticRequirements.TryGetDescription(requirement, out var description) ? description : requirement.Name),
            ];
            var locations = new List<DiagnosticRelated>();
            foreach (var key in prerequisites)
            {
                if (!key.IsUnresolved && key.Source >= 0)
                {
                    var (relatedSpan, relatedRange) = this.Locate(key.Source, key.Start, key.Length);
                    var label = DiagnosticRequirements.TryGetDescription(key.Requirement, out var prerequisiteDescription) ? prerequisiteDescription : key.Requirement.Name;
                    locations.Add(new("prerequisite", this.TableIndex(key.Source, table, remap), relatedSpan, relatedRange, label));
                }
            }

            related = locations.Count == 0 ? null : locations.ToArray();
        }

        return new(entry!.Name, entry.Severity, entry.Category, entry.FormatMessage(fact.First, fact.Second), this.TableIndex(fact.Source, table, remap), span)
        {
            Label = entry.Label,
            Reason = reason,
            Related = related,
            Note = fact.Note ?? entry.Note,
            Advice = fact.Advice ?? entry.Advice,
            Display = display,
        };
    }

    private sealed class FactList
    {
        public List<DiagnosticFact> Facts { get; } = [];

        public Dictionary<(DiagnosticKey Key, DiagnosticCode Code), int>? Problems { get; set; }

        public int Errors { get; set; }

        public void Clear()
        {
            this.Facts.Clear();
            this.Problems?.Clear();
            this.Errors = 0;
        }
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
