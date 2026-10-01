// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
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
    private readonly List<SourceEntry> sources = [];
    private readonly Dictionary<SourceDocument, int> documentSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> pathSources = new(StringComparer.Ordinal);
    private readonly Dictionary<Kotonoha, int> modules = new(ReferenceEqualityComparer.Instance);
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

    /// <summary>Gets the recording target of a module; its name also names sources parsed from text through it.</summary>
    /// <param name="name">The unit name.</param>
    /// <param name="module">The module whose syntax the target records, or <see langword="null"/> for inputs.</param>
    /// <returns>The recording target.</returns>
    public DiagnosticCollection GetOrAddCollection(string name, Kotonoha? module = null)
    {
        var index = module is null ? -1 : this.ModuleIndex(module);
        if (!this.targets.TryGetValue((name, index), out var collection))
        {
            collection = new(this, name, index);
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
    /// <param name="note">The environment-dependent text of the failure, such as an exception message; it is published as a bounded Note.</param>
    /// <remarks>The observed failure, its arguments or else its Note, is the context of the problem (SPEC 23.3.4): two failures of one input are two problems.</remarks>
    public void Report(DiagnosticPartition partition, DiagnosticCode code, string? path, object? first = null, object? second = null, string? note = null)
    {
        var entry = DiagnosticCollection.Validate(default, code, first, second, null, null);
        var source = path is null ? -1 : this.PathSource(path);
        var module = partition == DiagnosticPartition.Syntax ? this.UnattributedModule() : -1;
        var context = first is null ? note : second is null ? first.ToString() : string.Concat(first.ToString(), "\u001f", second.ToString());
        var key = new DiagnosticKey(null, source, 0, -1, new(partition, 0), 0, context);
        this.Record(partition, module, new(code, key, source, 0, -1, Capture(first), Capture(second), note, null, null), entry.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Registers an input that a check consumes before its sources, such as its project file, in the source table
    /// (SPEC 23.3.6.3); a later record that names it keeps that consumption order. An entry no record names is not published.</summary>
    /// <param name="path">The input path.</param>
    public void RegisterPath(string path)
        => this.PathSource(path);

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
    /// <param name="rejected">Whether the result these partitions produce is rejected; a rejection must publish an Error that explains it.</param>
    /// <returns>The records in result order and their source table.</returns>
    /// <exception cref="DiagnosticContractException">Two distinct problems have no defined order, or a rejection is unexplained.</exception>
    public DiagnosticResult Finalize(DiagnosticPartition first = DiagnosticPartition.Input, DiagnosticPartition last = DiagnosticPartition.Ownership, bool rejected = false)
    {
        var facts = this.Candidates(first, last);
        if (facts.Count == 0)
        {
            return rejected ? throw Unexplained() : DiagnosticResult.Empty;
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
            if (Compare(previous, current) == 0)
            {
                throw new DiagnosticContractException(DiagnosticFault.UndefinedOrder, $"{previous.Code} and {current.Code} at {current.Start} have no defined order.");
            }
        }

        // Select supplements before building the table. Its order is consumption order, never the order in which a
        // related location happened to be reported; omitted locations do not introduce unused table entries.
        var related = new DiagnosticRelatedFact[order.Count][];
        var remap = new int[this.sources.Count];
        Array.Fill(remap, -1);
        for (var i = 0; i < order.Count; i++)
        {
            var fact = facts[order[i]];
            related[i] = RelatedFacts(fact);
            if (fact.Source >= 0)
            {
                remap[fact.Source] = 0;
            }

            foreach (var item in related[i].AsSpan(0, Math.Min(related[i].Length, DiagnosticLimits.Related)))
            {
                if (item.Source >= 0)
                {
                    remap[item.Source] = 0;
                }
            }
        }

        var table = new List<DiagnosticSource>();
        for (var i = 0; i < remap.Length; i++)
        {
            if (remap[i] >= 0)
            {
                remap[i] = table.Count;
                table.Add(new(this.sources[i].Path, this.sources[i].IsInput));
            }
        }

        var records = new CheckDiagnostic[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            records[i] = this.CreateRecord(facts[order[i]], related[i], remap);
        }

        // SPEC 23.3.3: every rejected result publishes at least one Error; the fallbacks make this hold, so a violation is a defect.
        var result = new DiagnosticResult(records, table.ToArray());
        return rejected && !result.HasErrors ? throw Unexplained() : result;

        static DiagnosticContractException Unexplained()
            => new(DiagnosticFault.UnexplainedRejection, "The result is rejected but publishes no Error.");
    }

    internal static object? Capture(object? value)
        => value is null or string or bool or Enum or DiagnosticRequirement or byte or sbyte or short or ushort or int or uint or long or ulong or Int128 or UInt128 or decimal or float or double
            ? value : value.ToString();

    internal static object?[]? Capture(object?[]? values)
    {
        if (values is null)
        {
            return null;
        }

        var captured = new object?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            captured[i] = Capture(values[i]);
        }

        return captured;
    }

    internal static DiagnosticRelatedFact[] OrderRelated(DiagnosticRelatedFact[] related)
    {
        Array.Sort(related, static (x, y) =>
        {
            var order = string.CompareOrdinal(x.Role, y.Role);
            order = order != 0 ? order : (x.Source < 0 ? int.MaxValue : x.Source).CompareTo(y.Source < 0 ? int.MaxValue : y.Source);
            order = order != 0 ? order : (x.Length < 0 ? -1 : x.Start).CompareTo(y.Length < 0 ? -1 : y.Start);
            order = order != 0 ? order : x.Length.CompareTo(y.Length);
            return order != 0 ? order : string.CompareOrdinal(x.Label, y.Label);
        });
        return related;
    }

    internal void Record(DiagnosticPartition partition, int module, in DiagnosticFact fact, bool isError)
    {
        var list = partition == DiagnosticPartition.Syntax ? this.syntax[module] : this.partitions[(int)partition];

        // One problem, one fact: a repeated report merges its prerequisites and must agree on location and facts.
        list.Problems ??= [];
        if (list.Problems.TryGetValue((fact.Key, fact.Code), out var index))
        {
            var existing = list.Facts[index];
            // A Note or Advice that one report supplies merges; two different ones conflict like different facts.
            if (existing.Source != fact.Source || existing.Start != fact.Start || existing.Length != fact.Length ||
                !Equals(existing.First, fact.First) || !Equals(existing.Second, fact.Second) || Conflicts(existing.Note, fact.Note) || Conflicts(existing.Advice, fact.Advice) ||
                (existing.Evidence is { } recorded && fact.Evidence is { } reported && !recorded.SequenceEqual(reported)))
            {
                throw new DiagnosticContractException(DiagnosticFault.ConflictingProblem, $"{fact.Code} was reported twice with different locations or facts: [{existing.Start}+{existing.Length}] {existing.First} {existing.Second} {existing.Note} and [{fact.Start}+{fact.Length}] {fact.First} {fact.Second} {fact.Note}.");
            }

            list.Facts[index] = existing with
            {
                Note = existing.Note ?? fact.Note,
                Advice = existing.Advice ?? fact.Advice,
                Evidence = existing.Evidence ?? fact.Evidence,
                Related = fact.Related is { } related ? OrderRelated(Union(existing.Related, related)) : existing.Related,
                DerivedFrom = fact.DerivedFrom is { } derived ? Union(existing.DerivedFrom, derived) : existing.DerivedFrom,
            };

            return;
        }

        list.Problems.Add((fact.Key, fact.Code), list.Facts.Count);
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

    /// <summary>Finds the key of a lexical or syntax Error recorded for exactly one span of a module's document, so parser recovery at a
    /// token the lexer rejected rests on that Error instead of reporting the token again (SPEC 23.3.6.4).</summary>
    /// <param name="module">The module.</param>
    /// <param name="source">The source table index of the document.</param>
    /// <param name="range">The span.</param>
    /// <returns>The key, or <see langword="null"/> when no such Error was recorded.</returns>
    internal DiagnosticKey? SyntaxErrorAt(int module, int source, SourceSpan range)
    {
        if ((uint)module >= (uint)this.syntax.Count)
        {
            return null;
        }

        var facts = this.syntax[module].Facts;
        for (var i = 0; i < facts.Count; i++)
        {
            var key = facts[i].Key;
            if (key.Subject is null && key.Source == source && key.Start == range.Start && key.Length == range.Length && key.Requirement.Partition == DiagnosticPartition.Syntax &&
                DiagnosticEntries.TryGet(facts[i].Code, out var entry) && entry.Severity == DiagnosticSeverity.Error)
            {
                return key;
            }
        }

        return null;
    }

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

    // SPEC 23.3.6.2, 23.3.6.5: the code's facts, typed by its catalog schema (arguments, then evidence) and bounded once here;
    // the message and the label display the bounded values. The Types of one record are bounded as a pair.
    private static (DiagnosticValue[]? Reason, string Message, string? Label) Describe(DiagnosticEntry entry, in DiagnosticFact fact)
    {
        var arguments = entry.ArgumentSchema;
        var evidence = fact.Evidence is null ? [] : entry.EvidenceSchema;
        var count = arguments.Length + evidence.Length;
        if (count == 0)
        {
            return (null, entry.Message, entry.FormatLabel([]));
        }

        var values = new DiagnosticValue[count];
        var shown = new object?[count];
        var full = new string[count];
        var firstType = -1;
        var secondType = -1;
        for (var i = 0; i < count; i++)
        {
            var parameter = i < arguments.Length ? arguments[i] : evidence[i - arguments.Length];
            var value = i < arguments.Length ? (i == 0 ? fact.First : fact.Second) : fact.Evidence![i - arguments.Length];
            full[i] = value switch
            {
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty,
            };

            if (parameter.IsType)
            {
                secondType = firstType >= 0 && secondType < 0 ? i : secondType;
                firstType = firstType < 0 ? i : firstType;
            }

            var (bounded, elided) = parameter.Kind == DiagnosticValueKind.Text ? DiagnosticText.Bound(full[i]) : (full[i], false);
            values[i] = new(parameter.Name, parameter.Kind, bounded, elided);
            // A requirement is an exact fact by its stable name; the message and label display its phrase.
            shown[i] = parameter.Kind switch
            {
                DiagnosticValueKind.Number => value,
                DiagnosticValueKind.Requirement when value is DiagnosticRequirement requirement && DiagnosticRequirements.TryGetPhrase(requirement, out var phrase) => phrase,
                _ => bounded,
            };
        }

        if (secondType >= 0)
        {
            var (first, second) = DiagnosticText.BoundPair(full[firstType], full[secondType]);
            values[firstType] = values[firstType] with { Value = first.Text, Elided = first.Elided };
            values[secondType] = values[secondType] with { Value = second.Text, Elided = second.Elided };
            shown[firstType] = first.Text;
            shown[secondType] = second.Text;
        }

        var message = entry.FormatMessage(arguments.Length > 0 ? shown[0] : null, arguments.Length > 1 ? shown[1] : null);
        return (values, message, entry.FormatLabel(shown));
    }

    private static T[] Union<T>(T[]? left, T[] right)
        where T : struct
    {
        if (left is null)
        {
            return right;
        }

        var union = new HashSet<T>(left);
        union.UnionWith(right);
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
            if (facts[i].DerivedFrom is { } prerequisites)
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

        // A prerequisite denotes every Error at its check key, not just the first direct or explained Error. A key
        // containing an unresolved or cyclic derived Error must never release any of its dependents.
        var remaining = new Dictionary<DiagnosticKey, int>();
        var queue = new Queue<DiagnosticKey>();
        foreach (var fact in facts)
        {
            if (DiagnosticEntries.TryGet(fact.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error)
            {
                remaining[fact.Key] = remaining.GetValueOrDefault(fact.Key) + (fact.DerivedFrom is null ? 0 : 1);
            }
        }

        foreach (var (key, count) in remaining)
        {
            if (count == 0)
            {
                queue.Enqueue(key);
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
                    var completed = facts[index].Key;
                    if (--remaining[completed] == 0)
                    {
                        queue.Enqueue(completed);
                    }
                }
            }
        }

        return explained;
    }

    // SPEC 23.3.6.6: source table order, then span (none first), then the problem's identity: subject, code, requirement,
    // condition and context. Distinct problems with equal structural keys fault instead of using arrival order.
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

        if (order == 0)
        {
            order = left.Key.CompareSubject(right.Key);
        }

        if (order == 0)
        {
            order = string.CompareOrdinal(left.Code.ToString(), right.Code.ToString());
        }

        return order == 0 ? left.Key.CompareCheck(right.Key) : order;
    }

    private static DiagnosticRelatedFact[] RelatedFacts(in DiagnosticFact fact)
    {
        if (fact.DerivedFrom is not { } prerequisites)
        {
            return fact.Related ?? [];
        }

        var locations = new HashSet<DiagnosticRelatedFact>();
        foreach (var key in prerequisites)
        {
            if (!key.IsUnresolved && key.Source >= 0)
            {
                var label = DiagnosticRequirements.TryGetDescription(key.Requirement, out var description) ? description : key.Requirement.Name;
                locations.Add(new("prerequisite", key.Source, key.Start, key.Length, label));
            }
        }

        return OrderRelated(locations.ToArray());
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
        // Restrict work before copying or expanding tabs. Even a million-character line needs only a bounded window.
        // Tabs in a clipped window are expanded relative to that window; the record's Range keeps exact source offsets.
        var windowStart = text.Length <= DiagnosticLimits.ExcerptWidth ? 0 : Math.Max(0, startCharacter - DiagnosticLimits.ExcerptLead);
        var window = text.Slice(windowStart, Math.Min(text.Length - windowStart, DiagnosticLimits.ExcerptWidth + DiagnosticLimits.ExcerptLead));
        var expanded = ExpandTabs(window);
        var start = DisplayWidth(window[..(startCharacter - windowStart)]);
        var length = Math.Max(1, DisplayWidth(window[..Math.Min(endCharacter - windowStart, window.Length)]) - start);
        if (windowStart == 0 && window.Length == text.Length && expanded.Length <= DiagnosticLimits.ExcerptWidth)
        {
            return new(line + 1, expanded, start, length);
        }

        // Clip a long line to a window around the underline, marking each cut with an ellipsis.
        var from = Math.Clamp(start - DiagnosticLimits.ExcerptLead, 0, expanded.Length);
        var to = Math.Min(expanded.Length, from + DiagnosticLimits.ExcerptWidth);
        var prefix = windowStart > 0 || from > 0;
        var suffix = windowStart + window.Length < text.Length || to < expanded.Length;
        var clipped = string.Concat(prefix ? "…" : string.Empty, expanded.AsSpan(from, to - from), suffix ? "…" : string.Empty);
        var clippedStart = start - from + (prefix ? 1 : 0);
        return new(line + 1, clipped, clippedStart, Math.Max(1, Math.Min(length, clipped.Length - clippedStart)));
    }

    // Every recorded problem of the partitions is a candidate; suppression is decided only by explained prerequisites.
    private List<DiagnosticFact> Candidates(DiagnosticPartition first, DiagnosticPartition last)
    {
        var candidates = new List<DiagnosticFact>();
        for (var partition = first; partition <= last; partition++)
        {
            if (partition == DiagnosticPartition.Syntax)
            {
                foreach (var list in this.syntax)
                {
                    candidates.AddRange(list.Facts);
                }
            }
            else
            {
                candidates.AddRange(this.partitions[(int)partition].Facts);
            }
        }

        return candidates;
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

    private (SourceSpan? Span, SourceRange? Range) Locate(int source, int start, int length)
    {
        if (length < 0)
        {
            return (null, null);
        }

        var span = new SourceSpan(start, length);
        return (span, source >= 0 && this.sources[source].Document is { } document ? document.GetSourceRange(span) : null);
    }

    private CheckDiagnostic CreateRecord(in DiagnosticFact fact, DiagnosticRelatedFact[] locations, int[] remap)
    {
        DiagnosticEntries.TryGet(fact.Code, out var found);
        var entry = found!; // Every recorded code was validated against the catalog.
        var (span, range) = this.Locate(fact.Source, fact.Start, fact.Length);
        DiagnosticDisplay? display = null;
        List<DiagnosticOmission>? omissions = null;
        if (range is { } primary && this.sources[fact.Source].Document is { } document)
        {
            var lastLine = primary.End.Line;
            if (lastLine > primary.Start.Line && primary.End.Character == 0)
            {
                lastLine--;
            }

            lastLine = Math.Min(lastLine, document.LineCount - 1);
            var lines = new List<DiagnosticExcerptLine>();
            for (var line = primary.Start.Line; line <= lastLine; line++)
            {
                if (lines.Count == DiagnosticLimits.ExcerptLines - 1 && line < lastLine)
                {
                    (omissions ??= []).Add(new("excerpt lines", lastLine - line));
                    line = lastLine; // Keep the first lines and the last one.
                }

                var start = line == primary.Start.Line ? primary.Start.Character : 0;
                var end = line == primary.End.Line ? primary.End.Character : int.MaxValue;
                lines.Add(ExcerptLine(document, line, start, end));
            }

            display = new(range, lines.ToArray());
        }

        DiagnosticValue[]? reason;
        DiagnosticRelated[]? related = null;
        string message;
        string? label;
        if (fact.DerivedFrom is not null)
        {
            // SPEC 23.3.6.4: the requirement left undecided, what it needs, and where each prerequisite is.
            var requirement = fact.Key.Requirement;
            reason =
            [
                new("requirement", DiagnosticValueKind.Requirement, requirement.Name),
                new("condition", DiagnosticValueKind.Text, DiagnosticRequirements.TryGetDescription(requirement, out var description) ? description : requirement.Name),
            ];
            message = entry.Message;
            label = entry.Label;
        }
        else
        {
            (reason, message, label) = Describe(entry, fact);
        }

        if (locations.Length > 0)
        {
            related = new DiagnosticRelated[Math.Min(locations.Length, DiagnosticLimits.Related)];
            for (var i = 0; i < related.Length; i++)
            {
                var item = locations[i];
                var (relatedSpan, relatedRange) = this.Locate(item.Source, item.Start, item.Length);
                related[i] = new(item.Role, item.Source < 0 ? -1 : remap[item.Source], relatedSpan, relatedRange, item.Label is null ? null : DiagnosticText.Bound(item.Label).Text);
            }

            if (locations.Length > related.Length)
            {
                (omissions ??= []).Add(new("related locations", locations.Length - related.Length));
            }
        }

        return new(entry.Name, entry.Severity, entry.Category, message, fact.Source < 0 ? -1 : remap[fact.Source], span)
        {
            Label = label,
            Reason = reason,
            Related = related,
            Note = fact.Note is { } note ? DiagnosticText.Bound(note, DiagnosticLimits.NoteLength).Text : entry.Note,
            Advice = fact.Advice ?? entry.Advice,
            Omissions = omissions?.ToArray(),
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
