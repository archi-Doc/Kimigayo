// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// PLAN G949 S1-0b (design R1.4, coverage row c16): after the pass split the library pass never sees the program, so a program dependence
// hidden in today's library computation would vanish silently. Each row dumps the library layer after a Final Bind and compares it with
// the empty program's, beyond R8-M32's ten programs and its "Not compared" list:
// - Binding's private tables, ImplTable's, TyCtxt's and KimiLibrary's through their fields (Invalidate's five stores among them): a
//   table's library rows, those whose key or element names library declarations only (one naming a program declaration is the
//   program's, R1.3; one naming none is a builtin row); a table keyed by a number (TyCtxt's interned Types) lists its elements as rows;
//   the per-query answer tables (TypeMemo) hold no fact after a pass and are not listed;
// - every library declaration's symbol and scope, field by field: the Contract, Property, Schema and Adt contents, the slots R8-M32 does
//   not print, and the declaration query states;
// - the library's HIR rows, parsed and synthesized, as R8-M32's printer prints them (S1-0b r2, the dispositions review's C5);
// - the aggregates' library parts: Issues, Obligations, LibraryImports (Binding's lists) and the effect obligations.
// Values print through declarations and nodes (by location), Types (by printed form) and pass stamps (by their distance from the latest
// pass), so synthesized ids (R8-M32's Temporary 1) do not enter a table's row; rows compare as a sorted multiset. How a store compares
// is its disposition (S1-0b r2: the dispositions' §2 with the review's C1, C2 and C5), named in one table (LibraryLayer.Disposition);
// a store not in it is a fact:
// - fact: each program's rows equal the empty program's. Call scratch (suppliedExpansions, activeOriginRequirements and an Origin
//   requirement work's Dependents) is a fact once its call empties it at its end (G949 S1-e), so a leftover is a difference;
// - subset: a content interner or Type-keyed memo keeps every row of the empty program's unchanged, and a program's extra row is the
//   program's level (D2b-d §1.1, §1.5);
// - epoch: a memo of one capability epoch is the program's (D2b-d §1.1, census O16) and is not printed; its invariant is
//   TheCapabilityMemosEndWithTheirEpoch's;
// - number: a number naming a walk's or memo's order prints as #, the rest of its row compared; ArchitectureRulesTest's rows confine
//   its readers to the walk or memo that assigns it;
// - pool: a pool prints its live prefix, asserted slot by slot to be its live store's values in order, and not its free tail.
// Any other difference is a program fact in the library layer, never filtered: it moves to the program overlay by design (F decides).
// The layer is clean since G949 S1-e (S1-r1 waited for it): before it the rows differed in the lazy library facts S1-e makes eager (the
// dispositions' rows 1, 1b and 2), in the call scratch it empties (rows 8 and 17, and a library Origin requirement work's Dependents,
// which held the program's consumers) and in the synthesized nodes' HIR rows, which Milestones 27 and 30 numbered by their uses (R8-M32's
// Temporary 1). The fast row runs in every Unit, about 0.2 s per program; the corpus row (593 programs, about 75 to 220 s) is Explicit,
// run by each G949 unit and window.
public partial class KimiLibraryShapeTest
{
    private static readonly Lazy<string[]> EmptyProgramLayer = new(() => LibraryLayer.Dump(Bound(string.Empty)));

