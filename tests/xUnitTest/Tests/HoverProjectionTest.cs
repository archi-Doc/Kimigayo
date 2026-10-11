// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class HoverProjectionTest
{
    [Fact]
    public void DeclarationsAndResolvedUsesShareDetachedContractsAndComments()
    {
        const string text = "/// The record.\nstruct Record\n    /// The count.\n    public var count: i32 = 0\n/// Read a count.\nfunc read(value: ref/Record) -> i32 => value.count\n";
        var snapshot = Project(text);
        var type = At(snapshot, text.IndexOf("Record", StringComparison.Ordinal));
        Assert.Equal("struct Record", Assert.Single(type.Declarations).Header);
        Assert.Equal(ConstraintProof.Refuted, type.Copy);
        var reference = At(snapshot, text.LastIndexOf("Record", StringComparison.Ordinal));
        Assert.Same(type.Declarations[0], Assert.Single(reference.Declarations));
        var property = At(snapshot, text.IndexOf("count:", StringComparison.Ordinal));
        Assert.Equal("public var count: i32", Assert.Single(property.Declarations).Header);
        Assert.Null(property.Copy);
        Assert.Same(property.Declarations[0], Assert.Single(At(snapshot, text.LastIndexOf("count", StringComparison.Ordinal)).Declarations));
        var comment = Assert.Single(property.Declarations[0].Documentation);
        Assert.Equal("The count.", DocumentationComment.Extract(comment.Source, comment.Span, comment.Indent).Text);
        var function = At(snapshot, text.IndexOf("read(", StringComparison.Ordinal));
        Assert.Equal("func read(value: ref/Record) -> i32", Assert.Single(function.Declarations).Header);
        Assert.NotNull(At(snapshot, text.IndexOf("value:", StringComparison.Ordinal)).Variable);
        Assert.NotNull(At(snapshot, text.LastIndexOf("value.", StringComparison.Ordinal)).Variable);
        Assert.Equal(-1, Document(snapshot).Find(text.IndexOf("///", StringComparison.Ordinal)));
    }

    [Fact]
    public void CopyQueriesKeepCompleteTypesAndUseConstraints()
    {
        const string text = "func unknown<T>(x: ref/T) => ()\nfunc copied<T>(x: T)\n    T is Copy\n    return\nfunc borrowed(x: uniq/i32) => ()\n";
        var snapshot = Project(text);
        Assert.Equal(ConstraintProof.Unknown, At(snapshot, text.IndexOf("T>", StringComparison.Ordinal)).Copy);
        Assert.Equal(ConstraintProof.Proven, At(snapshot, text.IndexOf("ref/", StringComparison.Ordinal)).Copy);
        Assert.StartsWith("ref/T during ", At(snapshot, text.IndexOf("ref/", StringComparison.Ordinal)).CopyType);
        Assert.Equal(ConstraintProof.Proven, At(snapshot, text.IndexOf("x: T", StringComparison.Ordinal) + 3).Copy);
        Assert.Equal(ConstraintProof.Refuted, At(snapshot, text.IndexOf("uniq/", StringComparison.Ordinal)).Copy);
        Assert.Equal(ConstraintProof.Proven, At(snapshot, text.IndexOf("i32", StringComparison.Ordinal)).Copy);
    }

    [Fact]
    public void CallsUseOneTokenAndPreserveOriginalStaticContract()
    {
        const string text = "/// Identity.\nfunc same<T>(x: T) -> T => x\nfunc main() -> i32 => same(42)\n";
        var snapshot = Project(text);
        var callStart = text.LastIndexOf("same", StringComparison.Ordinal);
        var name = At(snapshot, callStart);
        Assert.Equal("func same<T>(x: T) -> T", Assert.Single(name.Declarations).Header);
        Assert.Contains("i32", name.Use);
        Assert.Same(name, At(snapshot, callStart + 4));
        Assert.Equal(-1, Document(snapshot).Find(callStart + 5));
        Assert.Equal(-1, Document(snapshot).Find(callStart + 7));
    }

    [Fact]
    public void HiddenClosureContextsKeepLengthSubstitutionsInAgreement()
    {
        const string text = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n    return action(value)\nfunc inspect<T>(value: T) => ()\nfunc main()\n    _ = identity([1, 2])\n    _ = identity([3])\n    inspect(7)\n";

        HoverInfo ProjectContext(int index)
        {
            var compilation = Create(text);
            Assert.True(compilation.Bind().IsComplete);
            var nodes = KotoTree.Walk(compilation.Kotonoha.RootKoto).ToArray();
            var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
            var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "identity").Select(static x => x.CallOf()!).ToArray();
            var closed = compilation.Binding.InstantiateStorageType(closure, calls[index])!;
            Assert.Empty(closed.Components); // The varying length is only in the hidden instantiation context.
            var inspect = Assert.Single(nodes.OfType<InvocationKoto>(), static x => x.CallOf()?.Target.Name == "inspect");
            var selected = inspect.CallOf()!;
            selected.Set(selected.Target, selected.ReturnType, null, [0], [closed]);
            return At(HoverBuilder.Create(compilation), text.LastIndexOf("inspect(", StringComparison.Ordinal));
        }

        var first = ProjectContext(0);
        var same = ProjectContext(0);
        var different = ProjectContext(1);
        Assert.Equal(first.Use, different.Use);
        Assert.True(new HoverAgreement().Equal(new(default, first), new(default, same)));
        Assert.False(new HoverAgreement().Equal(new(default, first), new(default, different)));
    }

    [Fact]
    public void OwnProjectDocumentsAreIndexedBeforeBeingOpenedAndSplitCommentsAreStable()
    {
        var compilation = Create("/// First.\nstruct Split\n");
        compilation.Kotonoha.AddSource(new("other.kimi", "/// Second.\nstruct Split\n"));
        Assert.True(compilation.Bind().IsComplete);
        var snapshot = HoverBuilder.Create(compilation);
        Assert.Equal(2, snapshot.Documents.Count);
        var declaration = Assert.Single(At(snapshot, "/// First.\nstruct ".Length).Declarations);
        Assert.Equal(2, declaration.Origins.Length);
        Assert.Equal(2, declaration.Documentation.Length);
        Assert.Equal("First.", Extract(declaration.Documentation[0]));
        Assert.Equal("Second.", Extract(declaration.Documentation[1]));
    }

    [Fact]
    public void DetachedSnapshotsDoNotKeepCompilationsAlive()
    {
        var (snapshot, weak) = Detached();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));
        Assert.NotEmpty(Document(snapshot).Entries.ToArray());
        GC.KeepAlive(snapshot);
    }

    [Fact]
    public void AssociatedUsesKeepContractDocumentationAndSpecificationsHaveTheirOwnDescription()
    {
        const string text = "contract Source\n    /// The abstract item.\n    associate Item\nstruct Concrete\n    Self is Source\n    /// The concrete item.\n    associate Source.Item is i32\nfunc use(x: Concrete.Item) => ()\n";
        var snapshot = Project(text);
        var use = At(snapshot, text.LastIndexOf("Item", StringComparison.Ordinal));
        Assert.Equal("associate Item", Assert.Single(use.Declarations).Header);
        Assert.Equal("The abstract item.", Extract(Assert.Single(use.Declarations[0].Documentation)));
        Assert.Equal("i32", use.CopyType);
        Assert.Equal(ConstraintProof.Proven, use.Copy);
        var specification = At(snapshot, text.IndexOf("Source.Item", StringComparison.Ordinal) + "Source.".Length);
        Assert.Equal("associate Source.Item is i32", Assert.Single(specification.Declarations).Header);
        Assert.Equal("The concrete item.", Extract(Assert.Single(specification.Declarations[0].Documentation)));
    }

    [Fact]
    public void SyntaxErrorDefersAllFragmentsWithoutErasingTheEstablishedDeclaration()
    {
        const string text = "/// First.\nstruct Split\n";
        var compilation = Create(text);
        compilation.Kotonoha.AddSource(new("other.kimi", "/// Second.\nstruct Split\nfunc broken(\n"));
        compilation.Bind();
        var snapshot = HoverBuilder.Create(compilation);
        var declaration = Assert.Single(At(snapshot, text.IndexOf("Split", StringComparison.Ordinal)).Declarations);
        Assert.Equal("Documentation deferred: syntax errors", declaration.DocumentationNotice);
        Assert.Equal("First.", Extract(Assert.Single(declaration.Documentation)));
    }

    [Fact]
    public void TypeParameterItemsKeepTheParentsClassificationInputs()
    {
        const string text = "/// * T: Item type.\n/// * value: Input.\nfunc same<T>(value: T) -> T => value\n";
        var snapshot = Project(text);
        var parent = Assert.Single(At(snapshot, text.IndexOf("same", StringComparison.Ordinal)).Declarations);
        var parameter = Assert.Single(At(snapshot, text.IndexOf("<T>", StringComparison.Ordinal) + 1).Declarations);
        Assert.Equal("T", parameter.Parameter);
        Assert.Same(parent.Documentation, parameter.Documentation);
        Assert.Equal(new[] { "T", "value" }, Assert.Single(parameter.Documentation).Parameters.Select(static p => p.Name));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void IndexedLookupAllocatesNothingAndHonorsHalfOpenRanges()
    {
        var entries = new HoverEntry[10000];
        var info = new HoverInfo([]);
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new(new(i * 3, 2), info);
        }

        var document = new HoverDocument(new("main.kimi", string.Empty), entries);
        var sum = 0;
        for (var i = 0; i < 32; i++)
        {
            sum += document.Find(i * 3);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < entries.Length; i++)
        {
            sum += document.Find(i * 3);
            sum += document.Find((i * 3) + 1);
            sum += document.Find((i * 3) + 2);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(99980496, sum);
        Assert.Equal(-1, document.Find(-1));
        Assert.Equal(-1, document.Find(30000));
    }

    [Theory]
    [InlineData("/// The type.\nstruct Empty\nfunc make() -> Empty => Empty.init()\n")]
    [InlineData("open struct Base\n    protected init() => ()\n/// The type.\nstruct Empty: Base\nfunc make() -> Empty => Empty.init()\n")]
    public void GeneratedConstructorDoesNotInventADeclarationLocationOrComment(string text)
    {
        var snapshot = Project(text);
        var call = At(snapshot, text.IndexOf("init()", StringComparison.Ordinal) is var at && text.StartsWith("open", StringComparison.Ordinal) ? text.LastIndexOf("init", StringComparison.Ordinal) : at);
        var declaration = Assert.Single(call.Declarations);
        Assert.Contains("init(", declaration.Header);
        Assert.Empty(declaration.Origins);
        Assert.Empty(declaration.Documentation);
        if (text.Contains(": Base", StringComparison.Ordinal))
        {
            // The synthesized base call is located at the base clause, yet the clause still shows its Type.
            Assert.Contains("struct Base", Assert.Single(At(snapshot, text.IndexOf(": Base", StringComparison.Ordinal) + 2).Declarations).Header);
        }
    }

    // SPEC 22.1: hover presents the public repeating constructor, never its internal implementation.
    [Fact]
    public void RepeatingConstructionHoverPresentsThePublicConstructor()
    {
        const string text = "func make() -> Array<i64> => Array<i64>.init(repeating: 7, count: 3)\n";
        var declaration = Assert.Single(At(Project(text), text.IndexOf("init", StringComparison.Ordinal)).Declarations);
        Assert.Contains("init(! repeating: T, count: isize)", declaration.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("initRepeating", declaration.Header + string.Concat(declaration.Documentation.Select(static x => x.ToString())), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidConstraintsNeverPublishCopyYes()
    {
        const string text = "func broken<T>(x: T)\n    T is Copy\n    T is not Copy\n    return\n";
        var compilation = Create(text);
        Assert.False(compilation.Bind().IsComplete);
        var snapshot = HoverBuilder.Create(compilation);
        Assert.Equal(ConstraintProof.Error, At(snapshot, text.IndexOf("x: T", StringComparison.Ordinal) + 3).Copy);
    }

    [Fact]
    public void RequirementEffectsRetainEveryDeclaringContractAndPremise()
    {
        const string text = "contract Reader\n    func read(self: ref/Self) -> i32\ncontract Left: Reader\n    effect Reader.read confined\ncontract Right: Reader\n    effect Reader.read confined\nfunc readBoth<T>(x: ref/T) -> i32\n    T is Left\n    T is Right\n    return x.read()\n";
        var snapshot = Project(text);
        var info = At(snapshot, text.LastIndexOf("read()", StringComparison.Ordinal));
        Assert.Contains("Requirement: Reader.read", info.Effects);
        Assert.Contains("Available bound: confined", info.Effects);
        Assert.Contains("Declared by: Left", info.Effects);
        Assert.Contains("Declared by: Right", info.Effects);
        Assert.Contains("Premise: T is Left effect confined", info.Effects);
        Assert.Contains("Premise: T is Right effect confined", info.Effects);
        Assert.DoesNotContain("Available bound: preserves results", info.Effects);
    }

    [Fact]
    public void PropertyOperationsAndImplicitResultsAreExplicit()
    {
        const string text = "struct Cell\n    public var count = 0\nfunc value() => 42\n";
        var snapshot = Project(text);
        var property = Assert.Single(At(snapshot, text.IndexOf("count", StringComparison.Ordinal)).Declarations);
        Assert.Equal("Inferred type: i32\nget: public; standard place access\nset: public; standard place access", property.Details?.Replace("\r\n", "\n", StringComparison.Ordinal));
        var function = Assert.Single(At(snapshot, text.IndexOf("value", StringComparison.Ordinal)).Declarations);
        Assert.Equal("Implicit result: ()", function.Details);
    }

    [Fact]
    public void ParameterSupplementRetainsItsDeclarationConstraints()
    {
        const string text = "func copied<T>(x: T) -> T\n    T is Copy\n    return x\n";
        var parameter = Assert.Single(At(Project(text), text.IndexOf("<T>", StringComparison.Ordinal) + 1).Declarations);
        Assert.Contains("T is Copy", parameter.Details);
    }

    [Fact]
    public void TypeDisplayPreservesGroupingAndBoundsDepthAndRepeatedExpansion()
    {
        var integer = new BoundType("i32", BoundTypeKind.Primitive);
        var tuple = new BoundType(string.Empty, BoundTypeKind.Tuple, components: [integer]);
        Assert.Equal("(i32,)", Binding.HoverTypeName(tuple));
        var function = new BoundType(string.Empty, BoundTypeKind.Function, components: [BoundType.Unit, integer]);
        var reference = new BoundType(string.Empty, BoundTypeKind.Semantics, semantics: SemanticsKind.Ref, components: [function]);
        Assert.Equal("ref/(() -> i32)", Binding.HoverTypeName(reference));
        var deep = integer;
        for (var i = 0; i < 128; i++)
        {
            deep = new(string.Empty, BoundTypeKind.Tuple, components: [deep]);
        }

        Assert.Throws<HoverLimitException>(() => Binding.HoverTypeName(deep));
        var repeated = integer;
        for (var i = 0; i < 24; i++)
        {
            repeated = new(string.Empty, BoundTypeKind.Tuple, components: [repeated, repeated]);
        }

        Assert.Throws<HoverLimitException>(() => Binding.HoverTypeName(repeated));
    }

    private static string Extract(HoverDocumentation documentation)
        => DocumentationComment.Extract(documentation.Source, documentation.Span, documentation.Indent).Text;

    private static HoverDocument Document(HoverSnapshot snapshot) => snapshot.Documents[SourceIdentity.FromPath("main.kimi")];

    private static HoverInfo At(HoverSnapshot snapshot, int offset)
    {
        var document = Document(snapshot);
        var index = document.Find(offset);
        Assert.True(index >= 0, $"Missing target at {offset}: {document.Source.SourceText.Substring(offset, Math.Min(20, document.Source.SourceText.Length - offset))}");
        return document.Entries[index].Info;
    }

    private static Compilation Create(string text)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.CollectHover = true;
        compilation.CollectDocumentation = true;
        compilation.Kotonoha.AddSource(new("main.kimi", text));
        Assert.Empty(TestDiagnostics.Of(compilation));
        return compilation;
    }

    private static HoverSnapshot Project(string text)
    {
        var compilation = Create(text);
        Assert.True(compilation.Bind().IsComplete, string.Join('\n', compilation.Binding.Issues));
        return HoverBuilder.Create(compilation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HoverSnapshot Snapshot, WeakReference<Compilation> Compilation) Detached()
    {
        var compilation = Create("/// A value.\nstruct Value\nfunc use(value: ref/Value) => ()\n");
        Assert.True(compilation.Bind().IsComplete);
        return (HoverBuilder.Create(compilation), new(compilation));
    }
}
