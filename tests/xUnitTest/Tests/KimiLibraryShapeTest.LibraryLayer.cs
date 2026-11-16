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
// - the aggregates' library parts: Issues, Obligations, LibraryImports (Binding's lists) and the effect obligations.
// Values print through declarations and nodes (by location), Types (by printed form) and pass stamps (by their distance from the latest
// pass), so synthesized ids (R8-M32's Temporary 1) do not enter; rows compare as a sorted multiset. Any difference is a program fact in
// the library layer, never filtered: it moves to the program overlay by design (F decides). Both rows fail at dev a63b1598b and are
// Explicit until the layer is clean (G949's S1-r1 waits for it); the fast row takes about 0.5 s per program.
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

    // The dump itself: the empty program's layer twice, in two compilations, is the same (no hash order, identity or counter in a row).
    [Fact]
    public void TheLibraryLayerDumpIsDeterministic() => SameLayerAsTheEmptyPrograms(string.Empty);

    [Theory(Explicit = true)] // Until the layer is clean (PLAN G949 S1-0b's report).
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryLayerIsIndependentOfTheProgram(string program) => SameLayerAsTheEmptyPrograms(program);

    [Theory(Explicit = true)] // Until the layer is clean (PLAN G949 S1-0b's report).
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

        // Compilation-wide roots and the source text: never listed (the HIR tables are R8-M32's).
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
            foreach (var (store, field) in new[] { ("symbol", "symbols"), ("scope", "scopes") })
            {
                foreach (var value in ((IDictionary)typeof(Binding).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!).Values)
                {
                    if (layer.Owner(value, 0) == Side.Library)
                    {
                        layer.rows.Add($"{store} {layer.Fields(value!, 0)}{(value is BindingSymbol s ? layer.Queries(s) : string.Empty)}");
                    }
                }
            }

            var effects = new List<EffectObligation>(); // Computed on request (MIR's), after the tables: its lazy writes (census O5) stay out of them.
            c.Binding.EffectObligations(effects);
            layer.Table("EffectObligations", effects);
            return [.. layer.rows.Order(StringComparer.Ordinal)];
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
                var value = field.GetValue(owner);
                if (value is IEnumerable and not string)
                {
                    this.Table($"{store}.{Name(field)}", value);
                }
                else if (value is not null && nesting < Nesting && value.GetType().Assembly == typeof(Binding).Assembly && !Roots.Contains(value.GetType())
                    && !Leaf(value) && !this.listed.Contains(value))
                {
                    this.Store($"{store}.{Name(field)}", value, nesting + 1);
                }
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
                text.Append(name).Append('=').Append(stamp ? this.Stamp(Convert.ToUInt64(x, CultureInfo.InvariantCulture)) : this.Print(x, depth + 1)).Append(' ');
            }

            return text.Append('}').ToString();
        }

        // A pass stamp by its distance from the latest pass (Binding's storageVersion).
        private string Stamp(ulong stamp)
            => stamp == 0 ? "0" : stamp <= this.pass ? $"pass-{this.pass - stamp}" : stamp.ToString(CultureInfo.InvariantCulture);

        private string Queries(BindingSymbol symbol)
            => " queries=" + string.Join(',', Enum.GetValues<DeclQueryKind>().Select(x => $"{x}:{symbol.QueryState(x, this.pass)}"));
    }
}
