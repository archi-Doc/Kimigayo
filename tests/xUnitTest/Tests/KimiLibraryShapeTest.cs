// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

/// <summary>The shapes of the embedded Kimi library that the compiler and the static <c>.ll.in</c> runtime rely on (SPEC 22.1).
/// A shape is the declaration's source header with its direct member lines; Binding gives the same text the same meaning.</summary>
public partial class KimiLibraryShapeTest
{
    // Read-only: no test changes the library or its Binding. The library of a compilation without a target is windows-x64's selection
    // (the Tatara plan's F1 (a)); each row's library, staged rows included, is selected under its own Conditions (B7b6: no Kimi.Windows
    // where `windows` does not hold, Time over the platform layer's clock), bound once on its first read (LibraryOf).
    private static readonly Lazy<Compilation> Library = new(() => BoundLibrary(null));
    private static readonly Dictionary<string, Lazy<Compilation>> Libraries = TargetProfile.AllRows.Select(static x => x.Target).Prepend(string.Empty)
        .ToDictionary(static x => x, static x => x.Length == 0 ? Library : new Lazy<Compilation>(() => BoundLibrary(x)));

    // PLAN G776-b (the owner's O-194 (a): a built-in conformance adds no member): the receiver groups no witness-table row names, the
    // fixed array's member group (SPEC 22.1).
    private static readonly WitnessMembers[] ReceiverOnlyGroups = [WitnessMembers.FixedArray];

    // R8-S1 C1: what a witness group declares for each Contract it serves: the Contract's own requirement by name, with the group's `self`.
    private static readonly (WitnessMembers Group, KimiDeclarationId Contract, string Header)[] WitnessRequirements =
    [
        (WitnessMembers.FixedArrayEntries, KimiDeclarationId.Iterable,
            "public func iterate<E, length N>(self: ref/[N of E] during source) -> ArrayIterator<E> during source"),
        (WitnessMembers.FixedArrayEntries, KimiDeclarationId.UniqIterable,
            "public func iterateUniq<E, length N>(self: uniq/[N of E] during source) -> ArrayUniqIterator<E> during source"),
        (WitnessMembers.FixedArrayEntries, KimiDeclarationId.IntoIterable,
            "public func intoIterator<E, length N>(self: [N of E]) -> FixedArrayOwningIterator<E, [N of E]>"),
        (WitnessMembers.IntegerPosition, KimiDeclarationId.Position, "public func tryResolve<I>(self: I, length: isize) -> Option<isize>"),
        (WitnessMembers.String, KimiDeclarationId.Equatable, "func equals(self: ref/string, other: ref/string) -> bool"),
        (WitnessMembers.String, KimiDeclarationId.Comparable, "func compare(self: ref/string, other: ref/string) -> i32"),
        (WitnessMembers.BoolFormat, KimiDeclarationId.Utf8Format, "func format(self: ref/bool, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.CharFormat, KimiDeclarationId.Utf8Format, "func format(self: ref/char, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.UnitFormat, KimiDeclarationId.Utf8Format, "func format(self: ref/(), writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.Utf8SliceFormat, KimiDeclarationId.Utf8Format,
            "func format(self: ref/Text.Utf8Slice, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.F32Format, KimiDeclarationId.Utf8Format, "func format(self: ref/f32, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.F64Format, KimiDeclarationId.Utf8Format, "func format(self: ref/f64, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.String, KimiDeclarationId.Utf8Format, "func format(self: ref/string, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.WrappingFormat, KimiDeclarationId.Utf8Format, "func format<I>(self: ref/Wrapping<I>, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
        (WitnessMembers.IntegerFormat, KimiDeclarationId.Utf8Format, "func format<I>(self: ref/I, writer: uniq/Utf8Writer) -> Result<(), BufferFull>"),
    ];

    // Code generation and the runtime call these bodiless functions by position: parameter order,
    // Types, overload position and constraints. Those with a lang item by it, the others by their library path (UnnamedCompilerFunctionShapes).
    private static readonly (KimiDeclarationId Id, string Shape)[] CompilerFunctionShapes =
    [
        (KimiDeclarationId.TestTempDirectory, "public func tempDirectory() -> string"),
        (KimiDeclarationId.MakeObj, "public func makeObj<T>(value: T) -> obj/T\nT is ObjectPayload"),
        (KimiDeclarationId.MakeRc, "public func makeRc<T>(value: T) -> rc/T\nT is ObjectPayload"),
        (KimiDeclarationId.MakeArc, "public func makeArc<T>(value: T) -> arc/T\nT is ObjectPayload"),
        (KimiDeclarationId.Clone, "public func clone<s/T>(value: ref/(s/T)) -> s/T\ns is rc or arc"),
        (KimiDeclarationId.ArrayWithCapacity, "public init(! capacity: isize)"),
        (KimiDeclarationId.ArrayRepeating, "public init(! repeating: T, count: isize)\nT is Copy"),
        (KimiDeclarationId.StorageTransferBytes, "internal unsafe func transferBytes(destination: raw/u8, source: raw/u8, count: isize)"),
        (KimiDeclarationId.RawRelease, "public unsafe func release<T>(storage: raw/T)"),
        (KimiDeclarationId.RawInitialize, "public unsafe func initialize<T>(storage: raw/T, value: T)"),
        (KimiDeclarationId.RawSlice, "public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s"),
        (KimiDeclarationId.ViewStart, "internal unsafe func viewStart<P, R>(storage: P, start: isize) -> P\nR is PositionRange"), // The builder's Form (L2).
        (KimiDeclarationId.RawReadVolatile, "public unsafe func readVolatile<T>(storage: raw/T) -> T\nT is PrimitiveInteger"),
    ];

    // The bodiless compiler functions without a lang item (R8-G1 G1-6: no C# reader names them), by their library path, each with the
    // value of the Condition `windows` under which the library declares it (null: on every target; the Tatara plan's B7b6:
    // Time.monotonicNanoseconds where `windows` does not hold, linux-x64's and none-x64's library).
    private static readonly (string Path, string Shape, bool? Windows)[] UnnamedCompilerFunctionShapes =
    [
        ("Console.writeStdout", "internal unsafe func writeStdout(data: raw/u8, length: isize)", null),
        ("Storage.missingDictionaryKey", "internal func missingDictionaryKey() -> Never", null),
        ("Storage.countOverflow", "internal func countOverflow() -> Never", null),
        ("Storage.allocationSizeExceeded", "internal func allocationSizeExceeded() -> Never", null),
        ("Storage.clockFailure", "internal func clockFailure() -> Never", null),
        ("Storage.tryAllocateBytes", "internal func tryAllocateBytes(bytes: isize) -> raw/u8", null),
        ("Storage.stride", "internal func stride<T>() -> isize", null),
        ("Storage.destroyValues", "internal unsafe func destroyValues<T>(storage: raw/u8, count: isize)", null),
        ("Storage.erasedReserve", "internal func erasedReserve(writer: uniq/Utf8Writer, minimum: isize) -> Result<WriteWindow during writer.target, BufferFull>", null),
        ("Storage.reserveDispatch", "internal func reserveDispatch<W>() -> raw/u8 during static\nW is BufferWriter", null),
        ("Storage.formatFloat", "internal func formatFloat(bits: u64 ! narrow: bool, destination: uniq/[24 of u8]) -> isize", null),
        ("Storage.formatInteger128", "internal func formatInteger128(value: u128 ! destination: uniq/[40 of u8]) -> isize", null),
        ("Raw.allocate", "public func allocate<T>(count: isize) -> raw/T", null),
        ("Raw.writeVolatile", "public unsafe func writeVolatile<T>(storage: raw/T, value: T)\nT is PrimitiveInteger", null),
        ("Storage.argumentOutOfRange", "internal func argumentOutOfRange() -> Never", null),
        ("Storage.indexBounds", "internal func indexBounds() -> Never", null),
        ("Time.monotonicNanoseconds", "func monotonicNanoseconds() -> i64", false),
    ];

    public static TheoryData<KimiDeclarationId> CatalogEntries
    {
        get
        {
            var data = new TheoryData<KimiDeclarationId>();
            foreach (var entry in KimiLibraryCatalog.Entries)
            {
                data.Add(entry.Id);
            }

            return data;
        }
    }

    public static TheoryData<KimiDeclarationId, string> CompilerFunctions
    {
        get
        {
            var data = new TheoryData<KimiDeclarationId, string>();
            foreach (var (id, shape) in CompilerFunctionShapes)
            {
                data.Add(id, shape);
            }

            return data;
        }
    }

    public static TheoryData<string, string, string> UnnamedCompilerFunctions
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (path, shape, windows) in UnnamedCompilerFunctionShapes)
            {
                foreach (var target in windows is null ? [string.Empty] : Libraries.Keys.Where(x => WindowsHolds(x) == windows))
                {
                    data.Add(path, shape, target);
                }
            }

            return data;
        }
    }

    [Theory]
    [InlineData("", 84)]
    [InlineData("x86_64-unknown-linux-musl", 85)]
    [InlineData("x86_64-unknown-none", 85)]
    public void LibraryBindsCompletely(string target, int unnamed)
    {
        var c = LibraryOf(target);
        Assert.False(c.Diagnostics.HasSyntaxErrors(c.Library.Kotonoha));
        Assert.Null(c.Library.Kotonoha.GeneratedFunction);
        // Text.fixed and FixedBuffer.text, a direct line's (R8-W2 a2); Never (G704); bool, char, Unit format (R8-S2a); f32, f64 format,
        // formatFloat (R8-S2c); string, view and Wrapping<T> format (R8-S2d1); the integers' format (R8-S2b1); formatInteger128 (R8-S2b2);
        // Utf8Writer.deferLiteral, an owning interpolation's literal before a value (R8-W2 c1); Utf8Writer.finish, its end (R8-W2 c3a).
        // R8-G1 G1-6 (the owner's O-307 (a)): only the lang items a C# reader identifies are cataloged; 84 other declarations carry
        // `#Intrinsic` or `#CallerLocation` and are registered unnamed (Storage.reserveDispatch, R8-W2 b2-1, among them; PLAN G71's
        // three `splitAt`, four position and range `format` bodies, Array `index` and `indexUniq`, and seven Time functions; the Time unit's
        // `Storage.clockFailure` and `Time.readCounterFrequency`; O-412 (1)'s Array.sort(by:), which destroys `by` inside the operation;
        // R8-X1's Storage.growthTarget, the growth target growArray and DictionaryStorage.grow share, PLAN G970; OSF2's Raw.writeVolatile,
        // whose pair Raw.readVolatile is cataloged for its NoStore row); the library where `windows` does not hold (linux-x64's,
        // none-x64's) adds Time.monotonicNanoseconds (B7b6).
        // G1-3b-2: every entry is sourced, the Weak family written `#Unsupported`. Platform (SPEC 22.4.5) is cataloged on every target.
        Assert.Equal(123, KimiLibraryCatalog.Entries.Length);
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            Assert.NotNull(c.Library.GetSymbol(entry.Id));
        }

        var registered = c.Library.RegisteredSymbols.ToArray();
        foreach (var symbol in registered)
        {
            Assert.Same(symbol, symbol.Declaration.SymbolOf(c));
            Assert.Equal(BindingState.Resolved, symbol.Declaration.StateOf(c));
        }

        Assert.Equal((123, unnamed), (registered.Count(static x => x.LibraryDeclaration is not null), registered.Count(static x => x.LibraryDeclaration is null)));
    }

