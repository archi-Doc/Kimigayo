// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ModuleBindingTest
{
    [Theory]
    [InlineData("public func main() => Lib.Api.value()", true)]
    [InlineData("alias Lib.Api\npublic func main() => value()", true)]
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
            Assert.True(compilation.Ownership.Analyze().IsVerified);
            Assert.False(compilation.Emission.Validate(out _));
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
    [InlineData("public func unused() => missing()", false, true)]
    [InlineData("public func unused()\n    let text = \"moved\"\n    ::Core.writeLine(text)\n    ::Core.writeLine(text)", true, false)]
    [InlineData("public func unused() => ()", true, true)]
    public void UnusedDependencyBodiesAreChecked(string library, bool bound, bool owned)
    {
        var compilation = Create("public func main() => ()", library);
        Assert.Equal(bound, compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        if (bound)
        {
            Assert.Equal(owned, compilation.Ownership.Analyze().IsVerified);
        }
    }

    [Fact]
    public void DependencyRuntimeCannotBecomeApplicationStartup()
    {
        var compilation = Create("public func main() => ()", "::Core.writeLine(\"illegal\")");
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
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => Lib.Api.test()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void AliasesDoNotLeakBetweenDocumentsOrModules()
    {
        var compilation = Create("alias Lib.Api\npublic func main() => value()", "public group Api\n    public func value() => ()");
        compilation.Kotonoha.AddSource(new SourceDocument("other.kimi", "public func other() => value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void DependenciesResolveTheirOwnDirectNames()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => Child.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => Child.Api.value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void RebindingRetainsModuleSymbolsAndInvalidatesAllBodies()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
        Assert.False(compilation.Ownership.Result.IsVerified);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.SourceModules[1].AddSource(new SourceDocument("bad.kimi", "public func broken() => missing()"));
        Assert.False(compilation.Bind().IsComplete);
        Assert.False(compilation.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void CompileTimeSettingsBelongToEachModule()
    {
        var compilation = Create("#if feature\npublic func main() => Lib.Api.value()", "#if feature\npublic func broken() => missing()\n#if not feature\npublic group Api\n    public func value() => ()", configure: (root, library) =>
        {
            root.CompileTimeSettings.Add("feature", new() { Bool = true });
            library.CompileTimeSettings.Add("feature", new() { Bool = false });
        });
        Assert.True(compilation.Bind().IsComplete, string.Join("; ", compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + " | " + string.Join("; ", compilation.SourceModules.SelectMany(x => x.DiagnosticCollection.GetArray()).Select(x => x.ToString())));
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("Lib.Api", true)]
    [InlineData("Lib.Api.", false)]
    [InlineData("Api", false)]
    [InlineData("Lib.Missing", false)]
    public void DefaultAliasesResolveDirectContainerPaths(string alias, bool expected)
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = [alias]);
        Assert.Equal(expected, compilation.Bind().IsComplete);
    }

    [Fact]
    public void LibraryDefaultsDoNotLeakToTheCaller()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => helper()\nprivate group Implementation\n    public func helper() => ()", configure: (_, library) => library.Alias = ["Implementation"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => helper()"));
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
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
    }

    [Fact]
    public void ExplicitAliasesPrecedeDefaultsAndCannotUseDefaultsToResolvePaths()
    {
        var compilation = Create("alias Local\npublic group Local\n    public func value() -> i32 => 1\npublic func main()\n    let x: i32 = value()", "public group Api\n    public func value() -> bool => true", configure: (root, _) => root.Alias = ["Lib.Api"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "alias Api\npublic func bad() => ()"));
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
        Assert.True(compilation.Ownership.Analyze().IsVerified);
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
        var compilation = Create("alias Outer.Hidden\npublic group Outer\n    private group Hidden\n        public func value() => ()\n    public func use() => value()\npublic func main() => Outer.use()", "public func unused() => ()");
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void WarmModuleBindingAndOwnershipAllocateNothing()
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = ["Lib.Api"]);
        void Analyze()
        {
            if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Module analysis failed.");
            }
        }

        for (var i = 0; i < 100; i++)
        {
            Analyze();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
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

    private static Compilation Create(string rootSource, string librarySource, string? childSource = null, Action<ProjectFile, ProjectFile>? configure = null)
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
            nodes = [root, library, child];
        }

        Assert.True(compilation.Prepare(WindowsProfile.Target, new(nodes)));
        compilation.SourceModules[0].AddSource(new SourceDocument("root.kimi", rootSource));
        compilation.SourceModules[1].AddSource(new SourceDocument("library.kimi", librarySource));
        if (childSource is not null)
        {
            compilation.SourceModules[2].AddSource(new SourceDocument("child.kimi", childSource));
        }

        return compilation;
    }
}
