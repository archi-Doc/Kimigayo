// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class StartupBindingTest
{
    // Row 19 (SPEC 22.4.5: a platform is a root-level struct of an Application): the root's Fwd conforms to Platform by forwarding each
    // member to the Library's Board (ForwardedLibrary), whose `write` writes a settable static.
    internal const string Forwarding = KernelSources.Hello + """
        struct Fwd
            Self is Copy
            Self is Platform
            var inner: Lib.Board

            init(inner: Lib.Board) => self.inner = inner

            public func start(boot: Boot) -> Self => Fwd.init(Lib.Board.start(boot))

            public func mapPages(self: uniq/Self, bytes: isize) -> raw/u8 => self.inner.mapPages(bytes)

            public func releasePages(self: uniq/Self, run: raw/u8, bytes: isize) -> i64 => self.inner.releasePages(run, bytes)

            public func write(self: uniq/Self, stream: i32, text: Text.Utf8Slice) -> i64 => self.inner.write(stream, text)

            public func clock(self: uniq/Self) -> i64 => self.inner.clock()

            public func terminate(self: uniq/Self, status: i32) -> Never => self.inner.terminate(status)

        """;

    internal static readonly string ForwardedLibrary = Derive(
        KernelSources.Board + KernelSources.BoardTerminate,
        ("struct Board", "public struct Board"),
        ("        let bytes = text.bytes()\n", "        Metrics.writes += 1\n        let bytes = text.bytes()\n")) + "public group Metrics\n    public var writes: i64 = 0\n";

    // The targets as data: the two hosted rows, the freestanding row and a triple without a row.
    private const string WindowsX64 = "x86_64-pc-windows-msvc";
    private const string LinuxX64 = "x86_64-unknown-linux-musl";
    private const string Freestanding = "x86_64-unknown-none";
    private const string NoRow = "x86_64-unknown-linux-gnu";
    private const string CompleteBoard = KernelSources.Board + KernelSources.BoardTerminate;

    // A startup form whose first item is a runtime `is` test (SPEC 14.10.1, in a Discard context, 12.1), then Hello.
    private const string TypeTestHello = "open struct Animal\nstruct Dog: Animal\ngroup Zoo\n    public func pet() -> objref/Animal => $abort(\"pet\")\nZoo.pet() is Dog\n" + KernelSources.Hello;

    // The Kernel's Board as a second platform, as a generic one, with an Origin parameter, through a Contract refining Platform, through
    // a refining and a direct clause, through a Contract whose parent is an unknown name, selected on `os == "none"` only, and nested in
    // a group.
    private static readonly string SecondBoard = CompleteBoard.Replace("Board", "Board2", StringComparison.Ordinal);
    private static readonly string GenericBoard = CompleteBoard.Replace("Board", "Board<T>", StringComparison.Ordinal);
    private static readonly string OriginBoard = CompleteBoard.Replace("struct Board", "struct Board {o}", StringComparison.Ordinal);
    private static readonly string RefiningBoard = "contract Mine: Platform\n" + CompleteBoard.Replace("Self is Platform", "Self is Mine", StringComparison.Ordinal);
    private static readonly string RedundantBoard = "contract Machine: Platform\n" + CompleteBoard.Replace("Self is Platform", "Self is Machine\n    Self is Platform", StringComparison.Ordinal);
    private static readonly string UnresolvedParentBoard = "contract Machine: Platfrom\n" + CompleteBoard.Replace("Self is Platform", "Self is Machine", StringComparison.Ordinal);
    private static readonly string FreestandingBoard = "#if os == \"none\"\n" + Indent(CompleteBoard);
    private static readonly string NestedBoard = "group Boards\n" + Indent(CompleteBoard);

    // SPEC 22.4.5 with 22 ¶1 and 23.1.2: the platform records of the startup check per target, as its startup issues (code and node)
    // and the published records (code and the first line of the located text). Several platforms, a generic platform and one with Origin
    // parameters are errors on every target; a platform on a hosted row, and an Application without one on the freestanding row, are
    // each a located Unsupported_Kd; a target without a row has neither. None reads the conformance's verdict, so each stands beside its
    // record.
    public static TheoryData<string, OutputKind, string[], string, string> PlatformRows => new()
    {
        // A second platform.
        { Freestanding, OutputKind.Application, [KernelSources.Hello, CompleteBoard, SecondBoard], "MultiplePlatforms_Kd struct Board2", "MultiplePlatforms_Kd struct" },
        {
            WindowsX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard, SecondBoard],
            "MultiplePlatforms_Kd struct Board2 | Unsupported_Kd Self is Platform | Unsupported_Kd Self is Platform",
            "Unsupported_Kd Self is Platform | MultiplePlatforms_Kd struct | Unsupported_Kd Self is Platform"
        },
        {
            LinuxX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard, SecondBoard],
            "MultiplePlatforms_Kd struct Board2 | Unsupported_Kd Self is Platform | Unsupported_Kd Self is Platform",
            "Unsupported_Kd Self is Platform | MultiplePlatforms_Kd struct | Unsupported_Kd Self is Platform"
        },
        { NoRow, OutputKind.Application, [KernelSources.Hello, CompleteBoard, SecondBoard], "MultiplePlatforms_Kd struct Board2", "MultiplePlatforms_Kd struct" },

        // No platform on the freestanding row: at the startup form, or at the root beside the missing startup body.
        { Freestanding, OutputKind.Application, [KernelSources.Hello], "Unsupported_Kd Console.writeLine(\"Hello\")", "Unsupported_Kd Console.writeLine(\"Hello\")" },
        { Freestanding, OutputKind.Application, ["group Util\n    public func helper() => ()\n"], "MissingStartupBody_Kd Root | Unsupported_Kd Root", "MissingStartupBody_Kd  | Unsupported_Kd " },
        { WindowsX64, OutputKind.Application, ["group Util\n    public func helper() => ()\n"], "MissingStartupBody_Kd Root", "MissingStartupBody_Kd " },

        // One platform, by Platform, by a Contract refining it (SPEC 8.4.3), by a refining and a direct clause (one conformance, SPEC
        // 8.6.1, at its first clause), or among the startup body's items: selected on the freestanding row, Unsupported on a hosted one;
        // a platform under `os == "none"` is no root item of a hosted program.
        { Freestanding, OutputKind.Application, [KernelSources.Hello, CompleteBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "Unsupported_Kd Self is Platform", "Unsupported_Kd Self is Platform" },
        { LinuxX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "Unsupported_Kd Self is Platform", "Unsupported_Kd Self is Platform" },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, RefiningBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, RefiningBoard], "Unsupported_Kd Self is Mine", "Unsupported_Kd Self is Mine" },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, RedundantBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, RedundantBoard], "Unsupported_Kd Self is Machine", "Unsupported_Kd Self is Machine" },
        { Freestanding, OutputKind.Application, [KernelSources.Hello + CompleteBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello + CompleteBoard], "Unsupported_Kd Self is Platform", "Unsupported_Kd Self is Platform" },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, FreestandingBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, FreestandingBoard], string.Empty, string.Empty },
        { LinuxX64, OutputKind.Application, [KernelSources.Hello, FreestandingBoard], string.Empty, string.Empty },

        // No platform: a struct nested in a group is no root-level struct, an ordinary struct on every row; a Contract whose parent is
        // an unknown name gives no path to Platform. The Contract's own verdict does not tell a failed parent from another failure, so the
        // no-platform record stays direct beside the parent's and the Contract's records and the constraint records at the struct and its
        // clauses (PLAN open issues).
        { Freestanding, OutputKind.Application, [KernelSources.Hello, NestedBoard], "Unsupported_Kd Console.writeLine(\"Hello\")", "Unsupported_Kd Console.writeLine(\"Hello\")" },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, NestedBoard], string.Empty, string.Empty },
        {
            Freestanding, OutputKind.Application, [KernelSources.Hello, UnresolvedParentBoard], "Unsupported_Kd Console.writeLine(\"Hello\")",
            "Unsupported_Kd Console.writeLine(\"Hello\") | InvalidConstraint_Kd contract | UnresolvedBinding_Kd Platfrom | InvalidConstraint_Kd struct | InvalidConstraint_Kd Self is Copy | InvalidConstraint_Kd Self is Machine"
        },
        {
            WindowsX64, OutputKind.Application, [KernelSources.Hello, UnresolvedParentBoard], string.Empty,
            "InvalidConstraint_Kd contract | UnresolvedBinding_Kd Platfrom | InvalidConstraint_Kd struct | InvalidConstraint_Kd Self is Copy | InvalidConstraint_Kd Self is Machine"
        },

        // A platform whose conformance failed (SPEC 8.6.3): a candidate still, and the hosted record beside the conformance's.
        { Freestanding, OutputKind.Application, [KernelSources.Hello, KernelSources.Board], string.Empty, "MissingContractImplementation_Kd Self is Platform" },
        {
            WindowsX64, OutputKind.Application, [KernelSources.Hello, KernelSources.Board], "Unsupported_Kd Self is Platform",
            "MissingContractImplementation_Kd Self is Platform | Unsupported_Kd Self is Platform"
        },
        {
            LinuxX64, OutputKind.Application, [KernelSources.Hello, KernelSources.Board], "Unsupported_Kd Self is Platform",
            "MissingContractImplementation_Kd Self is Platform | Unsupported_Kd Self is Platform"
        },

        // A generic platform, and one with an Origin parameter (a Phantom Origin, SPEC 15.3.2).
        { Freestanding, OutputKind.Application, [KernelSources.Hello, GenericBoard], "InvalidPlatform_Kd struct Board", "InvalidPlatform_Kd struct" },
        {
            WindowsX64, OutputKind.Application, [KernelSources.Hello, GenericBoard], "InvalidPlatform_Kd struct Board | Unsupported_Kd Self is Platform",
            "InvalidPlatform_Kd struct | Unsupported_Kd Self is Platform"
        },
        {
            LinuxX64, OutputKind.Application, [KernelSources.Hello, GenericBoard], "InvalidPlatform_Kd struct Board | Unsupported_Kd Self is Platform",
            "InvalidPlatform_Kd struct | Unsupported_Kd Self is Platform"
        },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, OriginBoard], "InvalidPlatform_Kd struct Board", "InvalidPlatform_Kd struct" },
        {
            WindowsX64, OutputKind.Application, [KernelSources.Hello, OriginBoard], "InvalidPlatform_Kd struct Board | Unsupported_Kd Self is Platform",
            "InvalidPlatform_Kd struct | Unsupported_Kd Self is Platform"
        },
        {
            LinuxX64, OutputKind.Application, [KernelSources.Hello, OriginBoard], "InvalidPlatform_Kd struct Board | Unsupported_Kd Self is Platform",
            "InvalidPlatform_Kd struct | Unsupported_Kd Self is Platform"
        },

        // A Library's conforming struct is an ordinary struct.
        { Freestanding, OutputKind.Library, [CompleteBoard], string.Empty, string.Empty },
        { WindowsX64, OutputKind.Library, [CompleteBoard], string.Empty, string.Empty },
    };

    // SPEC 22.4.5 (the Tatara plan's decision 45): a complete check on a row whose program supplies the platform publishes its one
    // platform and the witnesses of its conformance to Platform, one per requirement in Platform's declaration order (B9a's
    // ContractShapesAreFixed row), through a refining Contract and beside a redundant direct clause too (one path); nothing elsewhere: on
    // a hosted row (its Unsupported_Kd), for two candidates, for a failed conformance (the Board without `terminate`), on a target without
    // a row and for a Library. The obligation reads no startup record: each witness of every candidate whose conformance is verified owes
    // StaticFree, a Library's struct and a failed conformance none.
    public static TheoryData<string, OutputKind, string[], string, int> PublishedRows => new()
    {
        { Freestanding, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "Board start mapPages releasePages write clock terminate", 6 },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, RefiningBoard], "Board start mapPages releasePages write clock terminate", 6 },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, RedundantBoard], "Board start mapPages releasePages write clock terminate", 6 },
        { WindowsX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "-", 6 },
        { LinuxX64, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "-", 6 },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, CompleteBoard, SecondBoard], "-", 12 },
        { Freestanding, OutputKind.Application, [KernelSources.Hello, KernelSources.Board], "-", 0 },
        { NoRow, OutputKind.Application, [KernelSources.Hello, CompleteBoard], "-", 6 },
        { Freestanding, OutputKind.Library, [CompleteBoard], "-", 0 },
    };

    [Theory]
    [InlineData("()")]
    [InlineData("let value: i32")]
    [InlineData("var value: string")]
    [InlineData("let text = \"Hello world\"\nConsole.writeLine(text)")]
    [InlineData("Console.writeLine(\"Hello world\")")]
    [InlineData("public func main() => ()")]
    [InlineData("public func main() -> ()\n    Console.writeLine(\"Hello world\")")]
    [InlineData("#if true\nConsole.writeLine(\"selected\")")]
    public void SelectsOriginalRuntimeBodyOrMain(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var result = c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(result.IsComplete, Describe(c));
        Assert.Same(c.Kotonoha.SourceDocuments[0], result.Source);
        if (source.StartsWith("public", StringComparison.Ordinal))
        {
            Assert.Equal(StartupKind.Explicit, result.Kind);
            Assert.Same(GetMain(c).SymbolOf(), result.Symbol);
            Assert.Empty(c.Binding.StartupItems);
            Assert.Null(c.Binding.CheckStartup(OutputKind.Library).Symbol);
        }
        else
        {
            Assert.Equal(StartupKind.Implicit, result.Kind);
            Assert.Null(result.Symbol);
            Assert.Same(c.Kotonoha.GeneratedFunction!.Body!.Items[0], c.Binding.StartupItems[0]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("func helper() => ()")]
    [InlineData("func main() => ()")]
    [InlineData("group G\n    public func main() => ()")]
    [InlineData("struct S\n    public func main() => ()")]
    [InlineData("#if false\nConsole.writeLine(\"excluded\")")]
    public void DeclarationFragmentsBindWithoutBecomingApplications(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MissingStartupBody_Kd);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete, Describe(c));
        Assert.Equal(StartupKind.None, c.Binding.Startup.Kind);
        Assert.Empty(c.Binding.Issues);
    }

    // SPEC 6.1.1, 9.3, 22.4: only the root's `public func main` keeps an access word; every near miss is a root or local function,
    // which takes none: it is reported at the word and selects no startup function. The valid twins are the `public func main`
    // rows above and the fragments theory's `func main` and member rows.
    [Theory]
    [InlineData("public func Main() => ()", "public")]
    [InlineData("private func main() => ()", "private")]
    [InlineData("internal func main() => ()", "internal")]
    [InlineData("protected internal func main() => ()", "protected")]
    [InlineData("func outer()\n    public func main() => ()", "public")]
    public void OnlyTheRootsPublicMainKeepsItsAccessWord(string source, string word)
    {
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.MisplacedSyntax_Kd), nameof(DiagnosticCode.MissingStartupBody_Kd)], records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf(word, StringComparison.Ordinal), word.Length), records[0].Span);
    }

    [Theory]
    [InlineData("public func main(x: i32) => ()")]
    [InlineData("public func main(self: i32) => ()")]
    [InlineData("public func main<T>() => ()")]
    [InlineData("public func main(x: ref/i32 during a) => ()")]
    [InlineData("public func main() -> i32 => 1")]
    [InlineData("public unsafe func main() => ()")]
    public void ApplicationMainRestrictionsDoNotLeakIntoLibraryBinding(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.InvalidStartupMain_Kd);
        Assert.Null(c.Binding.Startup.Symbol);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete, Describe(c));
        Assert.Empty(c.Binding.StartupIssues);
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("let x: i32")]
    [InlineData("Console.writeLine(\"x\")")]
    public void RuntimeItemsAreRejectedInLibrariesAndMixedApplications(string runtime)
    {
        var c = Parse(WindowsX64, "public func main() => ()\n" + runtime);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MixedStartupBodies_Kd);
        Assert.Empty(c.Binding.StartupItems);
        Assert.False(c.Binding.CheckStartup(OutputKind.Library).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.LibraryRuntimeBody_Kd);
    }

    [Fact]
    public void EveryPublicMainIsValidatedBeforeAnySelection()
    {
        var c = Parse(WindowsX64, "public func main() => ()", "public func main(x: i32) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupMains_Kd);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.InvalidStartupMain_Kd);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete);
    }

    [Fact]
    public void SameSignaturePublicMainsAreSharedRootDuplicates()
    {
        var c = Parse(WindowsX64, "public func main() => ()", "public func main() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupMains_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOrderDoesNotChooseBetweenRuntimeBodies(bool reverse)
    {
        var sources = reverse ? new[] { "let x: i32", "()" } : new[] { "()", "let x: i32" };
        var c = Parse(WindowsX64, sources);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupSources_Kd);
        Assert.Null(c.Binding.Startup.Source);
    }

    [Fact]
    public void HelpersDoNotCountAsAnotherRuntimeDocument()
    {
        var c = Parse(WindowsX64, "func helper() => ()", "()\nlet x: i32", "struct S");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Same(c.Kotonoha.SourceDocuments[1], c.Binding.Startup.Source);
        Assert.Equal(2, c.Binding.StartupItems.Count);
    }

    [Fact]
    public void MainIsCallableAcrossSourcesAndKeepsItsDeclarationAlias()
    {
        var c = Parse(
            WindowsX64,
            "alias A\ngroup A\n    public func helper() => ()\npublic func main() => helper()",
            "alias B\ngroup B\n    public func helper() -> i32 => 1\nfunc callMain() => main()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete, Describe(c));
        var main = GetMain(c);
        var bodyCall = Assert.IsType<InvocationKoto>(main.ExpressionBody);
        Assert.Equal("A", Assert.IsType<GroupKoto>(bodyCall.CallOf()!.Target.Declaration.Parent).Name);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.Method.ToString() == "main");
        Assert.Same(main.SymbolOf(), call.CallOf()!.Target);
        Assert.Same(c.Kotonoha.SourceDocuments[0], main.CodeContext.SourceDocument);
    }

    [Fact]
    public void MainCannotCaptureRuntimeLocals()
    {
        var c = Parse(WindowsX64, "let text = \"x\"\npublic func main() => Console.writeLine(text)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCaptureBinding_Kd);
    }

    [Theory]
    [InlineData("Console.writeLine(\"x\")")]
    [InlineData("Kimi.Console.writeLine(\"x\")")]
    [InlineData("::Kimi.Console.writeLine(text: \"x\")")]
    [InlineData("let Core = 1\nlet writeLine = 2\n::Kimi.Console.writeLine(\"x\")")]
    public void WriteLineUsesTheCanonicalLanguageFunction(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).CallOf()!;
        Assert.Same(c.Library.WriteLine, call.Target);
        Assert.Equal(KimiDeclarationId.WriteLine, call.Target.LibraryDeclaration);
        Assert.Equal(DispositionKind.Body, call.Target.Definition.Kind); // A Kimigayo body (R8-W2 d).
        Assert.Null(call.Receiver);
        Assert.Equal("()", call.ReturnType.Name);
        Assert.Equal(new[] { 0 }, call.ArgumentToParameter.ToArray());
        var function = Assert.IsType<FunctionKoto>(call.Target.Declaration);
        var parameter = Assert.Single(function.Parameters).Type.TypeOf(c)!; // SPEC 22.1: writeLine(text: ref/string) borrows its argument.
        Assert.Equal((BoundTypeKind.Semantics, SemanticsKind.Ref), (parameter.Kind, parameter.Semantics));
        Assert.Equal("string", Assert.Single(parameter.Components).Name);
        Assert.Null(function.Body);
        Assert.NotNull(function.ExpressionBody); // SPEC 22.1: a library body, the view overload's call over Text.utf8 (R8-W2 d).
    }

    [Theory]
    [InlineData("Console.writeLine()")]
    [InlineData("Console.writeLine(1)")]
    [InlineData("Console.writeLine(\"a\", \"b\")")]
    [InlineData("Console.writeLine(value: \"x\")")]
    [InlineData("alias Kimi.Console\nlet writeLine = 1\nwriteLine(\"x\")")]
    [InlineData("func writeLine(x: i32) => ()\nwriteLine(\"x\")")]
    public void OrdinaryCallFailuresDoNotFallBackToCore(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Null(Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).CallOf());
    }

    [Fact]
    public void UserWriteLineHasNoCompilerImplementationIdentity()
    {
        var c = Parse(WindowsX64, "func writeLine(text: string) => ()\nwriteLine(\"x\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var target = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).CallOf()!.Target;
        Assert.NotSame(c.Library.WriteLine, target);
        Assert.Null(target.LibraryDeclaration);
        Assert.Equal(DispositionKind.Body, target.Definition.Kind);
    }

    [Fact]
    public void RebindingInvalidatesSelectionAndPreservesCoreAndCallIdentity()
    {
        var c = Parse(WindowsX64, "Console.writeLine(\"x\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).CallOf();
        var core = c.Library.WriteLine;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "public func main() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.Startup.IsComplete);
        Assert.Empty(c.Binding.StartupItems);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MixedStartupBodies_Kd);
        Assert.Same(core, c.Library.WriteLine);
        Assert.Same(call, Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).CallOf());
    }

    [Fact]
    public void StartupRequiresFinalBinding()
    {
        var c = Parse(WindowsX64, "()");
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckStartup(OutputKind.Application));
        c.Binding.Bind(BindingMode.Provisional);
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckStartup(OutputKind.Application));
    }

    [Fact]
    public void OutputKindRoundTripsAndDefaultsToApplication()
    {
        Assert.Equal(OutputKind.Application, new ProjectFile().OutputKind);
        var restored = TinyhandSerializer.Deserialize<ProjectFile>(TinyhandSerializer.Serialize(new ProjectFile { OutputKind = OutputKind.Library }));
        Assert.Equal(OutputKind.Library, restored!.OutputKind);
        var settings = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>("OutputKind = \"Library\""u8);
        Assert.Equal(OutputKind.Library, settings!.OutputKind);
        Assert.Throws<TinyhandException>(() => TinyhandSerializer.DeserializeFromUtf8<ProjectFile>("OutputKind = \"Unknown\""u8));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(1, false)]
    [InlineData(32, false)]
    [InlineData(512, false)]
    [InlineData(1, true)]
    [InlineData(32, true)]
    [InlineData(512, true)]
    public void WarmStartupAndWriteLineBindingAllocateNothing(int calls, bool explicitMain)
    {
        var source = new System.Text.StringBuilder(explicitMain ? "public func main()\n" : string.Empty);
        for (var i = 0; i < calls; i++)
        {
            source.Append(explicitMain ? "    " : string.Empty).Append("Console.writeLine(\"Hello world\")\n");
        }

        var c = Parse(WindowsX64, source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var allocated = AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
        });
        Assert.Equal(0, allocated);
    }

    // The platform's candidates are lists the startup check reuses: a warm Kernel's Bind and startup check allocate nothing where it is
    // selected and where its record is reported.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Freestanding)]
    [InlineData(WindowsX64)]
    public void WarmPlatformSelectionAllocatesNothing(string target)
    {
        var c = Parse(target, KernelSources.Hello, CompleteBoard);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(target == Freestanding, c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var allocated = AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
        });
        Assert.Equal(0, allocated);
    }

    [Theory]
    [MemberData(nameof(PlatformRows))]
    public void EachPlatformRecordReadsItsOwnInputs(string target, OutputKind kind, string[] sources, string issues, string records)
        => AssertStartupRecords(Parse(target, sources), kind, issues, records);

    [Theory]
    [MemberData(nameof(PublishedRows))]
    public void TheSelectedPlatformIsPublishedWithItsWitnesses(string target, OutputKind kind, string[] sources, string published, int owed)
        => AssertPublished(Parse(target, sources), kind, published, owed);

    // Row 19: an Application's root struct forwarding each member to a Library's struct conforming to Platform is its platform, published
    // with its own witnesses; the Library's struct is no candidate and owes nothing.
    [Fact]
    public void AForwardingRootStructIsThePlatform()
        => AssertPublished(
            ModuleBindingTest.Create(Forwarding, ForwardedLibrary, row: TargetProfile.Find(Freestanding)),
            OutputKind.Application,
            "Fwd start mapPages releasePages write clock terminate",
            6);

    // SPEC 23.1.2 (AGENTS "Diagnostics"): whether a freestanding Application has a platform rests on its whole root member set, so the
    // no-platform record is derived from a declaration the parser skipped and from a root struct's Self clause that did not bind: each
    // failure's own record is the one published, also where the startup form is a runtime `is` test (SPEC 14.10.1). Without a startup
    // form the missing startup body rests on the same skipped declaration, so both records at the root are derived and neither gives a
    // record of its own. A hosted row has no such record.
    [Theory]
    [InlineData(Freestanding, KernelSources.Hello, "extension Board\n    func f() => ()\n", "Unsupported_Kd Console.writeLine(\"Hello\")", "MisplacedSyntax_Kd extension")]
    [InlineData(Freestanding, "", "extension Board\n    func f() => ()\n", "MissingStartupBody_Kd Root | Unsupported_Kd Root", "MisplacedSyntax_Kd extension")]
    [InlineData(WindowsX64, KernelSources.Hello, "extension Board\n    func f() => ()\n", "", "MisplacedSyntax_Kd extension")]
    [InlineData(Freestanding, KernelSources.Hello, "struct Board\n    Self is Platfrom\n", "Unsupported_Kd Console.writeLine(\"Hello\")", "UnresolvedBinding_Kd Platfrom")]
    [InlineData(WindowsX64, KernelSources.Hello, "struct Board\n    Self is Platfrom\n", "", "UnresolvedBinding_Kd Platfrom")]
    [InlineData(Freestanding, TypeTestHello, "public 42\n", "Unsupported_Kd Zoo.pet() is Dog", "ExpectedSyntax_Kd 42")]
    [InlineData(WindowsX64, TypeTestHello, "public 42\n", "", "ExpectedSyntax_Kd 42")]
    public void TheNoPlatformRecordRestsOnAFailedMember(string target, string startup, string failed, string issues, string records)
    {
        var c = Parse(target, startup);
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, new SourceDocument("failed.kimi", failed));
        AssertStartupRecords(c, OutputKind.Application, issues, records);
    }

    [Theory]
    [InlineData("#LibraryImport(\"kernel32.dll\") public func main()")]
    [InlineData("specialize func main<i32>() => ()")]
    public void ImportedOrSpecializedMainCannotBecomeStartup(string source)
    {
        var c = Parse(WindowsX64, source);
        c.Bind();
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Null(c.Binding.Startup.Symbol);
        Assert.True(GetMain(c).IsSpecialization || c.Binding.StartupIssues.Any(x => x.Code == DiagnosticCode.InvalidStartupMain_Kd));
    }

    [Fact]
    public void LocalHelpersRemainSourceLocal()
    {
        var c = Parse(WindowsX64, "func helper() => ()", "public func main() => helper()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void CoreAliasAndUserCoreGroupUseOrdinaryLookup()
    {
        var c = Parse(WindowsX64, "alias Core\ngroup Core\n    public func writeLine(text: string) => ()\nCore.writeLine(\"user\")\n::Kimi.Console.writeLine(\"compiler\")\nwriteLine(\"alias\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal(DispositionKind.Body, calls[0].CallOf()!.Target.Definition.Kind);
        Assert.Same(c.Library.WriteLine, calls[1].CallOf()!.Target);
        Assert.Same(calls[0].CallOf()!.Target, calls[2].CallOf()!.Target);
    }

    [Fact]
    public void ClearingAndReparsingSourceDropsStaleMainSelection()
    {
        var c = Parse(WindowsX64, "public func main() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(StartupKind.Implicit, c.Binding.Startup.Kind);
    }

    [Fact]
    public void StartupSelectionDoesNotPermitTopLevelReturn()
    {
        var c = Parse(WindowsX64, "return");
        c.Bind();
        Assert.Equal([nameof(DiagnosticCode.InvalidJumpTarget_Kd)], CompilationTestHelper.BindingErrors(c).Select(static x => x.Code));
    }

    [Theory]
    [InlineData("Console.writeLine(\"Hello world\")")]
    [InlineData("::Kimi.Console.writeLine(text: \"Hello world\")")]
    [InlineData("public func main()\n    let text = \"Hello world\"\n    ::Kimi.Console.writeLine(text)")]
    [InlineData("#switch\n    #case true\n        Console.writeLine(\"selected\")\n    #case _\n        absent()")]
    public void WriteLineSuppliesTheExistingControlFlowChecks(string source)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        // The ordinary Slice iterator library body now retains its own Origin
        // obligation. Console calls must still introduce no source obligations.
        Assert.DoesNotContain(c.Binding.Obligations, x => !ReferenceEquals(x.Use.CodeContext.Kotonoha, c.Library.Kotonoha));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("unsafe func risky() => ()\nrisky()", true)]
    [InlineData("unsafe func risky() => ()\nunsafe\n    risky()", false)]
    public void CommittedCallsRetainUnsafePermissionChecks(string source, bool error)
    {
        var c = Parse(WindowsX64, source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        string[] codes = error ? [nameof(DiagnosticCode.UnsafeBlockRequired_Kd)] : [];
        Assert.Equal(codes, CompilationTestHelper.BindingErrors(c).Select(static x => x.Code));
    }

    [Theory]
    [InlineData("x.f(1)")]
    [InlineData("S.f(1, x)")]
    public void FlowUsesTheCommittedReceiverAndArgumentNodes(string expression)
    {
        var c = Parse(WindowsX64, $"struct S\n    public func f(value: i32, self: ref/Self) -> i32 => value\nfunc use(x: ref/S) -> i32 => {expression}");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        if (call.CallOf()!.Receiver is { } receiver)
        {
            Assert.NotEqual(HirCompletion.Unset, HirTables.CompletionOf(receiver));
        }

        foreach (var argument in call.ArgumentNodes)
        {
            Assert.NotEqual(HirCompletion.Unset, HirTables.CompletionOf(argument));
        }

        Assert.Equal(HirCompletion.Unset, HirTables.CompletionOf(call.Method));
    }

    [Theory]
    [InlineData(OutputKind.Application, "", false)]
    [InlineData(OutputKind.Application, "Console.writeLine(\"Hello world\")", true)]
    [InlineData(OutputKind.Application, "public func main() => ()", true)]
    [InlineData(OutputKind.Library, "", true)]
    [InlineData(OutputKind.Library, "public func main(x: i32) => ()", true)]
    [InlineData(OutputKind.Library, "let x: i32", false)]
    public async Task ProjectBuildChecksTheConfiguredStartupMode(OutputKind kind, string source, bool expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = kind;
        c.Project.AddSource("startup.kimi", source);
        Assert.Equal(expected, await c.Project.Check(TestContext.Current.CancellationToken));
    }

    // KernelSources' text derived by exact replacements (ruling 121 N7), each old text occurring once.
    internal static string Derive(string source, params (string Old, string New)[] edits)
    {
        foreach (var (old, replacement) in edits)
        {
            var at = source.IndexOf(old, StringComparison.Ordinal);
            Assert.True(at >= 0 && at == source.LastIndexOf(old, StringComparison.Ordinal), old);
            source = source.Replace(old, replacement, StringComparison.Ordinal);
        }

        return source;
    }

    private static FunctionKoto GetMain(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "main");

    private static Compilation Parse(string target, params string[] sources)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        for (var i = 0; i < sources.Length; i++)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, new SourceDocument($"source{i}.kimi", sources[i]));
        }

        Assert.Empty(TestDiagnostics.Of(c));
        return c;
    }

    // The final Bind, the startup check for the output kind and both partitions published: each startup issue as its code and node, each
    // published record as its code and the first line of its located text.
    private static void AssertStartupRecords(Compilation c, OutputKind kind, string issues, string records)
    {
        c.Bind();
        c.Binding.CheckStartup(kind);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        Assert.Equal(issues, string.Join(" | ", c.Binding.StartupIssues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Equal(records, string.Join(" | ", TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Text?.Split('\n')[0]}")));
    }

    // The final Bind and the startup check for the output kind: the published platform's name and its witnesses' names in order ("-" for
    // none), and the number of StaticFree obligations Binding lists.
    private static void AssertPublished(Compilation c, OutputKind kind, string published, int owed)
    {
        c.Bind();
        var startup = c.Binding.CheckStartup(kind);
        var rows = new List<EffectObligation>();
        c.Binding.EffectObligations(rows);
        var names = c.Binding.PlatformWitnesses.Select(static x => x.Name).Prepend(startup.Platform?.Name ?? "-");
        Assert.Equal((published, owed), (string.Join(' ', names), rows.Count(static x => x.Rule == EffectRule.StaticFree)));
    }

    // A source one block deeper, as the body of a Condition or a container.
    private static string Indent(string source)
        => string.Join('\n', source.Split('\n').Select(static x => x.Length == 0 ? x : "    " + x));

    private static string Describe(Compilation c)
        => string.Join(Environment.NewLine, c.Binding.Issues.Concat(c.Binding.StartupIssues).Select(x => $"{x.Code}: {x.Node}"));
}