    // DECISIONS BU-112: the library's trees are compiler-owned source, fixed at build time, so they are checked here and not walked at
    // run time: each lies within IMPL A.4's syntax depth L, measured as SyntaxNesting measures it (a node's distance from its root).
    [Fact]
    public void TheLibraryLiesWithinTheSyntaxNestingLimit()
        => WithinTheSyntaxNestingLimit(Compilation.CreateForTest().Library.Kotonoha.RootKoto);

    // R5 K package 4.4 (P5, DECISIONS R5-M41 D-B, amended by the Tatara plan's F3): the bundled library collected with every module a root
    // module, so every closed library body is a root, has no collector record; each target's library is one freeze (its declarations
    // selected by that target's Conditions), so rooting it in each compilation would add none, checked for the library without a target
    // and for each row's. Its own build has the library range, which builds every library body (PLAN G951).
    [Theory]
    [InlineData("")]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("x86_64-unknown-linux-musl")]
    [InlineData("x86_64-unknown-none")]
    public void TheLibraryCollectsWithoutRecords(string target)
    {
        var c = BoundLibrary(target.Length == 0 ? null : target);
        var program = new Kimi.Compiler.Mir.MirAnalysis(c.Types).Run(new Kimi.Compiler.Mir.MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), c.SourceModules.Length);
        var set = new Kimi.Compiler.Backend.InstanceCollector(c.Types, c.Layout).Run(program, SyntaxTable.LibraryModule + 1);
        Assert.Empty(program.Records(Kimi.Compiler.Mir.MirRecordClass.Collection));
        Assert.True(set.RootCount > 50 && set.Items.Length > set.RootCount, $"{set.RootCount} roots, {set.Items.Length} items");
    }

    // Registration binds each cataloged entry to the one declaration whose `#Lang` names it (row L1), under that declaration's name and
    // kind, in its declaring container's scope, with the body kind its role requires.
    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public void CatalogEntriesResolveExactlyOnce(KimiDeclarationId id)
    {
        var c = Library.Value;
        var entry = KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)];
        var symbol = c.Library.GetSymbol(id);
        Assert.NotNull(symbol);
        var declaration = symbol.Declaration;
        Assert.Equal(id.ToString(), LangName(declaration));
        Assert.Equal(declaration is FunctionKoto function ? (function.Name, BindingSymbolKind.Function) : (((DeclarationContainerKoto)declaration).Name, BindingSymbolKind.Type), (symbol.Name, symbol.Kind));
        Assert.Same(Container(declaration), Assert.IsAssignableFrom<DeclarationContainerKoto>(symbol.Scope.Owner));
        if (entry.Intrinsic != IntrinsicKind.None)
        {
            Assert.Equal(entry.Intrinsic, symbol.Intrinsic);
            Assert.Equal("public contract " + symbol.Name, Shape(declaration));
        }

        if (symbol.Definition.Kind is DispositionKind.Intrinsic or DispositionKind.Form || entry.Implementation is not null)
        {
            Assert.False(HasBody(declaration));
        }
        else if (declaration is FunctionKoto)
        {
            Assert.Equal(DispositionKind.Body, symbol.Definition.Kind);
            Assert.True(HasBody(declaration));
        }

        // SPEC 22.1: a linked public declaration is defined by an internal source function of the same container, which carries the
        // caller location (SPEC 22.5.1).
        if (entry.Implementation is { } implementation)
        {
            var linkedSymbol = c.Library.GetSymbol(implementation)!;
            Assert.Equal((DispositionKind.Body, DispositionKind.Body), (symbol.Definition.Kind, linkedSymbol.Definition.Kind));
            var linked = Assert.IsType<FunctionKoto>(linkedSymbol.Declaration);
            Assert.Equal(ModifierKind.Internal, linked.Modifier);
            Assert.True(HasBody(linked));
            Assert.Same(declaration.Parent, linked.Parent);
            Assert.True(KimiLibraryCatalog.RequiresCallerLocation(linkedSymbol));
        }
    }

    // Registration binds each declaration without a lang item that carries `#Intrinsic` or `#CallerLocation` (R8-G1 G1-6) under its name,
    // as a function, in its declaring container's scope, with the body kind its `#Intrinsic` states.
    [Fact]
    public void UnnamedDeclarationsResolveExactlyOnce()
    {
        var c = Library.Value;
        var unnamed = c.Library.RegisteredSymbols.ToArray().Where(static x => x.LibraryDeclaration is null).ToArray();
        Assert.Equal(84, unnamed.Length);
        foreach (var symbol in unnamed)
        {
            var function = Assert.IsType<FunctionKoto>(symbol.Declaration);
            Assert.Null(LangName(function));
            Assert.True(function.FindAttribute(AttributeKind.Intrinsic) is not null || function.FindAttribute(AttributeKind.CallerLocation) is not null, Line(function));
            Assert.Equal((function.Name, BindingSymbolKind.Function), (symbol.Name, symbol.Kind));
            Assert.Same(Container(function), Assert.IsAssignableFrom<DeclarationContainerKoto>(symbol.Scope.Owner));
            Assert.Equal(IntrinsicKind.None, symbol.Intrinsic);
            Assert.Equal(symbol.Definition.Kind != DispositionKind.Intrinsic, HasBody(function));
        }
    }

    // IMPL 21.3.4 (R8-G1 row L2): a function or constructor without a definition, outside a Contract, is a foreign import, or a
    // cataloged compiler function carrying `#Intrinsic` exactly when its symbol's Definition is an intrinsic, or a `#Lang`
    // constructor the catalog links to the internal function that defines it, or a `#Lang` function whose catalog entry is a Form row
    // (PLAN G597's L2: Storage.viewStart); a function with a definition carries no `#Intrinsic`.
    // An `#Unsupported` declaration omits its Body too (rows L9-L11, R8-G1 G1-3b-2). Every library source is ordinary
    // (R8-G1 G1-5): such a declaration omits its Body by its Attribute (FunctionKoto.OmitsBody).
    [Fact]
    public void BodilessFunctionsAreCompilerFunctions()
    {
        var c = Library.Value;
        var registered = c.Library.RegisteredSymbols.ToArray().ToHashSet();
        Visit(c.Library.Kotonoha.RootKoto);

        // SPEC 13.6.2-3, 22.1: Weak clone shares its name with the strong intrinsic. Each other Weak operation is alone in its group.
        // Weak itself has no members or synthesized constructor. Core.kimi is read before Intrinsics.kimi.
        var intrinsics = Assert.IsType<GroupKoto>(c.Library.MakeObj.Declaration.Parent); // Kimi.Intrinsics, makeObj's group (R8-G1 G1-5).
        var unsupported = LibraryAttributes(Library.Value, AttributeKind.Unsupported).Select(static x => x.Target).ToArray();
        Assert.Equal(["Weak", "clone", "downgrade", "upgrade", "makeRcCyclic", "makeArcCyclic"], unsupported.Select(static x => x is FunctionKoto function ? function.Name : ((StructKoto)x).Name));
        foreach (var function in unsupported.OfType<FunctionKoto>())
        {
            Assert.Same(intrinsics, function.Parent);
            Assert.False(function.IsSpecialization || HasBody(function) || function.FindAttribute(AttributeKind.Intrinsic) is not null, Line(function));
            var overloads = intrinsics.Members.OfType<FunctionKoto>().Where(x => x.Name == function.Name);
            if (function.Name == "clone")
            {
                Assert.Equal([c.Library.GetSymbol(KimiDeclarationId.Clone)!.Declaration, function], overloads.Cast<Koto>());
                const string Header = "public func clone<s/T>(value: ref/Weak<s/T>) -> Weak<s/T>\ns is rc or arc";
                Assert.Equal(Header, HeaderLines(function, Header));
            }
            else
            {
                Assert.Same(function, Assert.Single(overloads));
            }
        }

        var weak = Assert.IsType<StructKoto>(unsupported[0]);
        Assert.Same(weak, c.Library.GetSymbol(KimiDeclarationId.Weak)?.Declaration);
        Assert.Empty(weak.Members);
        Assert.Null(weak.SynthesizedConstructor);
        Assert.DoesNotContain(KotoTree.Walk(weak), x => x.SymbolOf(c) is { Kind: BindingSymbolKind.Function });

        void Visit(DeclarationContainerKoto container)
        {
            for (var i = 0; container is not ContractKoto && i < container.Members.Count; i++)
            {
                if (container.Members[i] is not FunctionKoto { IsRequirement: false } function)
                {
                    continue;
                }

                var intrinsic = function.FindAttribute(AttributeKind.Intrinsic) is not null;
                if (HasBody(function) || function.FindAttribute(AttributeKind.LibraryImport) is not null || function.FindAttribute(AttributeKind.Unsupported) is not null)
                {
                    Assert.False(intrinsic, Line(function));
                    continue;
                }

                // Its symbol is the one registration gives it: its lang item's, or an unnamed one (R8-G1 G1-6).
                var symbol = function.SymbolOf(c);
                Assert.True(symbol is not null && registered.Contains(symbol), Line(function));
                Assert.True(symbol!.LibraryDeclaration is not { } id ? LangName(function) is null : ReferenceEquals(c.Library.GetSymbol(id), symbol) && LangName(function) == id.ToString(), Line(function));
                var served = symbol.Definition.Kind == DispositionKind.Intrinsic;
                var linked = function.IsConstructor && symbol.Definition.Kind == DispositionKind.Body && symbol.LibraryDeclaration is { } linkedId && KimiLibraryCatalog.ImplementationOf(linkedId) is not null;
                var form = !function.IsConstructor && symbol.Definition.Kind == DispositionKind.Form;
                Assert.True(intrinsic == served && (served || linked || form), Line(function));
            }

            foreach (var nested in container.NestedContainers)
            {
                Visit(nested);
            }
        }
    }

    [Theory]
    [InlineData("", 29)]
    [InlineData("x86_64-unknown-linux-musl", 30)]
    [InlineData("x86_64-unknown-none", 30)]
    public void EveryCompilerFunctionHasAShape(string target, int count)
    {
        // Every registered intrinsic, Form function (L2) or linked constructor, named or not (R8-G1 G1-6), and no other declaration, has
        // its row: the rows of every target's library and those of this target's alone (B7b6).
        var library = LibraryOf(target).Library;
        var expected = new HashSet<Koto>(ReferenceEqualityComparer.Instance);
        foreach (var symbol in library.RegisteredSymbols)
        {
            if (symbol.Definition.Kind is DispositionKind.Intrinsic or DispositionKind.Form || (symbol.LibraryDeclaration is { } id && KimiLibraryCatalog.ImplementationOf(id) is not null))
            {
                expected.Add(symbol.Declaration);
            }
        }

        Koto[] shaped =
        [
            .. CompilerFunctionShapes.Select(x => library.GetSymbol(x.Id)!.Declaration),
            .. UnnamedCompilerFunctionShapes.Where(x => x.Windows is null || x.Windows == WindowsHolds(target)).Select(x => LibrarySource.Declaration(library, x.Path)),
        ];
        Assert.Equal((count, count), (expected.Count, shaped.Length));
        Assert.True(expected.SetEquals(shaped));
    }

    // Plan 5.8: every library function and $abort has one disposition, its symbol's Definition: an intrinsic serves at most one of them,
    // except Fail, which carries a fixed SPEC 22.5.2 code; a symbol of no compiler function has its Body; there are at most 30
    // intrinsics (OSF2's volatile pair, the owner's O-489; MonotonicClock, the Tatara plan's decision 25, the owner's O-478 (i)), the temporary ones of the owner's approvals of 2026-10-11 gone (FormatBuffer
    // at R8-W2 c3b, FormatFinish at c3a, FormatSegment, R5-M133's rename of FormatHint, at c2b). A built-in target's Shim is MirBuildTest's
    // census key.
    [Fact]
    public void EveryCompilerFunctionHasOneDisposition()
    {
        var spec = File.ReadAllText(System.IO.Path.Combine(Repository.Root, "docs", "spec", "22-core-execution-and-foreign-functions.md"));
        var library = Library.Value.Library;
        var symbols = library.RegisteredSymbols.ToArray().Append(library.Abort).ToArray();
        foreach (var symbol in symbols)
        {
            var row = symbol.Definition;
            var coded = row.Code is null ? row.Id != IntrinsicId.Fail
                : row.Id == IntrinsicId.Fail && (spec.Contains($"{row.Code} (", StringComparison.Ordinal) || spec.Contains($"`{row.Code}: ", StringComparison.Ordinal));
            Assert.True((row.Kind == DispositionKind.Intrinsic) == (row.Id != IntrinsicId.None) && row.Kind != DispositionKind.Shim && coded, symbol.Name);
        }

        var served = symbols.Select(static x => x.Definition.Id).Where(static x => x is not (IntrinsicId.None or IntrinsicId.Fail)).ToArray();
        Assert.Equal(served.Distinct(), served);
        Assert.Equal(new IntrinsicDisposition(DispositionKind.Intrinsic, IntrinsicId.Abort), library.Abort.Definition);
        Assert.True(Enum.GetValues<IntrinsicId>().Length <= 30);
    }

    // R3 decision 5: the MIR builder serves a built-in form (a slicing) by a library function it names by its lang item, which the
    // library registers as the intrinsic it calls; a direct line's fixed arm invokes Text.writer's body since R8-W2 b2-1. PLAN G597's
    // L2 (the owner's O-490 (a)): the one Form function, whose call the builder writes as its MIR form, is Storage.viewStart.
    [Fact]
    public void ServedFunctionsHaveTheirDeclarations()
    {
        var library = Library.Value.Library;
        Assert.Equal(
            [IntrinsicId.RawSlice],
            new[] { KimiDeclarationId.RawSlice }.Select(x => library.GetSymbol(x)!.Definition.Id));
        var forms = library.RegisteredSymbols.ToArray().Where(static x => x.Definition.Kind == DispositionKind.Form).ToArray();
        Assert.Equal([KimiDeclarationId.ViewStart], forms.Select(static x => x.LibraryDeclaration!.Value));
    }

    // SPEC 4.9.6, 12.4.3: each published row, as TyCtxt.Published gives it to the MIR: the requirement it runs and the parameter below
    // which, the parameter below which it destroys (-1: none, so a row without user code destroys nothing), whether in place,
    // `replace` only (SPEC 15.6.3, 15.7; design A3 4.1: a `clear` destroys its target's elements and ends no child Loan), and the
    // by-value Callable parameter it calls, `removeAll`'s `matching` only (PLAN G264 U0′: `truncate` destroys as `clear` does); the
    // Array mutation operations SPEC 4.9.6 gives None have that row (R6-check H4).
    [Fact]
    public void PublishedRowsAreFixed()
    {
        var c = Library.Value;
        var rows = KimiLibraryCatalog.Entries.ToArray().Where(static x => x.Row is not null)
            .Select(x => (x.Id, Row: c.Types.Published(c.Library.GetSymbol(x.Id)!)!.Value)).Select(static x => $"{x.Id} {x.Row.Runs?.Name} {x.Row.On} {x.Row.Destroys} {x.Row.InPlace} {x.Row.Calls}");
        Assert.Equal(
            [
                "Replace  -1 0 True -1", "Exchange  -1 -1 False -1", "Swap  -1 -1 False -1", "ArrayReserve  -1 -1 False -1", "ArrayAppend  -1 -1 False -1", "ArrayInsert  -1 -1 False -1", "ArrayPop  -1 -1 False -1", "ArrayRemove  -1 -1 False -1",
                "ArrayClear  -1 0 False -1", "ArrayShrinkToFit  -1 -1 False -1", "ArraySwap  -1 -1 False -1", "ArrayCapacityImplementation  -1 -1 False -1", "ArrayRepeatingImplementation  -1 -1 False -1",
                "TextToString format 0 -1 False -1", "TextTryFormat format 0 -1 False -1", "WriterWrite format 1 -1 False -1", "DictionaryReserve  -1 -1 False -1",
                "DictionaryTryInsert equals 1 -1 False -1", "DictionaryInsertOrReplace equals 1 1 False -1", "DictionaryRemove equals 1 -1 False -1",
                "DictionaryTryGet equals 1 -1 False -1", "DictionaryClear  -1 0 False -1", "DictionaryShrinkToFit  -1 -1 False -1",
                "DictionaryIndexUniq equals 1 -1 False -1", "StorageArrayCapacity  -1 -1 False -1", "DictionaryStorageCapacity  -1 -1 False -1",
                "DictionaryStorageFindEntry equals 1 -1 False -1", "ArrayAppendCopies  -1 -1 False -1",
                "ArrayTruncate  -1 0 False -1", "ArrayRemoveAll  -1 0 False 1", "ArrayInsertPosition  -1 -1 False -1", "ArrayAppendAll  -1 -1 False -1",
                "ArrayRemovePosition  -1 -1 False -1", "ArraySwapPositions  -1 -1 False -1", "ArraySwapRemove  -1 -1 False -1", "ArrayReverse  -1 -1 False -1",
            ],
            rows);
    }

    // SPEC 4.9.6, 15.6.4 (DECISIONS BU-40): a 4.9 operation's added and replacing inputs (KimiLibraryCatalog.Added) are exactly its inputs,
    // other than the receiver and not borrowed (a borrowed key is read, never stored), that its declared contract retains in the receiver's
    // storage, and a member without a receiver (construction) has none, so an Array or Dictionary member that adds an input cannot miss its
    // row and a row names no input it does not store.
    [Fact]
    public void AddedInputsAreTheInputsStoredInTheReceiver()
    {
        var c = Library.Value;
        var added = new List<string>();
        foreach (var id in new[] { KimiDeclarationId.Array, KimiDeclarationId.Dictionary })
        {
            var container = Assert.IsAssignableFrom<DeclarationContainerKoto>(c.Library.GetSymbol(id)!.Declaration);
            foreach (var function in container.Members.OfType<FunctionKoto>())
            {
                var symbol = function.SymbolOf(c)!;
                var writes = c.Types.Retains(symbol, null, null, []).Writes.ToArray();
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    var stored = symbol.ReceiverIndex >= 0 && i != symbol.ReceiverIndex && !Borrowed(c.Types.Signature(symbol, null, i, out _)!) &&
                        writes.Any(x => x.Output == symbol.ReceiverIndex && (x.Own.Kinds[i] | x.All.Kinds[i]) != RetentionKind.None);
                    Assert.True(stored == KimiLibraryCatalog.Added(symbol, i), $"{Line(function)}: input {i}");
                    if (stored)
                    {
                        added.Add($"{function.Name} {i}");
                    }
                }
            }
        }

        Assert.Equal(["insert 2", "appendAll 1", "appendCopies 1", "append 1", "insertAt 2", "tryInsert 1", "tryInsert 2", "insertOrReplace 1", "insertOrReplace 2"], added);

        static bool Borrowed(BoundType type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };
    }

    // SPEC 4.9.6, 4.8 (DECISIONS BU-42): CallRetention.ReferentStores takes a 4.6 to 4.8 view's referent as its own handle, sound only
    // while no 4.6 to 4.8 operation writes an element through a view. The 4.6 to 4.8 operations the fact covers (a library function with
    // a referent store, not a member of Array or Dictionary, nor of the fixed array's two groups) are exactly these, each lending or
    // advancing its receiver,
    // and none retains another input in its receiver's storage, so an operation that would store a value through a view, or a parameter
    // added to one of these, fails here first.
    [Fact]
    public void ReferentStoresWriteNoElementThroughAView()
    {
        var c = Library.Value;
        var covered = new List<string>();
        foreach (var function in KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<FunctionKoto>())
        {
            var symbol = function.SymbolOf(c)!;
            var retention = c.Types.Retains(symbol, null, null, []);
            if (retention.ReferentStores.IsEmpty || (function.Parent is { } parent && c.Library.MembersOf(parent) is WitnessMembers.FixedArray or WitnessMembers.FixedArrayEntries) ||
                function.Parent?.SymbolOf(c)?.LibraryDeclaration is KimiDeclarationId.Array or KimiDeclarationId.Dictionary)
            {
                continue;
            }

            var receiver = Assert.Single(retention.ReferentStores.ToArray());
            Assert.DoesNotContain(retention.Writes.ToArray(), x => x.Output == receiver &&
                x.Own.Kinds.ToArray().Zip(x.All.Kinds.ToArray()).Where((_, i) => i != receiver).Any(static y => (y.First | y.Second) != RetentionKind.None));
            covered.Add($"{Path(Container(function))}: {Line(function)}");
        }

        Assert.Equal(
            [
                "ResolvedRange: public func iterateUniq(self: uniq/Self during source) -> RangeIterator<isize>",
                "Slice: public func iterateUniq(self: uniq/Self during handle) -> SliceIterator<T> during source",
                "UniqSlice: public func sliceUniq(self: uniq/Self) -> UniqSlice<T> during self",
                "UniqSlice: public func sliceUniq<R>(self: uniq/Self, range: R) -> UniqSlice<T> during self",
                "UniqSlice: public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self",
                "UniqSlice: internal func indexPositionUniq<P>(self: uniq/Self, key: P) -> uniq/T during self",
            ],
            covered);
    }

    [Theory]
    [MemberData(nameof(CompilerFunctions))]
    public void CompilerFunctionShapesAreFixed(KimiDeclarationId id, string shape)
        => Assert.Equal(shape, Shape(Library.Value.Library.GetSymbol(id)!.Declaration));

    [Theory]
    [MemberData(nameof(UnnamedCompilerFunctions))]
    public void UnnamedCompilerFunctionShapesAreFixed(string path, string shape, string target)
        => Assert.Equal(shape, Shape(LibrarySource.Declaration(LibraryOf(target).Library, path)));

    // SPEC 4.9.3, 4.9.5, 15.7, 22.2 (R8-A1a, A1b, U1): the operations that became bodies keep the headers their compiler function rows held.
    [Theory]
    [InlineData(KimiDeclarationId.Replace, "public func replace<T>(target: uniq/T ! with => value: T) -> ()")]
    [InlineData(KimiDeclarationId.Exchange, "public func exchange<T>(target: uniq/T ! with => value: T) -> T")]
    [InlineData(KimiDeclarationId.Swap, "public func swap<T>(first: uniq/T, second: uniq/T) -> ()")]
    [InlineData(KimiDeclarationId.ArrayReserve, "public func reserve(self: uniq/Self, additional: isize)")]
    [InlineData(KimiDeclarationId.ArrayAppend, "public func append(self: uniq/Self, value: T)")]
    [InlineData(KimiDeclarationId.StorageGrowArray, "internal unsafe func growArray(handle: raw/ArrayRepr<u8>, stride: isize, minimum: isize)")]
    [InlineData(KimiDeclarationId.ArrayInsert, "internal func insertAt(self: uniq/Self, index: isize, value: T)")]
    [InlineData(KimiDeclarationId.ArrayPop, "public func pop(self: uniq/Self) -> Option<T>")]
    [InlineData(KimiDeclarationId.ArrayRemove, "internal func removeAt(self: uniq/Self, index: isize) -> T")]
    [InlineData(KimiDeclarationId.ArraySwap, "internal func swapAt(self: uniq/Self, first: isize, second: isize)")]
    [InlineData(KimiDeclarationId.ArrayClear, "public func clear(self: uniq/Self)")]
    [InlineData(KimiDeclarationId.ArrayShrinkToFit, "public func shrinkToFit(self: uniq/Self)")]
    [InlineData(KimiDeclarationId.StorageDestroyArray, "internal unsafe func destroyArray<E>(handle: raw/ArrayRepr<u8>)")]
    [InlineData(KimiDeclarationId.StorageDestroyDictionary, "internal unsafe func destroyDictionary<K, V>(handle: raw/DictionaryStorage.Handle)")]
    [InlineData(KimiDeclarationId.StorageReleaseDictionary, "internal unsafe func releaseDictionary(handle: raw/DictionaryStorage.Handle)")]
    [InlineData(KimiDeclarationId.DictionaryStorageInitialize, "unsafe func initialize(handle: raw/Handle)")]
    [InlineData(KimiDeclarationId.StorageArrayCapacity, "internal func arrayCapacity<E>(value: ref/Array<E> during a) -> isize")]
    [InlineData(KimiDeclarationId.DictionaryStorageCapacity, "internal func capacity<K, V>(value: ref/Dictionary<K, V> during a) -> isize")]
    [InlineData(KimiDeclarationId.TextHeap, "public func heap(capacity: isize) -> HeapBuffer")]
    [InlineData(KimiDeclarationId.TextWriter, "public func writer<W>(destination: uniq/W) -> Utf8Writer\nW is BufferWriter")] // R8-W2 b2-1.
    [InlineData(KimiDeclarationId.TextToString, "public func toString<T>(value: ref/T) -> string\nT is Utf8Format")]
    [InlineData(KimiDeclarationId.TextTryFormat, "public func tryFormat<T, length N>(\nvalue: ref/T, destination: uniq/[N of u8]\n) -> Result<Utf8Slice during destination, BufferFull>\nT is Utf8Format")]
    [InlineData(KimiDeclarationId.WriteLineUtf8, "public func writeLine(text: Text.Utf8Slice)")]
    [InlineData(KimiDeclarationId.WriteLine, "public func writeLine(text: ref/string) => Console.writeLine(Text.utf8(text))")]
    public void OperationBodiesKeepTheirHeaders(KimiDeclarationId id, string header)
        => Assert.Equal(header, HeaderLines(Library.Value.Library.GetSymbol(id)!.Declaration, header));

    // SPEC 22.9.2, 22.9.3, 22.9.4 (R8-W1a, W1c): the buffer and Text operations that became bodies keep the headers their compiler
    // function rows held, found by their container's path and name, since most of them have no catalog entry; so does Storage's
    // shrinkArray (R8-A1a), which has no lang item (R8-G1 G1-6).
    [Theory]
    [InlineData("Storage", "shrinkArray", "internal unsafe func shrinkArray(handle: raw/ArrayRepr<u8>, stride: isize)")]
    [InlineData("Text.FixedBuffer", "bytes", "public func bytes(self: ref/Self) -> Slice<u8>")]
    [InlineData("Text.FixedBuffer", "text", "public func text(self: ref/Self) -> Result<Text.Utf8Slice, Text.InvalidUtf8>")]
    [InlineData("Text.FixedBuffer", "validate", "public func validate(self: uniq/Self) -> Result<(), Text.InvalidUtf8>")]
    [InlineData("Text.FixedBuffer", "clear", "public func clear(self: uniq/Self)")]
    [InlineData("Text.FixedBuffer", "reserve", "public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>")]
    [InlineData("Text.FixedBuffer", "intoText", "public func intoText(self: owner/Self) -> Result<Text.Utf8Slice during self.source, Text.InvalidUtf8>")]
    [InlineData("Text.HeapBuffer", "bytes", "public func bytes(self: ref/Self) -> Slice<u8>")]
    [InlineData("Text.HeapBuffer", "text", "public func text(self: ref/Self) -> Result<Text.Utf8Slice, Text.InvalidUtf8>")]
    [InlineData("Text.HeapBuffer", "validate", "public func validate(self: uniq/Self) -> Result<(), Text.InvalidUtf8>")]
    [InlineData("Text.HeapBuffer", "clear", "public func clear(self: uniq/Self)")]
    [InlineData("Text.HeapBuffer", "reserve", "public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>")]
    [InlineData("Text.HeapBuffer", "intoString", "public func intoString(self: owner/Self) -> Result<string, Text.InvalidUtf8>")]
    [InlineData("WriteWindow", "push", "public func push(self: uniq/Self, byte: u8) -> Result<(), BufferFull>")]
    [InlineData("WriteWindow", "append", "public func append(self: uniq/Self, bytes: Slice<u8>) -> Result<(), BufferFull>")]
    [InlineData("WriteWindow", "limit", "public func limit(self: owner/Self, maximum: isize) -> Self{r}\norigin r.source == self.source")]
    [InlineData("WriteWindow", "commit", "public func commit(self: owner/Self) -> isize")]
    [InlineData("Utf8Writer", "write", "public func write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>\nT is Utf8Format")]
    [InlineData("Utf8Writer", "status", "public func status(self: ref/Self) -> Result<(), BufferFull>")]
    [InlineData("Text", "fixed", "public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer")]
    [InlineData("Text", "utf8", "public func utf8(text: ref/string) -> Utf8Slice")]
    [InlineData("Text", "validateUtf8", "public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice during bytes.source, InvalidUtf8>")]
    public void BufferOperationsKeepTheirHeaders(string container, string name, string header)
        => Assert.Equal(header, HeaderLines(Assert.Single(Record(container).Members.OfType<FunctionKoto>(), x => x.Name == name), header));

    // SPEC 22.1 and its formatting and arithmetic profiles: the compiler reaches these requirements by name and position.
    [Theory]
    [InlineData(KimiDeclarationId.Copy, "public contract Copy")]
    [InlineData(KimiDeclarationId.Owned, "public contract Owned")]
    [InlineData(KimiDeclarationId.Callable, "public contract Callable")]
    [InlineData(KimiDeclarationId.Sealed, "public contract Sealed")]
    [InlineData(KimiDeclarationId.ObjectPayload, "public contract ObjectPayload")]
    [InlineData(KimiDeclarationId.PrimitiveInteger, "public contract PrimitiveInteger")]
    [InlineData(KimiDeclarationId.Equatable, "public contract Equatable\nfunc equals(self: ref/Self, other: ref/Self) -> bool")]
    [InlineData(KimiDeclarationId.Comparable, "public contract Comparable: Equatable\nfunc compare(self: ref/Self, other: ref/Self) -> i32")]
    [InlineData(KimiDeclarationId.Addable, "public contract Addable<Rhs>\nassociate Output\nfunc added(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Subtractable, "public contract Subtractable<Rhs>\nassociate Output\nfunc subtracted(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Multipliable, "public contract Multipliable<Rhs>\nassociate Output\nfunc multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Dividable, "public contract Dividable<Rhs>\nassociate Output\nfunc divided(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.RemainderProvider, "public contract RemainderProvider<Rhs>\nassociate Output\nfunc remainder(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftAddable, "public contract LeftAddable<Lhs>\nassociate Output\nfunc addedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftSubtractable, "public contract LeftSubtractable<Lhs>\nassociate Output\nfunc subtractedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftMultipliable, "public contract LeftMultipliable<Lhs>\nassociate Output\nfunc multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftDividable, "public contract LeftDividable<Lhs>\nassociate Output\nfunc dividedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftRemainderProvider, "public contract LeftRemainderProvider<Lhs>\nassociate Output\nfunc remainderFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.Negatable, "public contract Negatable\nassociate Output\nfunc negated(self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.Indexable, "public contract Indexable<Key>\nassociate Element\nfunc index(self: ref/Self, key: ref/Key) -> ref/Element during self")]
    [InlineData(KimiDeclarationId.UniqIndexable, "public contract UniqIndexable<Key>: Indexable<Key>\nfunc indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self")]
    [InlineData(KimiDeclarationId.LendingIterator, "public contract LendingIterator\nassociate LentItem(step) for uniq/Self during step\nfunc next(self: uniq/Self during step) -> Option<Self.LentItem(step)>")]
    [InlineData(KimiDeclarationId.Iterator, "public contract Iterator: LendingIterator\nassociate Item\nassociate LendingIterator.LentItem(step) is Item")]
    [InlineData(KimiDeclarationId.Iterable, "public contract Iterable\nassociate IteratorType(source) is ::Kimi.LendingIterator for ref/Self during source\nfunc iterate(self: ref/Self during source) -> Self.IteratorType(source)")]
    [InlineData(KimiDeclarationId.UniqIterable, "public contract UniqIterable\nassociate IteratorType(source) is ::Kimi.LendingIterator for uniq/Self during source\nfunc iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)")]
    [InlineData(KimiDeclarationId.IntoIterable, "public contract IntoIterable\nassociate IteratorType is ::Kimi.LendingIterator\nfunc intoIterator(self: Self) -> Self.IteratorType")]
    [InlineData(KimiDeclarationId.Position, "public contract Position: Equatable, Utf8Format\nSelf is Copy\nSelf is Owned\neffect Utf8Format.format confined\nfunc tryResolve(self: Self, length: isize) -> Option<isize>")]
    [InlineData(KimiDeclarationId.PositionRange, "public contract PositionRange: Equatable, Utf8Format\nSelf is Copy\nSelf is Owned\neffect Utf8Format.format confined\nfunc tryResolve(self: Self, length: isize) -> Option<ResolvedRange>")]
    [InlineData(KimiDeclarationId.Utf8Format, "public contract Utf8Format\nfunc format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>")]
    [InlineData(KimiDeclarationId.BufferWriter, "public contract BufferWriter\nfunc reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>")]
    // SPEC 22.4.5: the requirements in declaration order, the order the startup check publishes and the runtime's adapters read.
    [InlineData(KimiDeclarationId.Platform, "public contract Platform\nSelf is Copy\nfunc start(boot: Boot) -> Self\nfunc mapPages(self: uniq/Self, bytes: isize) -> raw/u8\nfunc releasePages(self: uniq/Self, run: raw/u8, bytes: isize) -> i64\nfunc write(self: uniq/Self, stream: i32, text: Text.Utf8Slice) -> i64\nfunc clock(self: uniq/Self) -> i64\nfunc terminate(self: uniq/Self, status: i32) -> Never")]
    public void ContractShapesAreFixed(KimiDeclarationId id, string shape)
        => Assert.Equal(shape, Shape(Assert.IsType<ContractKoto>(Library.Value.Library.GetSymbol(id)!.Declaration)));

    // IMPL 21.6: PlatformRows' rows are Platform's requirements in declaration order, each row's receiver whether its requirement takes
    // `self`, and each adapter's record is named by its requirement.
    [Fact]
    public void PlatformRowsArePlatformsRequirements()
    {
        var shape = Shape(Assert.IsType<ContractKoto>(Library.Value.Library.GetSymbol(KimiDeclarationId.Platform)!.Declaration));
        var requirements = shape.Split('\n').Where(static x => x.StartsWith("func ", StringComparison.Ordinal))
            .Select(static x => (x[5..x.IndexOf('(', StringComparison.Ordinal)], x.Contains("(self:", StringComparison.Ordinal)));
        Assert.Equal(requirements, PlatformRows.Rows.Select(static x => (x.Name, x.Receiver)));
        Assert.All(PlatformRows.Rows, static x => Assert.Equal("__kimi_platform_member_" + x.Name, x.Adapter.Name));
    }

    // Code generation addresses these records' stored Fields by name, in this order, and runs their drop; a record without Fields stays
    // zero-sized.
    [Theory]
    [InlineData("Storage.RefRemainder", "internal struct RefRemainder<E> {source}\ninternal let storage: raw/E during source\ninternal var position: isize = 0\ninternal var count: isize\ninternal let loan: Loan<ref/E during source>")]
    [InlineData("Storage.UniqRemainder", "internal struct UniqRemainder<E> {source}\ninternal var storage: raw/E during source\ninternal var count: isize\ninternal let loan: Loan<uniq/E during source>")]
    [InlineData("ArrayOwningIterator", "public struct ArrayOwningIterator<T>\nvar array: Storage.ArrayRepr<T>\nvar position: isize = 0\nvar count: isize\ndrop")]
    [InlineData("Storage.DictionaryRefRemainder", "internal struct DictionaryRefRemainder<K, V> {source}\ninternal let storage: raw/u8 during source\ninternal var link: isize\ninternal var count: isize\ninternal let loan: Loan<ref/(K, V) during source>")]
    [InlineData("Storage.DictionaryUniqRemainder", "internal struct DictionaryUniqRemainder<K, V> {source}\ninternal let storage: raw/u8 during source\ninternal var link: isize\ninternal var count: isize\ninternal let loan: Loan<uniq/(K, V) during source>")]
    [InlineData("Storage.DictionaryOwnedRemainder", "internal struct DictionaryOwnedRemainder<K, V>\ninternal var handle: DictionaryStorage.Handle\ndrop")]
    [InlineData("DictionaryStorage.Handle", "internal struct Handle\npublic var buffer: raw/u8 during self\npublic var used: isize\npublic var capacity: isize\npublic var length: isize\npublic var head: isize\npublic var tail: isize\npublic var free: isize")]
    [InlineData("Storage.ArrayRepr", "internal struct ArrayRepr<T>\ninternal var data: raw/T during self\ninternal var length: isize\ninternal var capacity: isize")]
    [InlineData("Storage.SliceRepr", "internal struct SliceRepr<T>\ninternal var data: raw/T\ninternal var length: isize")]
    [InlineData("Storage.StringRepr", "internal struct StringRepr\ninternal var data: raw/u8\ninternal var length: isize\ninternal var releaseKind: u8")]
    [InlineData("Storage.InlineStorage", "internal struct InlineStorage<A>\ninternal var value: A")]
    [InlineData("Storage.FixedOwnedRemainder", "internal struct FixedOwnedRemainder<E, A>\ninternal var storage: InlineStorage<A>\ninternal var position: isize = 0\ninternal var count: isize\ndrop")]
    [InlineData("UniqSlice", "public struct UniqSlice<T> {source}\ninternal let storage: raw/T during source\ninternal let count: isize\ninternal let loan: Loan<uniq/T during source>")]
    [InlineData("ResolvedRange", "public struct ResolvedRange\npublic let start: isize\npublic let end: isize")]
    [InlineData("FromEnd", "public struct FromEnd<T>\npublic let offset: T")]
    [InlineData("Range", "public struct Range<S, E>\npublic let start: S\npublic let end: E")]
    [InlineData("ClosedRange", "public struct ClosedRange<S, E>\npublic let start: S\npublic let end: E")]
    [InlineData("Start", "public struct Start")]
    [InlineData("End", "public struct End")]
    [InlineData("Loan", "public struct Loan<T>")]
    [InlineData("Wrapping", "public struct Wrapping<T>")]
    [InlineData("BufferFull", "public struct BufferFull")]
    [InlineData("WriteWindow", "public struct WriteWindow {source}\nlet state: raw/u8 during source\nlet start: isize\npublic var written: isize\npublic var remaining: isize\nlet loan: Loan<uniq/u8 during source>")]
    [InlineData("Utf8Writer", "public struct Utf8Writer {target}\nlet destination: raw/u8 during target\nlet dispatch: raw/u8 during static\nlet kind: WriterDestination\nvar failed: bool\ninternal var hint: isize\nvar pending: raw/u8 during static\nvar pendingLength: isize\nlet loan: Loan<uniq/u8 during target>")]
    [InlineData("Text.FixedBuffer", "public struct FixedBuffer {source}\nlet data: raw/u8 during source\npublic let capacity: isize\npublic var length: isize\nvar validated: isize\nlet loan: Loan<uniq/u8 during source>")]
    [InlineData("Text.HeapBuffer", "public struct HeapBuffer\nvar data: raw/u8 during self\npublic var capacity: isize\npublic var length: isize\nvar validated: isize\ndrop")]
    [InlineData("Text.Utf8Slice", "public struct Utf8Slice {source}\nlet value: Slice<u8> during source")]
    [InlineData("Text.InvalidUtf8", "public struct InvalidUtf8")]
    // SPEC 6.3.6, 22.1: no Field initializer, so neither has a constructor; only the runtime creates a Boot (IMPL 21.6).
    [InlineData("Boot", "public struct Boot\npublic let directMapOffset: usize\npublic let regions: raw/raw/Boot.Region\npublic let regionCount: isize")]
    [InlineData("Boot.Region", "public struct Region\npublic let address: u64\npublic let length: u64\npublic let kind: u64")]
    public void RecordFieldsAreFixed(string path, string shape)
    {
        var c = Library.Value;
        var type = c.Binding.SelfType(Record(path).SymbolOf(c)!);
        var lines = new List<string> { Line(Record(path)) };
        for (var i = 0; i < AdtDef.Count(type); i++)
        {
            Assert.NotNull(AdtDef.FieldType(c.Types, type, i));
            lines.Add(Line(AdtDef.Field(type, i)));
        }

        if (AdtDef.Destructor(type) is not null)
        {
            lines.Add("drop");
        }

        Assert.Equal(shape, string.Join('\n', lines));
    }

    // SPEC 22.1: the compiler manages these representations, so their declarations store nothing.
    [Theory]
    [InlineData("Array")]
    [InlineData("Dictionary")]
    [InlineData("Slice")]
    [InlineData("Loan")]
    [InlineData("Wrapping")]
    public void ManagedRepresentationsStoreNothing(string path)
        => Assert.DoesNotContain(Record(path).Members, member => member is VariableKoto and not PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement });

    // SPEC 22.1: Case order is the tag (Some/Ok = 0, None/Err = 1) that lowering and the runtime construct and test; the
    // conditional Copy clause is each Type's Copy set.
    [Theory]
    [InlineData(KimiDeclarationId.Option, "public enum Option<T>\nSelf is Copy when T is Copy\nSome(T)\nNone")]
    [InlineData(KimiDeclarationId.Result, "public enum Result<T, E>\nSelf is Copy when T is Copy, E is Copy\nOk(T)\nErr(E)")]
    public void OptionAndResultCasesAreFixed(KimiDeclarationId id, string shape)
    {
        var symbol = Library.Value.Library.GetSymbol(id)!;
        var declaration = Assert.IsType<EnumKoto>(symbol.Declaration);
        Assert.Equal(shape, Shape(declaration));
        var ordinal = 0;
        foreach (var member in declaration.Members)
        {
            if (member is not SyntaxFormKoto { BoundSymbol.EnumCase: { } bound })
            {
                continue;
            }

            Assert.Same(symbol, bound.Owner);
            Assert.Equal(ordinal, bound.Ordinal);
            var payload = bound.Payload;
            var parameter = id == KimiDeclarationId.Option ? (ordinal == 0 ? 0 : -1) : ordinal;
            Assert.Equal(parameter < 0 ? 0 : 1, payload.Length);
            if (parameter >= 0)
            {
                Assert.Same(declaration.GenericParameterNodes[parameter].TypeOf(Library.Value), payload[0].TypeOf(Library.Value));
            }

            ordinal++;
        }

        Assert.Equal(2, ordinal);
    }

    // Layouts read at fixed offsets, each row by its reader: Utf8Writer's destination and dispatch by the kept runtime
    // (ErasedWriterRuntime.ll.in: __kimi_erased_reserve loads them), its hint at 16 by FunctionCodegenTest.Interpolation's Hint helper,
    // and its size and other offsets, which nothing reads at a fixed offset, are pinned because every adapter's generated size and
    // Field addresses follow them, so that a layout change is an explicit edit of this row; WriteWindow's by __kimi_erased_reserve
    // (written and remaining, in the Result's payload at byte 8); FixedBuffer's and HeapBuffer's equal Storage.BufferRepr's, as which
    // the library reads a window's state (Formatting.kimi, WriteWindow.commit, commitUtf8 and next).
    [Theory]
    [InlineData("Utf8Writer", 48, "destination", 0, "dispatch", 8, "hint", 16, "pending", 24, "pendingLength", 32, "kind", 40, "failed", 44)]
    [InlineData("Text.FixedBuffer", 32, "data", 0, "capacity", 8, "length", 16, "validated", 24)]
    [InlineData("Text.HeapBuffer", 32, "data", 0, "capacity", 8, "length", 16, "validated", 24)]
    [InlineData("WriteWindow", 32, "state", 0, "start", 8, "written", 16, "remaining", 24)]
    [InlineData("Storage.BufferRepr", 32, "data", 0, "capacity", 8, "length", 16, "validated", 24)]
    // IMPL 21.1.2, 21.6: Boot's by the runtime's boot template, which stores the loader's answers in its three Fields.
    [InlineData("Boot", 24, "directMapOffset", 0, "regions", 8, "regionCount", 16)]
    // IMPL 21.1.2, 21.6: Boot.Region's equals the loader's memory-map entry (three u64), which the loader writes and the program reads
    // through Boot.regions.
    [InlineData("Boot.Region", 24, "address", 0, "length", 8, "kind", 16)]
    public void RuntimeLayoutsAreFixed(string path, int size, params object[] offsets)
    {
        var (layout, type) = Layout(path);
        Assert.Equal(size, layout.Size);
        for (var i = 0; i < offsets.Length; i += 2)
        {
            Assert.Equal((int)offsets[i + 1], layout.FieldOffsets.Span[AdtDef.IndexOf(type, (string)offsets[i])]);
        }
    }

    // The kept runtime's routine is glue over the library's named layouts (plan Q5, B4's guard): __kimi_erased_reserve loads destination
    // and dispatch (R8-M18's lasting pair) and checks the window's written and remaining in the Result's payload at byte 8. Since R8-W2
    // b2-1 Text.writer's body stores the adapter (fail-before: __kimi_text_writer zeroed its size and stored destination, dispatch, kind).
    [Fact]
    public void RuntimeGlueMatchesTheNamedLayouts()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write("word")
            var bytes = [4 of 0@u8]
            match Text.tryFormat(Value.init(), bytes@uniq)@move
                .Ok(let view) => Console.writeLine(view)
                .Err(_) => $abort("full")
            Console.writeLine(Text.toString(Value.init()))
            """;
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        var (writer, writerType) = Layout("Utf8Writer");
        var (window, windowType) = Layout("WriteWindow");
        int At(TypeLayout layout, BoundType type, string field) => layout.FieldOffsets.Span[AdtDef.IndexOf(type, field)];
        Assert.Equal(0, At(writer, writerType, "destination")); // Loaded at the adapter's address.
        foreach (var glue in new[]
        {
            $"  %destination = load ptr, ptr %writer, align 8\n  %functionp = getelementptr i8, ptr %writer, i64 {At(writer, writerType, "dispatch")}\n",
            $"  %writtenp = getelementptr i8, ptr %ret, i64 {8 + At(window, windowType, "written")}\n" +
                $"  %remainingp = getelementptr i8, ptr %ret, i64 {8 + At(window, windowType, "remaining")}\n",
        })
        {
            Assert.Contains(glue, ir, StringComparison.Ordinal);
        }

        // R8-W1c: the conversions are bodies, so no glue allocates a buffer or an adapter record.
        Assert.DoesNotContain("%buffer = alloca", ir, StringComparison.Ordinal);
    }

    // R8-S1 C1: a group with a wrong signature fails here, not at a use, and a new group needs its rows. The signature is the first
    // line before an expression body's `=>` (STYLE: a short single-item body). The row groups are every group but the receiver-only
    // ones, and no row names a receiver-only group (PLAN G776-b).
    [Fact]
    public void EachWitnessGroupDeclaresItsContractsRequirements()
    {
        var library = Library.Value.Library;
        Assert.Equal(Enum.GetValues<WitnessMembers>().Except(ReceiverOnlyGroups), WitnessRequirements.Select(static x => x.Group).Distinct());
        var fixedArray = Library.Value.Types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [BoundType.I32], 2);
        foreach (var self in BoundType.Primitives.Values.Concat([fixedArray, BoundType.String, BoundType.Boolean, BoundType.Unit]))
        {
            Assert.All(Enum.GetValues<KimiDeclarationId>(), x => Assert.False(Binding.WitnessGroup(self, x) is { } row && ReceiverOnlyGroups.Contains(row.Members)));
        }

        foreach (var (group, contract, header) in WitnessRequirements)
        {
            var requirement = library.GetSymbol(contract)!.Contract!.OwnRequirement()!;
            var overloads = Declarations(library.Members(group)!, requirement.Name, true);
            Assert.Equal(header, Line(Assert.Single(overloads, static x => x is FunctionKoto { IsSpecialization: false })).Split(" => ", 2)[0]);
        }
    }

    // PositionSyntax functions construct `^x` and range syntax values (Binding.Ranges): one internal function per name.
    [Theory]
    [InlineData("fromEnd")]
    [InlineData("between")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("all")]
    [InlineData("through")]
    [InlineData("upTo")]
    public void PositionSyntaxFunctionsAreUnique(string name)
    {
        var function = Assert.IsType<FunctionKoto>(Assert.Single(Declarations(Library.Value.Library.PositionSyntax!, name, true)));
        Assert.Equal(ModifierKind.Internal, function.Modifier);
        Assert.False(function.IsSpecialization);
        Assert.True(HasBody(function));
    }

    // A compilation without a source module, bound for a target (null: none), whose library is complete.
    private static Compilation BoundLibrary(string? target)
    {
        var c = Compilation.CreateForTest();
        Assert.True(target is null || c.Prepare(target));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    }

    // The class's bound library of a target: "" the one without a target (windows-x64's selection), else the row's.
    private static Compilation LibraryOf(string target)
        => Libraries[target].Value;

    // Whether the Condition `windows` holds in the class's library of a target (SPEC 19.2.1): the one without a target is windows-x64's
    // selection.
    private static bool WindowsHolds(string target)
        => target.Length == 0 || TargetTriple.Parse(target).OsFamily == "windows";

    private static bool HasBody(Koto declaration)
        => declaration is FunctionKoto function && (function.Body is not null || function.ExpressionBody is not null);

    private static DeclarationContainerKoto Container(Koto declaration)
    {
        var parent = declaration.Parent;
        while (parent is not DeclarationContainerKoto)
        {
            parent = parent!.Parent;
        }

        return (DeclarationContainerKoto)parent;
    }

    private static string Path(DeclarationContainerKoto container)
        => container.IsRoot ? string.Empty : container.Parent is DeclarationContainerKoto { IsRoot: false } parent ? Path(parent) + "." + container.Name : container.Name;

    private static DeclarationContainerKoto Record(string path)
    {
        DeclarationContainerKoto container = Library.Value.Library.Kotonoha.RootKoto;
        foreach (var name in path.Split('.'))
        {
            container = Assert.Single(container.NestedContainers, x => x.Name == name);
        }

        return container;
    }

    // The same-named functions of a container, including those of its conditional conformance blocks, or its same-named
    // nested declarations, in source order (the order of catalog overload positions); an explicit specialization has no name
    // (SPEC 8.13.3), so it is none of them.
    private static List<Koto> Declarations(DeclarationContainerKoto container, string name, bool function)
    {
        var found = new List<Koto>();
        if (!function)
        {
            found.AddRange(container.NestedContainers.Where(x => x.Name == name));
            return found;
        }

        foreach (var member in container.Members)
        {
            Visit(member);
        }

        return found;

        void Visit(Koto node)
        {
            if (node is FunctionKoto { IsSpecialization: false } member && member.Name == name)
            {
                found.Add(member);
            }
            else if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conformance && conformance.Operands is [_, _, CodeBlockKoto block])
            {
                foreach (var item in block.Items)
                {
                    Visit(item);
                }
            }
        }
    }

    private static (TypeLayout Layout, BoundType Type) Layout(string path)
    {
        var type = Library.Value.Binding.SelfType(Record(path).SymbolOf(Library.Value)!);
        return (Library.Value.Layout.Of(type).Value!.Layout, type);
    }

    // The trimmed source line holding the node's start: a declaration header with its modifiers, or a Field.
    private static string Line(Koto node) => Lines(node, false)[0];

    // The header and its direct member lines, one level deeper, without comments or bodies.
    private static string Shape(Koto node) => string.Join('\n', Lines(node, true));

    // A body's pinned header (R8-W1b B2, W1c B1, W1c-2b): the declaration's first source lines, each trimmed, as many as the pin states:
    // its header, on one line or wrapped, then its clause lines (an origin relation, a constraint). The body's
    // statements follow the clauses at their depth, so the pin's own line count bounds what is read.
    private static string HeaderLines(Koto node, string header)
    {
        var text = node.CodeContext.SourceDocument!.SourceText;
        var lines = text.Split('\n').Skip(text.AsSpan(0, node.Span.Start).Count('\n')).Take(header.AsSpan().Count('\n') + 1);
        return string.Join('\n', lines.Select(static x => x.Trim()));
    }

    private static List<string> Lines(Koto node, bool members)
    {
        var text = node.CodeContext.SourceDocument!.SourceText;
        var lines = text.Split('\n');
        var index = text.AsSpan(0, node.Span.Start).Count('\n');
        var result = new List<string> { lines[index].Trim() };
        var indent = Indent(lines[index]);
        var child = -1;
        for (var i = index + 1; members && i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var depth = Indent(lines[i]);
            if (depth <= indent)
            {
                break;
            }

            child = child < 0 ? depth : child;
            if (depth == child)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static void WithinTheSyntaxNestingLimit(Koto root)
        => Assert.InRange(SyntaxNestingTest.Depth(root), 0, SyntaxNesting.Limit);
}
