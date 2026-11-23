// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class ModuleBindingTest
{
    private const string HolderLibrary = "public open struct Base\n    public init() => ()\n    protected internal contract Holder\n        associate Item\n    protected struct Token\n";
    private const string MakerLibrary = "public open struct Base\n    public init() => ()\n    protected internal contract Maker\n        func tag() -> i32\n    protected func tag() -> i32 => 1\n";
    private const string HolderMine = "struct Mine: Lib.Base\n    public init(): base() => ()\n    Self is Lib.Base.Holder\n    associate Lib.Base.Holder.Item is ";
    private const string MakerMine = "struct Mine: Lib.Base\n    public init(): base() => ()\n    Self is Lib.Base.Maker\n";
    private const string HolderOuter = "public open struct Outer: Lib.Base\n    public init(): base() => ()\n";
    private const string HolderNested = " struct Mine\n        Self is Lib.Base.Holder\n        associate Lib.Base.Holder.Item is ";
    private const string ReaderLibrary = "public open struct Base\n    public init() => ()\n    protected internal contract Reader\n        property size: i32 has get\n    protected var size: i32 = 1\n";
    private const string ReaderMine = "struct Mine: Lib.Base\n    public init(): base() => ()\n    Self is Lib.Base.Reader\n";
    private const string ChildBase = "public open struct Base2\n    public init() => ()\n    protected struct Token2\n";
    private const string ChildHolder = ChildBase + "    protected internal contract Holder2\n        associate Item\n";
    private const string DerivedBase = "public open struct Base: Child.Base2\n    public init(): base() => ()\n";
    private const string Holder2Mine = "struct Mine: Lib.Base\n    public init(): base() => ()\n    Self is Child.Base2.Holder2\n    associate Child.Base2.Holder2.Item is ";

    [Theory]
    [InlineData("public func main() => Lib.Api.value()", true)]
    [InlineData("alias Lib.Api\npublic func main() => value()", true)]
    [InlineData("alias A => Lib.Api\npublic func main() => A.value()", true)]
    [InlineData("alias L => ::Lib\npublic func main() => L.Api.value()", true)]
    [InlineData("alias A => Lib.Api\npublic func main() => Lib.A.value()", false)]
    [InlineData("public func main() => ::Lib.Api.value()", true)]
    [InlineData("public func main() => Api.value()", false)]
    [InlineData("public func main() => value()", false)]
    [InlineData("public group Lib\npublic func main() => ()", false)]
    public void DirectNamesAreModuleLocal(string root, bool expected)
    {
        var compilation = Create(root, "public group Api\n    public func value() => ()");
        Assert.Equal(expected, compilation.Bind().IsComplete);
        if (expected)
        {
            Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(compilation.Analyze().IsVerified);
            Assert.True(compilation.Generate(TextWriter.Null), MinimalEmissionTest.Describe(compilation));
        }
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    [InlineData("private", false)]
    public void ImportedMembersRetainAccess(string access, bool expected)
    {
        var compilation = Create("public func main() => Lib.Api.value()", $"public group Api\n    {access} func value() => ()");
        Assert.Equal(expected, compilation.Bind().IsComplete);
    }

    [Theory]
    [InlineData("func unused() => missing()", false, true)]
    [InlineData("func unused()\n    let text = \"moved\"\n    _ = text@move\n    ::Kimi.Console.writeLine(text)", true, false)]
    [InlineData("func unused() => ()", true, true)]
    public void UnusedDependencyBodiesAreChecked(string library, bool bound, bool owned)
    {
        var compilation = Create("public func main() => ()", library);
        Assert.Equal(bound, compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        if (bound)
        {
            Assert.Equal(owned, compilation.Analyze().IsVerified);
        }
    }

    [Fact]
    public void DependencyRuntimeCannotBecomeApplicationStartup()
    {
        var compilation = Create("public func main() => ()", "::Kimi.Console.writeLine(\"illegal\")");
        Assert.True(compilation.Bind().IsComplete);
        Assert.False(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(compilation.Binding.StartupIssues, x => x.Code == DiagnosticCode.LibraryRuntimeBody_Kd);
    }

    [Fact]
    public void DependencyTestsAreNotProductDeclarations()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()\n    #Test\n    public func test() => TestOnly.missing()");
        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "func bad() => Lib.Api.test()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void AliasesDoNotLeakBetweenDocumentsOrModules()
    {
        var compilation = Create("alias Lib.Api\npublic func main() => value()", "public group Api\n    public func value() => ()");
        compilation.Kotonoha.AddSource(new SourceDocument("other.kimi", "func other() => value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void DependenciesResolveTheirOwnDirectNames()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => Child.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "func bad() => Child.Api.value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void RebindingRetainsModuleSymbolsAndInvalidatesAllBodies()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
        Assert.False(compilation.Analysis.IsVerified);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Analyze().IsVerified);
        compilation.SourceModules[1].AddSource(new SourceDocument("bad.kimi", "func broken() => missing()"));
        Assert.False(compilation.Bind().IsComplete);
        compilation.Analyze();
        Assert.False(compilation.Accepts); // Since the MIR decides acceptance (R6-check H5), Binding's record rejects the program.
        Assert.Equal([DiagnosticCode.UnresolvedBinding_Kd], compilation.Binding.Issues.Select(static x => x.Code));
    }

    [Fact]
    public void CompileTimeSettingsBelongToEachModule()
    {
        var compilation = Create("#if feature\npublic func main() => Lib.Api.value()", "#if feature\nfunc broken() => missing()\n#if not feature\npublic group Api\n    public func value() => ()", configure: (root, library) =>
        {
            root.CompileTimeSettings.Add("feature", new() { Bool = true });
            library.CompileTimeSettings.Add("feature", new() { Bool = false });
        });
        Assert.True(compilation.Bind().IsComplete, string.Join("; ", compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + " | " + string.Join("; ", compilation.SourceModules.SelectMany(x => TestDiagnostics.Of(x)).Select(x => x.ToString())));
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("Lib.Api", true)]
    [InlineData("Api", false)]
    [InlineData("Lib.Missing", false)]
    public void DefaultAliasesResolveDirectContainerPaths(string alias, bool expected)
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = [alias]);
        Assert.Equal(expected, compilation.Bind().IsComplete);
    }

    // SPEC 18.2.3, F.5, IMPL 20.2 (F's ruling N1-9 (4)): a configured path that does not read whole by the Type production is reported by
    // its parse, as a source alias's target is: the parser's record in the configuration document, and no row for Binding, which neither
    // evaluates it nor records anything resting on it; the use, which no opened target serves, is its own miss (SPEC 23.1.2).
    [Theory]
    [InlineData("Lib.Api.", "MissingSyntax_Kd", 8)]
    [InlineData("Lib Api", "ExpectedSyntax_Kd", 4)]
    public void AConfiguredPathThatDoesNotParseWholeIsItsParsesRecord(string alias, string code, int at)
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = [alias]);
        Assert.False(compilation.Bind().IsComplete);
        Assert.DoesNotContain(compilation.DefaultAliases(compilation.Kotonoha), x => x.Path == alias);
        var syntax = Assert.Single(TestDiagnostics.Of(compilation), x => x.Path?.EndsWith($"#Alias[{alias}]", StringComparison.Ordinal) == true);
        Assert.Equal((code, at), (syntax.Code, syntax.Span.Start));
        var issue = Assert.Single(compilation.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnresolvedBinding_Kd, issue.Code);
        Assert.Equal("value", issue.Node.ToString());
    }

    [Fact]
    public void ADefaultAliasOpensNoFormalParameter()
    {
        // SPEC 18.2.1, 18.2.3 (PLAN G574): a default alias opens its target's direct members, never its formal parameters. A generic
        // target is never fully bound (F's rulings N1-2 C1, N1-9 (1)): one record at the configured path's `Box`, and the use of `T`,
        // resting on it, records nothing (N1-2 C2; it was one UnresolvedBinding_Kd at `T`).
        var compilation = Create("public struct Box<T>\n    public var a: i32 = 0\nfunc f(x: T) => ()", "public group Api", configure: (root, _) => root.Alias = ["Box"]);
        Assert.False(compilation.Bind().IsComplete);
        var issue = Assert.Single(compilation.Binding.Issues);
        Assert.Equal(DiagnosticCode.InvalidTypeFormation_Kd, issue.Code);
        AssertConfiguredAt(issue.Node, "Box", 0, "Box");
    }

    // SPEC 18.2.3, 18.2.1, 23.1.2 (F's rulings N1-2, N1-7 (2), (5), N1-9 (1), N1-13 B1; design d rows): a configured default alias
    // resolves as a source alias's target does, by the path walk from its module's root: an inherited member is found (d05v), a path
    // through the library's name too (C4); a generic Container is never fully bound (d01u, d06i, d07i; d02i's walk fails there, at its
    // first component, before `T`) and a path naming nothing is reported without a use (d08i), a missing qualifier at that qualifier
    // (m06i, m08i): one record where the walk fails, in the configured path's own node, which a use resting on it adds nothing to.
    [Theory]
    [InlineData("d01u", "Box", "func g() -> i32 => make()", "InvalidTypeFormation_Kd", "Box")]
    [InlineData("d02i", "Box.T", "func g() => ()", "InvalidTypeFormation_Kd", "Box")]
    [InlineData("d05v", "Derived.Node", "func g() -> i32 => make()", null, null)]
    [InlineData("d06i", "A.B", "func g() -> i32 => helper()", "InvalidTypeFormation_Kd", "A")]
    [InlineData("d07i", "A.B", "func g() => ()", "InvalidTypeFormation_Kd", "A")]
    [InlineData("d08i", "Nope", "func g() => ()", "UnresolvedBinding_Kd", "Nope")]
    [InlineData("c4v", "Kimi.Console", "func g() => writeLine(\"ok\")", null, null)]
    [InlineData("m06i", "Nope.Inner", "func g() -> i32 => make()", "UnresolvedBinding_Kd", "Nope")]
    [InlineData("m08i", "Kimi.Nope.Inner", "func g() => ()", "UnresolvedBinding_Kd", "Kimi.Nope")]
    public void ADefaultAliasResolvesByThePathWalk(string row, string alias, string use, string? code, string? text)
    {
        // Each row's own declarations, as its probe's.
        var declarations = alias switch
        {
            "Derived.Node" => "public open struct Base\n    public init() => ()\n    public struct Node\n        public init() => ()\n        public func make() -> i32 => 1\n" +
                "public struct Derived : Base\n    public init() : base() => ()\n",
            "A.B" => "public struct A<T>\n    public group B\n        public func helper() -> i32 => 1\n" +
                "public struct A<T, U>\n    public group B\n        public func helper() -> i32 => 1\n",
            _ => "public struct Box<T>\n    public init() => ()\n    public func make() -> i32 => 1\n",
        };
        var compilation = Create(declarations + use, "public group Api", configure: (root, _) => root.Alias = [alias]);
        Assert.True((code is null) == compilation.Bind().IsComplete, row + ": " + string.Join("; ", compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        if (code is not null)
        {
            var issue = Assert.Single(compilation.Binding.Issues);
            Assert.Equal(code, issue.Code.ToString());
            AssertConfiguredAt(issue.Node, alias, 0, text!);
        }
    }

    // SPEC 18.2.3, 18.2.2, 18.2.1 (F's rulings N1-7 (5), N1-13 B2): a configured default alias is the opening alias `alias Path` its
    // configuration declares, evaluated as that source alias is, its argument bindings included: each row's configured form and its
    // source twin agree (e01v, e03v the 18.2.2 example accepted; e06i's i64 result rejected as an i32).
    [Theory]
    [InlineData("e01v", "Box<i32>", "public struct Box<T>\n    public init() => ()\n    public func id(value: T) -> T => value@move\nfunc g() -> i32 => id(1)", true)]
    [InlineData("e03v", "Family<i32>.Helpers", "public struct Family<T>\n    public group Helpers\n        public struct Token\n            public init() => ()\ngroup Use\n    func make() -> Token => Token.init()", true)]
    [InlineData("e06i", "Box<i64>", "public struct Box<T>\n    public init() => ()\n    public func id(value: T) -> T => value@move\nfunc g() -> i32 => id(1)", false)]
    public void AConfiguredAliasIsItsSourceTwin(string row, string alias, string source, bool valid)
    {
        foreach (var configured in new[] { true, false })
        {
            var compilation = configured
                ? Create(source, "public group Api", configure: (root, _) => root.Alias = [alias])
                : Create("alias " + alias + "\n" + source, "public group Api");
            Assert.True(valid == compilation.Bind().IsComplete, $"{row} ({(configured ? "configured" : "source")}): " + string.Join("; ", compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
            Assert.DoesNotContain(compilation.Binding.Issues, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        }
    }

    // SPEC 23.1.2 (F's ruling N1-9 (2)): a failed configured path fails no declaration of its module: a base clause beside it records
    // nothing (the module root is not failed).
    [Fact]
    public void AFailedConfiguredPathFailsNoBaseClause()
    {
        var compilation = Create("public open struct Base\n    public init() => ()\npublic struct Derived : Base\n    public init() : base() => ()\nfunc g() => ()", "public group Api", configure: (root, _) => root.Alias = ["Nope"]);
        Assert.False(compilation.Bind().IsComplete);
        var issue = Assert.Single(compilation.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnresolvedBinding_Kd, issue.Code);
        AssertConfiguredAt(issue.Node, "Nope", 0, "Nope");
    }

    // SPEC 18.2.1, 9.6, 9.7, 23.1.2 (F's ruling N1-9 (3)): an alias target resolves whole with its Type arguments and the walk fails at
    // its first failing component: `Box.T` at `Box`, a generic Container used without its arguments (a09i); `Box<i32>.T` at `T`, a formal
    // parameter, which is no member a later component finds (18.2.1: formal parameters are not opened), recorded at the path, as every
    // Type use's missing last component is (FailingStep). A group reached through a failed qualifier rests on the qualifier's record
    // (a06s). A missing qualifier is the failing component (m01i, m02i, m07i; a03i: no alias resolves a target, 18.2.1): the record and
    // the node the alias rests on are the one walk's (F's ruling N1-13 B1). The alias rests on that record, and a use resting on the
    // alias records nothing (F's ruling N1-2 C2: m04i).
    [Theory]
    [InlineData("a09i", "alias Box.T\npublic struct Box<T>\n    public init() => ()\n    public func make() -> i32 => 1\nfunc g() => ()", "InvalidTypeFormation_Kd", 6, "Box")]
    [InlineData("a09t", "alias Box<i32>.T\npublic struct Box<T>\n    public init() => ()\n    public func make() -> i32 => 1\nfunc g() => ()", "UnresolvedBinding_Kd", 6, "Box<i32>.T")]
    [InlineData("a06s", "alias A.B\npublic struct A<T>\n    public group B\n        public func helper() -> i32 => 1\npublic struct A<T, U>\n    public group B\n        public func helper() -> i32 => 1\nfunc g() -> i32 => helper()", "InvalidTypeFormation_Kd", 6, "A")]
    [InlineData("m01i", "alias Missing.Inner\nfunc g() => ()", "UnresolvedBinding_Kd", 6, "Missing")]
    [InlineData("m02i", "alias O => Missing.Inner\nfunc g() => ()", "UnresolvedBinding_Kd", 11, "Missing")]
    [InlineData("m04i", "alias Missing.Inner\npublic struct Box\n    public func make() -> i32 => 1\nfunc g() -> i32 => make()", "UnresolvedBinding_Kd", 6, "Missing")]
    [InlineData("m07i", "alias Kimi.Nope.Inner\nfunc g() => ()", "UnresolvedBinding_Kd", 6, "Kimi.Nope")]
    [InlineData("a03i", "alias K => Kimi\nalias Output => K.Console", "UnresolvedBinding_Kd", 32, "K")]
    public void AnAliasTargetsWalkFailsAtItsFirstFailingComponent(string row, string source, string code, int start, string text)
    {
        var compilation = Create(source, "public group Api");
        Assert.False(compilation.Bind().IsComplete, row);
        var issue = Assert.Single(compilation.Binding.Issues);
        Assert.Equal(code, issue.Code.ToString());
        Assert.Equal((start, text.Length, text), (issue.Node.Span.Start, issue.Node.Span.Length, issue.Node.ToString()));
    }

    // SPEC 18.2.3, IMPL 20.2 (F's rulings N1-7 (5), N1-13 C2): the library's own configured `Kimi` alias is an alias of the library's
    // pass, evaluated there before its table freezes, so a path no use reads is judged in its own module too.
    [Fact]
    public void TheLibrarysConfiguredAliasIsEvaluatedInItsPass()
    {
        var compilation = Create("func g() => ()", "public group Api");
        Assert.True(compilation.Bind().IsComplete);
        var row = Assert.Single(compilation.DefaultAliases(compilation.Library.Kotonoha));
        var alias = Assert.IsType<AliasKoto>(row.Target.Parent);
        Assert.Equal((BindingState.Resolved, "Kimi"), (HirTables.State(alias), HirTables.Symbol(alias)?.Name));
    }

    [Fact]
    public void LibraryDefaultsDoNotLeakToTheCaller()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => helper()\nprivate group Implementation\n    public func helper() => ()", configure: (_, library) => library.Alias = ["Implementation"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "func bad() => helper()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void PreparedDefaultAliasesAreFrozen()
    {
        var aliases = new[] { "Lib.Api" };
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = aliases);
        aliases[0] = "Missing";
        Assert.True(compilation.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasesCombineOverloadsWithoutChangingSymbolIdentity(bool reverse)
    {
        var aliases = reverse ? "alias Lib.Second\nalias Lib.First" : "alias Lib.First\nalias Lib.Second";
        var compilation = Create(aliases + "\npublic func main()\n    choose(1@i32)\n    choose(true)", "public group First\n    public func choose(value: i32) => ()\npublic group Second\n    public func choose(value: bool) => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
    }

    [Fact]
    public void ExplicitAliasesPrecedeDefaultsAndCannotUseDefaultsToResolvePaths()
    {
        var compilation = Create("alias Local\npublic group Local\n    public func value() -> i32 => 1\npublic func main()\n    let x: i32 = value()", "public group Api\n    public func value() -> bool => true", configure: (root, _) => root.Alias = ["Lib.Api"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "alias Api\nfunc bad() => ()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void RootQualificationDoesNotUseAnAlias()
    {
        var compilation = Create("alias Lib\npublic func main() => ::Api.value()", "public group Api\n    public func value() => ()");
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void ModuleReparseRebuildsLookupAndOwnership()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        foreach (var module in compilation.SourceModules)
        {
            module.OnDeserialized(compilation);
        }

        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Analyze().IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InaccessibleOverloadsDoNotHideAccessibleCandidates(bool defaults)
    {
        var compilation = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public func main() => choose(1@i32)", "public group Api\n    private func choose(value: bool) => ()\n    public func choose(value: i32) => ()", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.True(compilation.Bind().IsComplete);
    }

    [Fact]
    public void AliasTargetAccessIsCheckedAtItsDeclaration()
    {
        var compilation = Create("alias Outer.Hidden\npublic group Outer\n    private group Hidden\n        public func value() => ()\n    public func use() => value()\npublic func main() => Outer.use()", "func unused() => ()");
        Assert.False(compilation.Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmModuleBindingAndOwnershipAllocateNothing()
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = ["Lib.Api"]);
        void Analyze()
        {
            if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Module analysis failed.");
            }
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
    }

    // F's ruling N1-4 (2): a configured path's node lives for the Compilation and its rows are reset each pass, so an edit beside it (a
    // document added, the module renumbered) leaves its lookup current: each later pass resolves through it again, and judges it again.
    [Fact]
    public void AConfiguredPathStaysCurrentAcrossAnEdit()
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = ["Lib.Api"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("more.kimi", "func more() => value()"));
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "func bad() => missing()"));
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.UnresolvedBinding_Kd, Assert.Single(compilation.Binding.Issues).Code);
    }

    [Theory]
    [InlineData("func f(x: Lib.Api.Box, y: Lib.Api.Box<i32>) => ()", false, true)]
    [InlineData("func f(x: ::Lib.Api.Box, y: ::Lib.Api.Box<i32>) => ()", false, true)]
    [InlineData("alias Lib.Api\nfunc f(x: Box, y: Box<i32>) => ()", false, true)]
    [InlineData("func f(x: Box, y: Box<i32>) => ()", true, true)]
    [InlineData("struct Box<T, U>\nfunc f(x: Box<i32>) => ()", true, false)]
    [InlineData("alias Local\ngroup Local\n    public struct Box<T, U>\nfunc f(x: Box<i32>) => ()", true, false)]
    public void ModuleTypeAritiesUseTheCommittedLookupStage(string source, bool defaults, bool expected)
    {
        var c = Create(source, "public group Api\n    public struct Box\n    public struct Box<T>", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.Equal(expected, c.Bind().IsComplete);
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("public", false, "func expose(value: Source.(C).Element) => ()")]
    [InlineData("internal", true, "func expose(value: Source.(C).Element) => ()")]
    [InlineData("public", false, "let value: Source.(C).Element = 1")]
    [InlineData("internal", true, "let value: Source.(C).Element = 1")]
    [InlineData("public", false, "enum Result\n        Item(Source.(C).Element)")]
    [InlineData("internal", true, "enum Result\n        Item(Source.(C).Element)")]
    [InlineData("public", false, "struct Result<T>\n        T is Source.(C).Element")]
    [InlineData("internal", true, "struct Result<T>\n        T is Source.(C).Element")]
    public void UnusedDependencyApisRetainProjectionAccess(string access, bool valid, string declaration)
    {
        var c = Create("public func main() => ()", "public group Api\n    internal contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32\n    " + access + " " + declaration);
        Assert.True(c.Bind().IsComplete == valid, string.Join(", ", c.Binding.Issues.Select(x => x.Code)));
        Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.FailureOf() == BindingFailure.Access);
        }

        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false, "Source.Element")]
    [InlineData(false, "Source.(C).Element")]
    [InlineData(true, "Source.Element")]
    [InlineData(true, "Source.(C).Element")]
    public void ImportedProjectionUsesTheOriginalDomains(bool defaults, string type)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public group Export\n    public func identity(value: " + type + ") -> i32 => value", "public group Api\n    public contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => x.Code)));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerAliasesCannotWidenLibraryApiDomains(bool defaults)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public func main() => ()", "public group Api\n    contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32\n    public enum Result\n        Item(Source.(C).Element)", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.FailureOf() == BindingFailure.Access);
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedGenericCallArgumentsRequireValidCompleteTypes(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func take<T>() => ()\n    func call() => take<Box<" + argument + ">>()", "public group Api\n    public struct Box<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).CallOf() is not null);
        }
    }

    [Theory]
    [InlineData(false, "public", true)]
    [InlineData(true, "public", true)]
    [InlineData(false, "internal", false)]
    [InlineData(true, "internal", false)]
    public void ImportedCallTypesObserveLateLibraryApiValidation(bool defaults, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n    public struct Source\n        Self is Hidden\n    public enum E<T>\n        T is Hidden\n        A\n    public func take(value: E<Source>) -> E<Source> => value@move";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func call(value: E<Source>) => take(value@move)", library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).CallOf() is not null);
            if (!valid)
            {
                Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.FailureOf() == BindingFailure.Access);
            }
        }
    }

    [Theory]
    [InlineData(false, "public", true)]
    [InlineData(true, "public", true)]
    [InlineData(false, "internal", false)]
    [InlineData(true, "internal", false)]
    public void ImportedPropertyAndPatternTypesObserveLateLibraryApiValidation(bool defaults, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n    public struct Source\n        Self is Hidden\n    public enum E<T>\n        T is Hidden\n        A";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct Storage\n    var value: E<Source>\ngroup Consumer\n    func inspect(value: E<Source>) => match value\n        _ => ()", library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var storage = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Storage");
            Assert.Equal(valid, storage.Members.OfType<PropertyKoto>().Single().SymbolOf()!.Property!.IsVerified);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            Assert.True(c.Binding.TryGetMatch(Assert.IsType<MatchKoto>(function.ExpressionBody), out var plan));
            Assert.Equal(valid ? MatchCoverageState.Exhaustive : MatchCoverageState.Invalid, plan!.Coverage.State);
        }
    }

    [Theory]
    [InlineData(false, "public", false)]
    [InlineData(true, "public", false)]
    [InlineData(false, "protected", false)]
    [InlineData(true, "protected", false)]
    [InlineData(false, "protected internal", false)]
    [InlineData(true, "protected internal", false)]
    [InlineData(false, "internal", true)]
    [InlineData(true, "internal", true)]
    [InlineData(false, "private protected", true)]
    [InlineData(true, "private protected", true)]
    [InlineData(false, "private", true)]
    [InlineData(true, "private", true)]
    public void ImportedInheritedNamesUseTheDerivedModulesAccess(bool defaults, string access, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct S: Base\n    public func f() => ()", "public group Api\n    public open struct Base\n        " + access + " func f(self: ref/Self) => ()", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, structure.StateOf());
            if (!valid)
            {
                Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, structure) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
            }
        }
    }

    // SPEC 9.4.2, 9.4.6: Access(Mine) ∩ Access(Contract) over two Kotonoha (KA, KL). An intersection that holds a Kotonoha or Body atom
    // of each is empty; Derived(Base) is of neither (PLAN G610). The Contract is a protected internal member of Lib's open Base
    // (KL ∪ Body(Base) ∪ Derived(Base)); the binding or the implementation is a protected member (Body(Base) ∪ Derived(Base)).
    // A third Kotonoha (KC, Child; App names it too): the member or the Contract belongs to Child's open Base2, which Lib's Base derives from.
    [Theory]
    [InlineData(HolderMine + "Lib.Base.Token\n", HolderLibrary, null, null)] // KA∩KL, KA∩Body(Base) are empty; Derived(Base) is in U
    [InlineData("public " + HolderMine + "Lib.Base.Token\n", HolderLibrary, null, DiagnosticCode.InaccessibleBinding_Kd)] // a public Mine: KL alone is not empty
    [InlineData(HolderMine + "Own\n    private struct Own\n", HolderLibrary, null, DiagnosticCode.InaccessibleBinding_Kd)] // KA∩Derived(Base) is not empty, U is Body(Mine)
    [InlineData(MakerMine, MakerLibrary, null, null)] // the inherited protected tag implements the requirement
    [InlineData("public " + MakerMine, MakerLibrary, null, DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData(ReaderMine, ReaderLibrary, null, null)] // the inherited protected Field implements the Property requirement
    [InlineData("public " + ReaderMine, ReaderLibrary, null, DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData(HolderOuter + "    protected internal" + HolderNested + "Lib.Base.Token\n", HolderLibrary, null, null)] // each chain's Derived atom is in U
    [InlineData(HolderOuter + "    private struct Own\n    protected" + HolderNested + "Own\n", HolderLibrary, null, DiagnosticCode.InaccessibleBinding_Kd)] // Derived(Outer)∩KL is not empty
    [InlineData(HolderOuter + "    protected struct Own2\n    protected internal" + HolderNested + "Own2\n", HolderLibrary, null, DiagnosticCode.InaccessibleBinding_Kd)] // KA∩Derived(Base) is not empty
    [InlineData("", HolderLibrary + "struct Mine: Base\n    public init(): base() => ()\n    Self is Base.Holder\n    associate Base.Holder.Item is Base.Token\n", null, DiagnosticCode.InaccessibleBinding_Kd)] // one Kotonoha: K∩K is not empty
    [InlineData(HolderMine + "Child.Base2.Token2\n", DerivedBase + "    protected internal contract Holder\n        associate Item\n", ChildBase, null)] // U is Body(Base2) ∪ Derived(Base2): Derived(Base) is in it
    [InlineData("public open struct Outer: Child.Base2\n    public init(): base() => ()\n    protected internal struct Mine: Lib.Base\n        public init(): base() => ()\n        Self is Lib.Base.Holder\n        associate Lib.Base.Holder.Item is Child.Base2.Token2\n", HolderLibrary, ChildBase, DiagnosticCode.InaccessibleBinding_Kd)] // Base does not derive from Base2: KA∩Derived(Base) is not empty
    [InlineData(Holder2Mine + "Child.Base2.Token2\n", DerivedBase, ChildHolder, null)] // KA∩KC, KA∩Body(Base2) are empty; Derived(Base2) is in U
    [InlineData(Holder2Mine + "Lib.Base.TokL\n", DerivedBase + "    protected struct TokL\n", ChildHolder, DiagnosticCode.InaccessibleBinding_Kd)] // Derived(Base2) is not in Derived(Base)
    public void ConformanceDomainsOverTwoKotonohaReadEmptyIntersections(string root, string library, string? child, DiagnosticCode? rejected)
    {
        var c = Create(root, library, child, rootReferencesChild: child is not null);
        Assert.Equal(rejected is null, c.Bind().IsComplete);
        if (rejected is { } code)
        {
            Assert.Equal(code, Assert.Single(c.Binding.Issues).Code);
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedBaseConstraintsControlDerivedCertificates(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "contract C\nstruct S: Base<" + argument + ">\n    Self is C", "public group Api\n    public open struct Base<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
            Assert.Equal(valid, c.Binding.GetConformanceDefinition(structure.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedAssociatedDefinitionsRequireValidInputs(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct S\n    Self is C\n    associate C.Item is Box<" + argument + ">", "public group Api\n    public struct Box<T>\n        T is i32\n    public contract C\n        associate Item", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            Assert.Equal(valid ? string.Empty : "UnsatisfiedConstraint_Kd Box<string>", string.Join(" | ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            var contract = c.SourceModules[1].RootKoto.NestedContainers.Single(x => x.Name == "Api").NestedContainers.Single(x => x.Name == "C");
            Assert.True(c.Binding.GetConformanceDefinition(structure.TypeOf()!, contract.SymbolOf()!)!.IsVerified); // O-54 (a)
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedRuntimeTestTargetsRequireValidInputs(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func f(x: objref/Box<i32>) -> bool => x is Box<" + argument + ">", "public group Api\n    public struct Box<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            var test = Assert.IsType<IsKoto>(function.ExpressionBody);
            Assert.Equal(valid, test.RuntimeTestOf() is not null);
            Assert.Equal(valid, test.StateOf() == BindingState.Resolved);
        }
    }

    [Theory]
    [InlineData(false, false, "i32", true)]
    [InlineData(false, true, "i32", true)]
    [InlineData(true, false, "i32", true)]
    [InlineData(true, true, "i32", true)]
    [InlineData(false, false, "string", false)]
    [InlineData(false, true, "string", false)]
    [InlineData(true, false, "string", false)]
    [InlineData(true, true, "string", false)]
    public void ImportedExpressionProjectionsRetainInputConstraints(bool defaults, bool runtime, string argument, bool valid)
    {
        var library = "public group Api\n    public contract Origin\n        associate Item\n    public struct Source<T>\n        T is i32\n        Self is Origin\n        associate Origin.Item is i32";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + ProjectionConsumer(runtime, "Source<" + argument + ">.(Origin).Item"), library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, runtime, valid);
    }

    [Theory]
    [InlineData(false, false, "public", true)]
    [InlineData(false, true, "public", true)]
    [InlineData(true, false, "public", true)]
    [InlineData(true, true, "public", true)]
    [InlineData(false, false, "internal", false)]
    [InlineData(false, true, "internal", false)]
    [InlineData(true, false, "internal", false)]
    [InlineData(true, true, "internal", false)]
    public void ImportedExpressionProjectionsRetainWitnessValidity(bool defaults, bool runtime, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n        associate Item\n    public struct Local\n        Self is Hidden\n        associate Hidden.Item is i32\n    public contract Origin\n        associate Item\n        func f(self: ref/Self, x: i32) -> i32\n    public struct Source\n        Self is Origin\n        associate Origin.Item is i32\n        public func f(self: ref/Self, x: Local.(Hidden).Item) -> i32 => x";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + ProjectionConsumer(runtime, "Source.(Origin).Item"), library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, runtime, valid);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    [InlineData(3, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, false)]
    [InlineData(4, false, true)]
    [InlineData(4, true, false)]
    [InlineData(4, true, true)]
    [InlineData(5, false, false)]
    [InlineData(5, false, true)]
    [InlineData(5, true, false)]
    [InlineData(5, true, true)]
    [InlineData(6, false, false)]
    [InlineData(6, false, true)]
    [InlineData(6, true, false)]
    [InlineData(6, true, true)]
    public void ImportedDeclarationProjectionsRetainLateWitnessValidity(int form, bool defaults, bool valid)
    {
        var library = "public group Api\n    " + (valid ? "public" : "internal") + " contract Hidden\n        associate Item\n    public struct Local\n        Self is Hidden\n        associate Hidden.Item is i32\n    public open struct Base<T>\n    public contract Origin\n        associate Item\n        func f(self: ref/Self, x: i32) -> i32\n    public struct Source\n        Self is Origin\n        associate Origin.Item is i32\n        public func f(self: ref/Self, x: Local.(Hidden).Item) -> i32 => x";
        var consumer = form switch
        {
            0 => "group Consumer\n    func take(x: Source.(Origin).Item) -> i32 => x\n    func call() -> i32 => take(1)",
            1 => "struct S\n    var field: Source.(Origin).Item",
            2 => "contract C\nenum S\n    A(Source.(Origin).Item)\n    Self is C",
            3 => "contract C\nstruct S: Base<Source.(Origin).Item>\n    Self is C",
            4 => "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Source.(Origin).Item",
            5 => "contract C\n    associate Item is Source.(Origin).Item\nstruct S\n    Self is C",
            _ => "contract C\n    associate Item\nstruct S<T>\n    Self is C when T is Copy\n        associate C.Item is Source.(Origin).Item",
        };
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            if (form == 0)
            {
                var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
                Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).CallOf() is not null);
                return;
            }

            var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            if (form == 1)
            {
                Assert.Equal(valid, type.Members.OfType<PropertyKoto>().Single().SymbolOf()!.Property!.IsVerified);
                return;
            }

            var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
            Assert.All(c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!.Paths, path => Assert.Equal(valid, path.IsVerified));
        }
    }

    [Theory]
    [InlineData("alias Lib => Kimi.Console", true)]
    [InlineData("alias Lib => ::Lib", false)]
    public void DependencyQualifierHidingUsesResolvedIdentity(string source, bool warns)
    {
        var c = Create(source, "public group Api");
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(warns, TestDiagnostics.Of(c, "root.kimi").Any(x => x.Code == nameof(DiagnosticCode.HiddenNamedAlias_Kd)));
    }

    [Fact]
    public void NamedAliasKeepsDependencyDefinitionDefaults()
    {
        var c = Create("alias A => Lib.Api\npublic func main() => A.value()", "public group Api\n    public func value() => writeLine(\"library\")", configure: (_, library) => library.Alias = ["Kimi.Console"]);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified);
        c.SourceModules[1].AddSource(new SourceDocument("Generated.kimi", "group Generated\n    func output() => Console.writeLine(\"generated\")"));
        Assert.True(c.Bind().IsComplete);
    }

    // A prepared compilation of a root project in the directory `root` with one dependency, Lib, in `library` (full paths); the caller
    // adds each module's sources.
    internal static Compilation Prepared(string root, string library)
    {
        var compilation = Compilation.CreateForTest();
        var project = compilation.Project;
        project.Directory = root;
        var file = Path.Combine(library, "Lib.kimiproj");
        project.ProjectFile.Dependencies.Add("Lib", new() { PackageId = "library", PackageVersion = "1", Project = Path.GetRelativePath(root, file) });
        var node = new DependencyNode("root", new(Path.Combine(root, "App.kimiproj"), project.ProjectFile, [], []), -1, string.Empty);
        var dependency = new DependencyNode("library@1", new(file, new() { OutputKind = OutputKind.Library }, [], []), 0, "Lib");
        node.Edges.Add("Lib", dependency.Key);
        Assert.True(compilation.Prepare(TargetProfile.Windows.Target, new([node, dependency])));
        return compilation;
    }

    internal static Compilation Create(string rootSource, string librarySource, string? childSource = null, Action<ProjectFile, ProjectFile>? configure = null, bool rootReferencesChild = false)
    {
        var compilation = Compilation.CreateForTest();
        var project = compilation.Project;
        project.ProjectFile.Dependencies.Add("Lib", new() { PackageId = "library", PackageVersion = "1", Project = "library.kimiproj" });
        var root = new DependencyNode("root", new("root.kimiproj", project.ProjectFile, [], []), -1, string.Empty);
        var library = new DependencyNode("library@1", new("library.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 0, "Lib");
        configure?.Invoke(project.ProjectFile, library.Input.Configuration);
        root.Edges.Add("Lib", library.Key);
        DependencyNode[] nodes = [root, library];
        if (childSource is not null)
        {
            var child = new DependencyNode("child@1", new("child.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 1, "Child");
            library.Edges.Add("Child", child.Key);
            if (rootReferencesChild)
            {
                project.ProjectFile.Dependencies.Add("Child", new() { PackageId = "child", PackageVersion = "1", Project = "child.kimiproj" });
                root.Edges.Add("Child", child.Key);
            }

            nodes = [root, library, child];
        }

        Assert.True(compilation.Prepare(TargetProfile.Windows.Target, new(nodes)));
        compilation.SourceModules[0].AddSource(new SourceDocument("root.kimi", rootSource));
        compilation.SourceModules[1].AddSource(new SourceDocument("library.kimi", librarySource));
        if (childSource is not null)
        {
            compilation.SourceModules[2].AddSource(new SourceDocument("child.kimi", childSource));
        }

        return compilation;
    }

    // A record's node in a configured path's own document (F's ruling N1-9 (1)): named by its configuration and entry, its text the path.
    private static void AssertConfiguredAt(Koto node, string alias, int start, string text)
    {
        Assert.EndsWith($"#Alias[{alias}]", node.CodeContext.SourceDocument!.Path, StringComparison.Ordinal);
        Assert.Equal((start, text.Length, text), (node.Span.Start, node.Span.Length, node.ToString()));
    }

    private static string ProjectionConsumer(bool runtime, string argument)
        => runtime
            ? "struct Target<T>\ngroup Consumer\n    func call(x: objref/Target<i32>) -> bool => x is Target<" + argument + ">"
            : "group Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()";

    private static void VerifyImportedProjectionCertificate(Compilation c, bool runtime, bool valid)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            var expression = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody;
            Assert.Equal(valid, runtime ? Assert.IsType<IsKoto>(expression).RuntimeTestOf() is not null : Assert.IsType<InvocationKoto>(expression).CallOf() is not null);
            if (pass == 0)
            {
                foreach (var module in c.SourceModules)
                {
                    module.OnDeserialized(c);
                }
            }
        }
    }
}