    /// <summary>Gets the fast row's programs: R8-M32's ten and every milestone.</summary>
    public static TheoryData<string> LayerPrograms
        => [string.Empty, "example/Hello", "example/Ranges", "example/Utf8Formatting", .. Names(Directory.GetFiles(
            DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi").Select(static x => "milestone/" + System.IO.Path.GetFileNameWithoutExtension(x)))];

    /// <summary>Gets the corpus row's programs: every other example and the diagnostic snapshot's mutations and syntax cases.</summary>
    public static TheoryData<string> LayerCorpus
        => [.. Names(Directory.GetDirectories(DiagnosticCorpus.RepositoryPath("docs", "examples")).Select(static x => "example/" + System.IO.Path.GetFileName(x))
            .Except(["example/Hello", "example/Ranges", "example/Utf8Formatting"])
            .Concat(DiagnosticCorpus.Mutations.Select(static x => "mutation/" + x.Name))
            .Concat(DiagnosticCorpus.SyntaxCases.Select(static x => "syntax/" + x.Name)))];

    // The dump itself: the empty program's layer twice, in two compilations, is the same (no hash order, identity or counter in a row);
    // and each disposition names a store or field that exists, so none widens a rule silently; every node family (NodeColumns) has
    // library rows in it, so a family the walk misses fails here.
    [Fact]
    public void TheLibraryLayerDumpIsDeterministic()
    {
        Assert.Empty(LibraryLayer.Unresolved());
        SameLayerAsTheEmptyPrograms(string.Empty);
        var dump = LibraryLayer.Dump(Bound(string.Empty));
        Assert.All(typeof(NodeColumns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic), x => Assert.Contains(dump, row => row.StartsWith(x.Name + " ", StringComparison.Ordinal)));
    }

    // The epoch rule's invariant: ResetCapabilities, which begins every pass (Binding.Pass), ends the capability epoch, so no entry of a
    // per-epoch memo is current after it. Every component summary is stale (its Epoch is not summaryEpoch); Enumerate's kept pool is
    // free (enumerateKeptPoolNext 0: the kept lists are enumerateKept's, review C1); every capability work holds CapabilityWork.Reset's
    // state; every other epoch store is empty. Reset leaves a work's Site and Fact (review N2). Site is the limit site of the work's own
    // Closure signature, its Use that signature's binder and its Needed flag cleared by ResetCapabilities (ClearNeededSites); only the
    // work's own evaluation reads it. Fact, the U fact of its last Unknown, is payload no fixpoint compares (design-r2 Amendment 4):
    // each evaluation rewrites it, but a consumer that requests the work while it waits in the queue reads it beside the reset Unknown
    // (RequestCapability's read of the queued work), so it is not asserted here (PLAN G949 S1-0b r2's report).
    [Fact]
    public void TheCapabilityMemosEndWithTheirEpoch()
    {
        var binding = Bound("example/Hello").Binding;
        typeof(Binding).GetMethod("ResetCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, [binding.Result.Mode]);
        var epoch = (int)LibraryLayer.ValueAt(binding, "Binding.summaryEpoch");
        string[] reset = ["Result", "Assumed", "Evaluated", "Queued", "Pending", "Parent", "Cause", "OwnCause", "Origin", "Parts", "Reads", "Dependents"];
        foreach (var store in LibraryLayer.StoresOf(LibraryLayer.Rule.Epoch))
        {
            var value = (IEnumerable)LibraryLayer.ValueAt(binding, store);
            switch (store)
            {
                case "Binding.summaries":
                    Assert.NotEmpty(value);
                    Assert.DoesNotContain(((IDictionary)value).Values.Cast<object>(), x => (int)Property(x, "Epoch")! == epoch);
                    break;
                case "Binding.capabilityNodes":
                    Assert.NotEmpty(value);
                    Assert.Empty(((IDictionary)value).Values.Cast<object>()
                        .SelectMany(x => reset.Where(name => Property(x, name) is not (null or false or ConstraintProof.Unknown or ICollection { Count: 0 }))));
                    break;
                case "Binding.enumerateKeptPool":
                    Assert.Equal(0, (int)LibraryLayer.ValueAt(binding, "Binding.enumerateKeptPoolNext"));
                    break;
                default:
                    Assert.Empty(value);
                    break;
            }
        }

        static object? Property(object x, string name) => x.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(x);
    }

    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryLayerIsIndependentOfTheProgram(string program) => SameLayerAsTheEmptyPrograms(program);

    [Theory(Explicit = true)] // Its length (PLAN G949 S1-0b): run by each G949 unit and window.
    [MemberData(nameof(LayerCorpus))]
    public void TheLibraryLayerIsIndependentOfTheCorpus(string program) => SameLayerAsTheEmptyPrograms(program);

    private static IEnumerable<string> Names(IEnumerable<string> names) => names.Order(StringComparer.Ordinal);

    private static void SameLayerAsTheEmptyPrograms(string program)
    {
        var (expected, actual) = (EmptyProgramLayer.Value, LibraryLayer.Dump(Bound(program)));
        var differences = new List<string>();
        var (e, a) = (0, 0);
        while (e < expected.Length || a < actual.Length)
        {
            var order = e == expected.Length ? 1 : a == actual.Length ? -1 : string.CompareOrdinal(expected[e], actual[a]);
            if (order == 0)
            {
                (e, a) = (e + 1, a + 1);
                continue;
            }

            if (order > 0 && LibraryLayer.RuleOf(actual[a]) == LibraryLayer.Rule.Subset)
            {
                a++; // The subset rule: the program's own row in a content interner or Type-keyed memo.
                continue;
            }

            differences.Add(order < 0 ? "- " + expected[e++] : "+ " + actual[a++]);
        }

        // The rows by store, then each store's first 12 rows, a long one from where it leaves the row before it (a "-" and its "+").
        var stores = differences.GroupBy(static x => x[..(x.IndexOf(' ', 2) is var i && i < 0 ? x.Length : i)]).Select(static x => $"{x.Key} {x.Count()}");
        var shown = differences.Select((x, i) => (Row: x, From: i == 0 ? 0 : Math.Max(0, CommonPrefix(x, differences[i - 1]) - 200)))
            .GroupBy(static x => x.Row[2..(x.Row.IndexOf(' ', 2) is var i && i < 0 ? x.Row.Length : i)]).SelectMany(static x => x.Take(12))
            .Select(static x => (x.From <= 2 ? x.Row[..2] : x.Row[..2] + "... ") + x.Row[Math.Max(2, x.From)..Math.Min(x.Row.Length, Math.Max(2, x.From) + 700)]);
        var message = $"{program}: {differences.Count} library-layer rows differ from the empty program's ({expected.Length} rows); by store: {string.Join(", ", stores)}";
        Assert.True(differences.Count == 0, message + "\n" + string.Join('\n', shown));
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = 2;
        while (n < a.Length && n < b.Length && a[n] == b[n])
        {
            n++;
        }

        return n;
    }

    // A program read from the repository or the diagnostic corpus, after its Final Bind.
    private static Compilation Bound(string program)
    {
        var c = Compilation.CreateForTest();
        var kind = program.Length == 0 ? string.Empty : program[..program.IndexOf('/', StringComparison.Ordinal)];
        var subject = program[(kind.Length == 0 ? 0 : kind.Length + 1)..];
        if (kind == "example" && Directory.GetFiles(DiagnosticCorpus.RepositoryPath("docs", "examples", subject), "*.kimi").Length == 0)
        {
            PrepareModules(c, DiagnosticCorpus.RepositoryPath("docs", "examples", subject));
        }
        else
        {
            var source = kind switch
            {
                "" => string.Empty,
                "milestone" => Repository.ReadText("tests", "milestones", subject + ".kimi"),
                "example" => File.ReadAllText(Directory.GetFiles(DiagnosticCorpus.RepositoryPath("docs", "examples", subject), "*.kimi").Single()),
                "syntax" => DiagnosticCorpus.Syntax(subject).Source,
                _ => DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(subject)),
            };
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        }

        c.Bind();
        return c;
    }

    // docs/examples/SourceDependencies: Application, its source dependency Geometry and Geometry's Math, linked as DependencyResolver links
    // them (ModuleBindingTest.Create's form).
    private static void PrepareModules(Compilation c, string folder)
    {
        c.Project.ProjectFile.Dependencies.Add("Geometry", new() { PackageId = "geometry", PackageVersion = "1", Project = "geometry.kimiproj" });
        var root = new DependencyNode("root", new("root.kimiproj", c.Project.ProjectFile, [], []), -1, string.Empty);
        var geometry = new DependencyNode("geometry@1", new("geometry.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 0, "Geometry");
        var math = new DependencyNode("math@1", new("math.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 1, "Math");
        root.Edges.Add("Geometry", geometry.Key);
        geometry.Edges.Add("Math", math.Key);
        Assert.True(c.Prepare(WindowsProfile.Target, new([root, geometry, math])));
        string[] modules = ["Application", "Geometry", "Math"];
        for (var i = 0; i < modules.Length; i++)
        {
            var source = Directory.GetFiles(System.IO.Path.Combine(folder, modules[i]), "*.kimi").Single();
            c.SourceModules[i].AddSource(new SourceDocument(System.IO.Path.GetFileName(source), File.ReadAllText(source)));
        }
    }

    /// <summary>The library layer of a bound compilation as sorted rows (store, key, value).</summary>
    private sealed class LibraryLayer
    {
        private const int Depth = 3; // Nesting printed inside a row.
        private const int Nesting = 3; // Objects whose fields are listed as stores below Binding (ImplTable, TyCtxt, their parts).

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsOf = new();

        // The dispositions (PLAN G949 S1-0b r2; the dispositions' §2 table with the review's C1): a store by its path from Binding, a
        // number by its type and field, a pool with its live store (a field beside it). A store not named here is a fact.
        private static readonly Dictionary<string, (Rule Rule, string? Live)> Disposition = new(StringComparer.Ordinal)
        {
            // Content interners and Type-keyed memos: the program's instantiations at builtin Types are its own rows.
            ["Binding.constraints"] = (Rule.Subset, null),
            ["Binding.types.types"] = (Rule.Subset, null),
            ["Binding.originSummaries"] = (Rule.Subset, null),
            ["Binding.erasureOutcomes"] = (Rule.Subset, null),
            ["Binding.pendingAssociatedTypes"] = (Rule.Subset, null),
            ["Binding.boundContracts"] = (Rule.Subset, null),
            ["Binding.storageMarks"] = (Rule.Subset, null),

            // Memos of one capability epoch (ClearCapabilityResults): component summaries, capability works and Enumerate's kept rows,
            // whose pool lends its lists to enumerateKept alone (C1).
            ["Binding.summaries"] = (Rule.Epoch, null),
            ["Binding.capabilityNodes"] = (Rule.Epoch, null),
            ["Binding.capabilityEdges"] = (Rule.Epoch, null),
            ["Binding.capabilityQueue"] = (Rule.Epoch, null),
            ["Binding.enumerateIndex"] = (Rule.Epoch, null),
            ["Binding.enumerateKept"] = (Rule.Epoch, null),
            ["Binding.enumerateKeptPool"] = (Rule.Epoch, null),
            ["Binding.enumerateHeads"] = (Rule.Epoch, null),

            // Numbers: Tarjan's visit order, the Origin requirement walk's consumer mark, a scope's view serial and the chain state it names.
            ["InlineSummary.Index"] = (Rule.Number, null),
            ["InlineSummary.Low"] = (Rule.Number, null),
            ["OriginRequirementWork.Consumer"] = (Rule.Number, null),
            ["BindingScope.ViewSerial"] = (Rule.Number, null),
            ["BindingScope.ViewState"] = (Rule.Number, null),

            // A pool whose live prefix is its live store's values in insertion order (the store is only cleared, never removed from).
            ["Binding.inlineSummaryPool"] = (Rule.Pool, "inlineSummaries"),
        };

        // The number rule's fields, by declaring type and name.
        private static readonly HashSet<(Type, string)> Numbers = [.. Disposition.Where(static x => x.Value.Rule == Rule.Number)
            .Select(static x => NumberField(x.Key)).Where(static x => x.HasValue).Select(static x => x!.Value)];

        // Compilation-wide roots and the source text: never listed (the HIR rows print through R8-M32's printer).
        private static readonly HashSet<Type> Roots = [typeof(Compilation), typeof(Kotonoha), typeof(CodeContext), typeof(SyntaxTable), typeof(HirTables)];

        // Per-query answer tables (Binding.TypeMemo, DECISIONS BU-19): an entry is read only by the query that wrote it, and the next query
        // begun evicts it, so after a pass no entry is a fact; never listed.
        private static readonly Type PerQuery = typeof(Binding).GetNestedType("TypeMemo`2", BindingFlags.NonPublic)!;

        // Objects listed as stores, never expanded inside a row.
        private static readonly HashSet<Type> Stores = [.. Roots, typeof(Binding), typeof(TyCtxt), typeof(ImplTable), typeof(KimiLibrary)];

        private readonly Kotonoha library;
        private readonly Printer printer = new(false);
        private readonly ulong pass;
        private readonly HashSet<object> listed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> printing = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<BoundType, Side> typeOwners = new(ReferenceEqualityComparer.Instance);
        private readonly List<string> rows = [];

        private LibraryLayer(Compilation c)
        {
            this.library = c.Library.Kotonoha;
            this.pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        }

        // How a store compares (the class header's list); Fact is a store's rule when the table does not name it.
        internal enum Rule
        {
            Fact,
            Subset,
            Epoch,
            Number,
            Pool,
        }

        private enum Side
        {
            None,
            Library,
            Program,
        }

        internal static string[] Dump(Compilation c)
        {
            var layer = new LibraryLayer(c);
            layer.Store("Binding", c.Binding, 0);

            // Binding's node families (NodeColumns, DECISIONS BU-220) live in each module's HIR tables, which no store walks: by one rule
            // every family of every module, its rows' objects (a row's fields, an array's and a pair's elements) by the family's name.
            foreach (var module in c.SourceModules.Select(static x => x.Syntax).Append(c.Library.Kotonoha.Syntax))
            {
                foreach (var family in Fields(typeof(NodeColumns)))
                {
                    foreach (var value in RowObjects(family.GetValue(module.Hir.Families)!))
                    {
                        layer.Row(Name(family), value);
                    }
                }
            }

            foreach (var value in ((IDictionary)typeof(Binding).GetField("scopes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!).Values)
            {
                layer.Row("scope", value);
            }

            // The library's HIR rows (review C5): the parsed tree's, then each synthesized node's, each row headed by its node's id.
            var (hir, table) = (new Printer(true), c.Library.Kotonoha.Syntax);
            hir.Visit(c.Library.Kotonoha.RootKoto);
            for (var id = table.ParsedCount + 1; id <= table.Count; id++)
            {
                hir.Node(table.Nodes[id]!);
            }

            layer.rows.AddRange(hir.Hir.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => "hir " + x));
            var effects = new List<EffectObligation>(); // Computed on request (MIR's), after the tables: its lazy writes (census O5) stay out of them.
            c.Binding.EffectObligations(effects);
            layer.Table("EffectObligations", effects);
            return [.. layer.rows.Order(StringComparer.Ordinal)];
        }

        // A row's rule, by its store (a number-keyed table's rows are its elements, "[]").
        internal static Rule RuleOf(string row)
        {
            var store = row[..(row.IndexOf(' ', StringComparison.Ordinal) is var i && i < 0 ? row.Length : i)];
            return Disposition.GetValueOrDefault(store.EndsWith("[]", StringComparison.Ordinal) ? store[..^2] : store).Rule;
        }

        internal static IEnumerable<string> StoresOf(Rule rule) => Disposition.Where(x => x.Value.Rule == rule).Select(static x => x.Key);

        // The dispositions naming no store or field: a store's path from Binding, a pool's live store beside it, a number's field of an
        // integer or a tuple of integers.
        internal static IEnumerable<string> Unresolved()
            => Disposition.Where(static x => x.Value.Rule == Rule.Number ? NumberField(x.Key) is null
                : FieldAt(x.Key) is null || (x.Value.Live is { } live && FieldAt(x.Key[..(x.Key.LastIndexOf('.') + 1)] + live) is null)).Select(static x => x.Key);

        // A store's value in a Binding, by its path.
        internal static object ValueAt(Binding binding, string store)
        {
            object value = binding;
            foreach (var name in store.Split('.').Skip(1))
            {
                value = Fields(value.GetType()).Single(x => Name(x) == name).GetValue(value)!;
            }

            return value;
        }

        private static FieldInfo? FieldAt(string store)
        {
            var (type, field) = (typeof(Binding), (FieldInfo?)null);
            foreach (var name in store.StartsWith("Binding.", StringComparison.Ordinal) ? store.Split('.').Skip(1) : Enumerable.Empty<string>())
            {
                field = Fields(type).SingleOrDefault(x => Name(x) == name);
                if (field is null)
                {
                    return null;
                }

                type = field.FieldType;
            }

            return field;
        }

        private static (Type, string)? NumberField(string entry)
        {
            var (type, name) = (entry[..entry.IndexOf('.', StringComparison.Ordinal)], entry[(entry.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            return typeof(Binding).Assembly.GetTypes().Where(x => x.Name == type).ToArray() is [var owner] &&
                Fields(owner).SingleOrDefault(x => Name(x) == name) is { } field && Integral(field.FieldType) ? (field.DeclaringType!, name) : null;
        }

        private static bool Integral(Type type)
            => Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.UInt64 ||
                (type.IsValueType && typeof(ITuple).IsAssignableFrom(type) && type.GetGenericArguments().All(Integral));

        // A family column's present rows (PagedColumn.Enumerate), each flattened to its objects.
        private static IEnumerable<object> RowObjects(object column)
        {
            var rows = column.GetType().GetMethod("Enumerate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(column, null)!;
            var (moveNext, current) = (rows.GetType().GetMethod("MoveNext")!, rows.GetType().GetProperty("Current")!);
            while ((bool)moveNext.Invoke(rows, null)!)
            {
                var row = current.GetValue(rows)!;
                foreach (var part in Fields(row.GetType()).Select(x => x.GetValue(row)).SelectMany(static x => x is Array items ? items.Cast<object?>() : [x]))
                {
                    foreach (var value in part is ITuple pair ? Enumerable.Range(0, pair.Length).Select(i => pair[i]) : [part])
                    {
                        if (value is not null)
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        private static FieldInfo[] Fields(Type type)
            => FieldsOf.GetOrAdd(type, static t => [.. Hierarchy(t).SelectMany(static x => x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))]);

        private static IEnumerable<Type> Hierarchy(Type? type)
        {
            for (; type is not null && type != typeof(object) && type != typeof(ValueType); type = type.BaseType)
            {
                yield return type;
            }
        }

        private static string Name(FieldInfo field) => field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;

        // A compiler object or a tuple, printed field by field inside a row; any other value prints as a leaf.
        private static bool Expandable(object value)
            => !Stores.Contains(value.GetType()) && value is not Delegate &&
                (value.GetType().Assembly == typeof(Binding).Assembly || value is ITuple || value.GetType().Name.StartsWith("KeyValuePair", StringComparison.Ordinal));

        // A table's elements; a dictionary's as DictionaryEntry (IEnumerable gives KeyValuePair).
        private static IEnumerable<object?> Items(object table)
        {
            if (table is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    yield return entry;
                }

                yield break;
            }

            foreach (var item in (IEnumerable)table)
            {
                yield return item;
            }
        }

        private static bool Leaf(object value) => value is Koto or BindingSymbol or BindingScope or BoundType or SourceRef;

        private static bool Scalar(object? value) => value is null or string or Enum || value.GetType().IsPrimitive;

        // Every table of an object: Binding, then ImplTable, TyCtxt, KimiLibrary and the other compiler objects it holds.
        private void Store(string store, object owner, int nesting)
        {
            this.listed.Add(owner);
            if (owner.GetType().IsGenericType && owner.GetType().GetGenericTypeDefinition() == PerQuery)
            {
                return;
            }

            foreach (var field in Fields(owner.GetType()))
            {
                var (name, value) = ($"{store}.{Name(field)}", field.GetValue(owner));
                var (rule, live) = Disposition.GetValueOrDefault(name);
                if (rule == Rule.Epoch)
                {
                    continue; // The program's memo, not the library layer.
                }

                if (rule == Rule.Pool)
                {
                    this.Pool(name, (IList)value!, (IDictionary)Fields(owner.GetType()).Single(x => Name(x) == live).GetValue(owner)!);
                }
                else if (value is IEnumerable and not string)
                {
                    this.Table(name, value);
                }
                else if (value is not null && nesting < Nesting && value.GetType().Assembly == typeof(Binding).Assembly && !Roots.Contains(value.GetType())
                    && !Leaf(value) && !this.listed.Contains(value))
                {
                    this.Store(name, value, nesting + 1);
                }
            }
        }

        // The pool rule: the live prefix, each slot the live store's value at its position (insertion order), and not the free tail; a
        // slot that is not fails the row instead of hiding a fact.
        private void Pool(string store, IList pool, IDictionary live)
        {
            var i = 0;
            foreach (var value in live.Values)
            {
                Assert.True(i < pool.Count && ReferenceEquals(pool[i], value), $"{store}[{i}] is not its live store's value {i}");
                this.Element(store, pool[i++]);
            }
        }

        // A table's library rows: keyed entries by key (a name-keyed entry by its value), a list's or set's elements. A table keyed by a
        // number (TyCtxt's Types by hash) lists its values as elements. Rows compare as a multiset: a list's order is not compared (c15
        // compares the aggregates' order at base and head).
        private void Table(string store, object table)
        {
            foreach (var item in Items(table))
            {
                if (item is DictionaryEntry entry && entry.Key.GetType().IsPrimitive)
                {
                    this.Element(store + "[]", entry.Value);
                }
                else if (item is DictionaryEntry keyed)
                {
                    if (this.Owner(keyed.Key is string ? keyed.Value : keyed.Key, 0) == Side.Library)
                    {
                        this.rows.Add($"{store} {this.Print(keyed.Key, 0)} = {this.Print(keyed.Value, 0)}");
                    }
                }
                else
                {
                    this.Element(store, item);
                }
            }
        }

        private void Element(string store, object? item)
        {
            if (item is IEnumerable and not string)
            {
                this.Table(store, item);
            }
            else if (!Scalar(item) && this.Owner(item, 0) == Side.Library)
            {
                this.rows.Add($"{store} {this.Print(item, 0)}");
            }
        }

        // Whether a value names library declarations only (Library), any program declaration (Program) or none (None).
        private Side Owner(object? value, int depth)
        {
            switch (value)
            {
                case null:
                    return Side.None;
                case Koto node:
                    return ReferenceEquals(node.CodeContext?.Kotonoha, this.library) ? Side.Library : Side.Program;
                case BindingSymbol symbol:
                    return this.Owner(symbol.Declaration, depth);
                case BindingScope scope:
                    return this.Owner(scope.Owner, depth);
                case SourceRef id:
                    return id.Module == SyntaxTable.LibraryModule ? Side.Library : Side.Program;
                case BoundType type:
                    return this.TypeOwner(type);
            }

            var side = Side.None;
            if (depth >= Depth || Scalar(value) || !(value is IEnumerable || Expandable(value)))
            {
                return side;
            }

            foreach (var part in this.Parts(value))
            {
                side = (Side)Math.Max((int)side, (int)this.Owner(part, depth + 1));
                if (side == Side.Program)
                {
                    break;
                }
            }

            return side;
        }

        // A Type's side at any depth (a program Type argument may sit deep inside a library Type), once per interned Type.
        private Side TypeOwner(BoundType type)
        {
            if (this.typeOwners.TryGetValue(type, out var side))
            {
                return side;
            }

            this.typeOwners[type] = Side.None; // A Type met again inside itself (a Closure's context) adds nothing.
            foreach (var part in this.Parts(type))
            {
                side = (Side)Math.Max((int)side, (int)this.Owner(part, 1));
            }

            return this.typeOwners[type] = side;
        }

        private IEnumerable<object?> Parts(object value)
        {
            if (value is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    yield return entry.Key;
                    yield return entry.Value;
                }

                yield break;
            }

            if (value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    yield return item;
                }

                yield break;
            }

            foreach (var field in Fields(value.GetType()))
            {
                yield return field.GetValue(value);
            }
        }

        private string Print(object? value, int depth)
        {
            switch (value)
            {
                case null:
                    return "-";
                case string text:
                    return text;
                case Koto node:
                    return Where(node);
                case BindingSymbol symbol:
                    return $"{symbol.Kind}:{symbol.Name}@{Where(symbol.Declaration)}";
                case BindingScope scope:
                    return "scope@" + Where(scope.Owner);
                case BoundType type:
                    return this.printer.Type(type);
                case SourceRef id:
                    return id.Module == SyntaxTable.LibraryModule ? "@" + Where(this.library.Syntax.Nodes[id.Id]) : $"{id.Module}#{id.Id}";
                case not null when Scalar(value):
                    return Convert.ToString(value, CultureInfo.InvariantCulture)!;
                case IEnumerable items:
                    return depth >= Depth ? "[..]" : this.Items(items, depth);
            }

            if (depth >= Depth || !Expandable(value) || !this.printing.Add(value))
            {
                return value.GetType().Name;
            }

            var fields = this.Fields(value, depth);
            this.printing.Remove(value);
            return fields;
        }

        private string Items(IEnumerable items, int depth)
        {
            var printed = new List<string>();
            if (items is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    // A number key (a hash) prints no key, as a table keyed by one lists its values.
                    printed.Add(entry.Key.GetType().IsPrimitive ? this.Print(entry.Value, depth + 1) : $"{this.Print(entry.Key, depth + 1)}:{this.Print(entry.Value, depth + 1)}");
                }
            }
            else
            {
                foreach (var item in items)
                {
                    printed.Add(this.Print(item, depth + 1));
                }
            }

            return "[" + string.Join(", ", items is IList ? printed : printed.Order(StringComparer.Ordinal)) + "]";
        }

        private string Fields(object value, int depth)
        {
            var text = new StringBuilder(value.GetType().Name).Append('{');
            foreach (var field in Fields(value.GetType()))
            {
                var (name, x) = (Name(field), field.GetValue(value));
                var stamp = x is ulong or uint && (name.Contains("pass", StringComparison.OrdinalIgnoreCase) || name.Contains("version", StringComparison.OrdinalIgnoreCase));
                text.Append(name).Append('=').Append(Numbers.Contains((field.DeclaringType!, name)) ? "#"
                    : stamp ? this.Stamp(Convert.ToUInt64(x, CultureInfo.InvariantCulture)) : this.Print(x, depth + 1)).Append(' ');
            }

            return text.Append('}').ToString();
        }

        // A pass stamp by its distance from the latest pass (Binding's storageVersion).
        private string Stamp(ulong stamp)
            => stamp == 0 ? "0" : stamp <= this.pass ? $"pass-{this.pass - stamp}" : stamp.ToString(CultureInfo.InvariantCulture);

        // A library object's row, field by field, a symbol's with its query states.
        private void Row(string store, object? value)
        {
            if (this.Owner(value, 0) == Side.Library)
            {
                this.rows.Add($"{store} {this.Fields(value!, 0)}{(value is BindingSymbol s ? this.Queries(s) : string.Empty)}");
            }
        }

        private string Queries(BindingSymbol symbol)
            => " queries=" + string.Join(',', Enum.GetValues<DeclQueryKind>().Select(x => $"{x}:{symbol.QueryState(x, this.pass)}"));
    }
}
