// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class LibraryEmissionTest
{
    // A Library's module from the generation entry exports no definition and has no OS entry (DECISIONS R5-M12's class S assertion on the
    // old generator's module, rewritten at G3); test-library.ps1 emits these sources through the CLI and checks the IR natively.
    [Theory]
    [InlineData("")]
    [InlineData("public func main(value: i32) -> i32 => value + 1")]
    [InlineData("public func main() => $abort(\"must not execute\")")]
    [InlineData("public group Api\n    public func answer() -> i32 => 42\n    public func text() -> string => \"library\"")]
    [InlineData("public group Api\n    public func keep<T>(value: T) -> T => value@move")]
    [InlineData("public struct Value\n    public let value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")")]
    public void EmitsInspectionWithoutAnOsEntry(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Startup.IsComplete);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Matches(@"(?m)^define internal ", ir);
        Assert.DoesNotMatch(@"(?m)^define (?!internal )", ir);
        Assert.DoesNotContain("@__kimi_start", ir);
        Assert.DoesNotContain("@__kimi_entry_body", ir);
        Assert.Contains("@_fltused = global i32 0, align 4", ir);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("let pending: i32")]
    [InlineData("Console.writeLine(\"runtime\")")]
    [InlineData("func unused() => missing()")]
    [InlineData("func unused()\n    let text = \"moved\"\n    let taken = text@move\n    Console.writeLine(text)")]
    [InlineData("public group Api\n    public func keep<T>(value: T) -> T => value")]
    [InlineData("group State\n    var value: i32 = 1")]
    public void InvalidOrUnsupportedLibraryCannotWrite(string source)
    {
        var c = Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManifestUsesTheVerifiedOutputKind(bool testBuild)
    {
        var c = Analyze(testBuild ? "#Test\nfunc sample() => $expect(true)" : "public func main(x: i32) -> i32 => x", testBuild);
        var directory = Path.Combine(Path.GetTempPath(), "kimi-library-" + Guid.NewGuid().ToString("N"));
        c.Project.Directory = directory;
        try
        {
            Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
            var root = manifest.RootElement;
            Assert.Equal(testBuild ? "Application" : "Library", root.GetProperty("outputKind").GetString());
            Assert.Equal(testBuild ? WindowsProfile.EntrySymbol : null, root.GetProperty("entry").GetString());
            Assert.Equal(testBuild ? WindowsProfile.Subsystem : null, root.GetProperty("subsystem").GetString());
            Assert.Equal(ArtifactFiles.Hash(path!), root.GetProperty("irSha256").GetString());
            Assert.False(File.Exists(Path.ChangeExtension(path!, ".link.build.json")));
            if (testBuild)
            {
                NativeToolchain.ValidateManifest(root);
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => NativeToolchain.ValidateManifest(root));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static Compilation Analyze(string source, bool testBuild = false)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = OutputKind.Library;
        c.IsTestBuild = testBuild;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Library.kimi", source));
        c.Bind();
        if (testBuild)
        {
            c.Binding.CheckTestStartup();
        }
        else
        {
            c.Binding.CheckStartup(OutputKind.Library);
        }

        // A test build's catalog is discovered after acceptance, as Project discovers it: MIR code generation reads the discovery of the
        // version it generates (DECISIONS R5-M136).
        if (c.Analyze().IsVerified && testBuild)
        {
            c.Tests.Discover(c);
        }

        return c;
    }
}
