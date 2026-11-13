// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Testing;
using Xunit;

namespace XunitTest;

public sealed class Kernel32ImportsTest
{
    [Fact]
    public void OldManifestRequiresReEmission()
    {
        using var json = JsonDocument.Parse("{\"schemaVersion\":1}");
        var error = Assert.Throws<InvalidDataException>(() => NativeToolchain.ValidateManifest(json.RootElement));
        Assert.Contains("Re-emit", error.Message);
    }

    [Fact]
    public void DefinitionCoversRuntimeAndNativeTestImports()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var definition = File.ReadAllText(Path.Combine(repo, "src/backend/windows-x64/kernel32.def")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        Assert.Equal(definition, Kernel32Imports.Definition);
        var exports = definition.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2).Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        Assert.True(exports.SetEquals(WindowsProfile.Kernel32Symbols));
        var runtime = File.ReadAllText(Path.Combine(repo, "src/Kimi/Compiler/Backend/WindowsRuntime.ll.in"));
        foreach (Match match in Regex.Matches(runtime, @"declare dllimport .*?@(\w+)\("))
        {
            Assert.Contains(match.Groups[1].Value, exports);
        }

        var native = File.ReadAllText(Path.Combine(repo, "src/backend/windows-x64/tests/native.c"));
        foreach (Match match in Regex.Matches(native, @"(?m)^__declspec\(dllimport\).*\b(\w+)\([^();]*\);"))
        {
            Assert.Contains(match.Groups[1].Value, exports);
        }
    }

    // PLAN G137: every runtime template splices each shared ABI definition at exactly one marker and keeps no unexpanded marker;
    // a broken template fails closed. Every `{{` opens a marker (G358), so `{{{` fails.
    [Theory]
    [InlineData("; no marker\n")]
    [InlineData("{{__kimi_exit}}\n{{__kimi_exit}}\n")]
    [InlineData("{{__kimi_exit}}\n{{__kimi_unknown}}\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{reason_unknown}})\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{reason_size)\n")]
    [InlineData("{{__kimi_exit}}")]
    [InlineData("{{{__kimi_exit}}\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{{reason_size}})\n")]
    public void BrokenRuntimeTemplateFails(string template)
        => Assert.Throws<InvalidDataException>(() => LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit]));

    // A definition's marker line ends in `\n` or `\r\n`; `\r\n` becomes `\n` and a lone `\r` stays; adjacent markers each expand; an
    // ABI constant's marker takes the constant (G366), the string release kinds and the profile's unmapped bound among them (G371).
    [Theory]
    [InlineData("{{__kimi_exit}}\r\n  unreachable\n}\n", "  unreachable\n}\n")]
    [InlineData("{{__kimi_exit}}\n  ret\r void\r\r\n}\r", "  ret\r void\r\n}\r")]
    [InlineData("{{__kimi_exit}}\n{{abort_reason_count}}{{reason_size}}{{reason_heap}}\n", "1812\n")]
    [InlineData("{{__kimi_exit}}\n  ret ptr inttoptr (i64 {{zero_sized_buffer}} to ptr)\n", "  ret ptr inttoptr (i64 4096 to ptr)\n")]
    [InlineData("{{__kimi_exit}}\n  [ i8 {{static_release_kind}}, i8 {{heap_release_kind}} ] {{unmapped_bound}}\n", "  [ i8 0, i8 1 ] 65536\n")]
    public void RuntimeTemplateSplicesEachDefinitionOnce(string template, string body)
        => Assert.Equal(RuntimeAbi.Exit.GetDefinition(exported: false) + body, LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit]));

    // PLAN G371: what is read without a marker. HeapBuffer.intoString (Formatting.kimi) gives its string the release kind 0 or 1 as a
    // literal, so Static is 0 and Heap 1; the zero-sized buffer lies below the unmapped bound, so __kimi_free never releases it.
    [Fact]
    public void TheRuntimeTemplatesUnmarkedFactsHold()
    {
        Assert.Equal((0, 1), (RuntimeAbi.StaticReleaseKind, RuntimeAbi.HeapReleaseKind));
        Assert.InRange(RuntimeAbi.ZeroSizedBuffer, 1, WindowsProfile.UnmappedBound - 1);
    }

    // PLAN G358, G366, G371: the one ordered scan writes what replacing each marker of a real runtime template, the Windows runtime's,
    // the Array runtime's or the test runtime's (the definitions, the reason indices, the reason table and its count, the ABI
    // constants), by its text throughout the whole text wrote.
    [Theory]
    [InlineData("WindowsRuntime")]
    [InlineData("TestRuntime")]
    public void RuntimeTemplateExpandsAsEachMarkersReplacement(string runtime)
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var (template, definitions) = runtime switch
        {
            "WindowsRuntime" => (File.ReadAllText(Path.Combine(repo, "src/Kimi/Compiler/Backend/WindowsRuntime.ll.in")), RuntimeAbi.RuntimeDefinitions),
            _ => (File.ReadAllText(Path.Combine(repo, "src/Kimi/Testing/TestRuntime.ll")), Array.Empty<FunctionAbi>()),
        };
        var expected = template;
        foreach (Match marker in Regex.Matches(template, @"\{\{(\w+)\}\}"))
        {
            var name = marker.Groups[1].Value;
            if (Array.Find(definitions, x => x.Name == name) is { } abi)
            {
                expected = expected.Replace(marker.Value + "\n", abi.GetDefinition(exported: false), StringComparison.Ordinal);
                continue;
            }

            var text = new StringBuilder();
            Assert.True(AbortReasons.Expand(name, text) || RuntimeAbi.Expand(name, text));
            expected = expected.Replace(marker.Value, text.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(expected, LlvmModuleText.ExpandRuntime(template, definitions));
    }

    // PLAN G366: a runtime is a template, so a definition marker line it lacks or repeats fails it.
    [Theory]
    [InlineData("{{__kimi_write_line}}\n", "")]
    [InlineData("{{__kimi_write_line}}\n", "{{__kimi_write_line}}\n{{__kimi_write_line}}\n")]
    public void ABrokenMarkerLineOfTheRuntimeFails(string marker, string replacement)
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var template = File.ReadAllText(Path.Combine(repo, "src/Kimi/Compiler/Backend/WindowsRuntime.ll.in"));
        Assert.Contains(marker, template, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => LlvmModuleText.ExpandRuntime(
            template.Replace(marker, replacement, StringComparison.Ordinal), RuntimeAbi.RuntimeDefinitions));
    }

    // PLAN G358, G371: a process's first code generation builds its runtime texts once, the test runtime's among them, each read,
    // expanded in one scan and kept: about 10 bytes per char of the texts (1,768,896 B for 178,644 chars on .NET 10, R8-S1e, the deleted
    // formatting runtime then included). The bound is 11 bytes per char; the per-marker whole-text copies G358 removed cost 51 bytes per
    // char.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public Task AFreshProcessBuildsTheRuntimeTextsOnce()
        => FreshProcess.RunExplicit(typeof(Kernel32ImportsTest), nameof(Kernel32ImportsTest.TheRuntimeTextsOfAFreshProcess));

    // AFreshProcessBuildsTheRuntimeTextsOnce's child: the texts' class initializations, after those of the records and rows they
    // read, against the length of every text they keep.
    [Trait("Purpose", "Allocation")]
    [Fact(Explicit = true)]
    public void TheRuntimeTextsOfAFreshProcess()
    {
        RuntimeHelpers.RunClassConstructor(typeof(RuntimeAbi).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AbortReasons).TypeHandle);
        var allocated = AllocationMeasurement.MeasureOnce(static () =>
        {
            LlvmModuleText.WriteRuntime(TextWriter.Null, null);
            LlvmModuleText.WriteRuntime(TextWriter.Null, TestRuntime.Text);
        });
        var texts = new StringWriter();
        LlvmModuleText.WriteRuntime(texts, null);
        LlvmModuleText.WriteRuntime(texts, TestRuntime.Text);
        texts.Write(LlvmModuleText.TryAllocate + LlvmModuleText.TransferBytes);
        var chars = texts.GetStringBuilder().Length;
        Assert.True(allocated <= 11L * chars, $"runtime texts allocation {allocated} for {chars} chars");
    }

    [Fact]
    public void RuntimeDeclarationTableMatchesTheEmittedDeclarations()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var runtime = File.ReadAllText(Path.Combine(repo, "src/Kimi/Compiler/Backend/WindowsRuntime.ll.in")) +
            File.ReadAllText(Path.Combine(repo, "src/Kimi/Testing/TestRuntime.ll"));
        var declared = new List<string>();
        foreach (Match match in Regex.Matches(runtime, @"(?m)^declare dllimport (\S+) @(\w+)\(([^)]*)\)(.*)$"))
        {
            var symbol = match.Groups[2].Value;
            declared.Add(symbol);
            var entry = Assert.Single(WindowsProfile.RuntimeDeclarations, x => x.Symbol == symbol);

            // A noreturn declaration is unshareable; otherwise the recorded import-ABI codes must agree.
            if (match.Groups[4].Value.Contains("noreturn", StringComparison.Ordinal))
            {
                Assert.Null(entry.Signature);
                continue;
            }

            var codes = Code(match.Groups[1].Value) + string.Concat(match.Groups[3].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Code));
            Assert.Equal(codes, entry.Signature);
        }

        Assert.Equal(declared, WindowsProfile.RuntimeDeclarations.Select(x => x.Symbol));

        static string Code(string type) => type switch
        {
            "ptr" => "p",
            "i8" => "1",
            "i16" => "2",
            "i32" => "4",
            "i64" => "8",
            "float" => "f",
            "double" => "d",
            "void" => "v",
            _ => type,
        };
    }

    [Theory]
    [InlineData("generator", "other")]
    [InlineData("dll", "OTHER.dll")]
    [InlineData("kind", "static")]
    [InlineData("definitionSha256", "wrong")]
    [InlineData("input", "kernel32.lib")]
    public void ManifestRejectsSubstitutedSupplies(string field, string value)
    {
        var library = new JsonObject { ["name"] = "kernel32", ["kind"] = "import", ["generator"] = "llvm-dlltool", ["dll"] = "KERNEL32.dll", ["definitionSha256"] = Kernel32Imports.DefinitionSha256 };
        using var valid = JsonDocument.Parse(library.ToJsonString());
        Kernel32Imports.ValidateManifest(valid.RootElement);
        library[field] = value;
        using var invalid = JsonDocument.Parse(library.ToJsonString());
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateManifest(invalid.RootElement));
    }

    [Theory]
    [InlineData("generator")]
    [InlineData("generatorSha256")]
    [InlineData("target")]
    [InlineData("dll")]
    [InlineData("definitionSha256")]
    [InlineData("sha256")]
    public void InstallationRejectsChangedGenerationConditions(string field)
    {
        var entry = new JsonObject
        {
            ["generator"] = Kernel32Imports.Generator, ["generatorSha256"] = Kernel32Imports.DlltoolSha256,
            ["target"] = WindowsProfile.Target, ["dll"] = Kernel32Imports.Dll,
            ["definitionSha256"] = Kernel32Imports.DefinitionSha256, ["sha256"] = "actual library hash",
        };
        using var valid = JsonDocument.Parse(entry.ToJsonString());
        Kernel32Imports.ValidateInstallation(valid.RootElement, "actual library hash");
        entry[field] = "changed";
        using var invalid = JsonDocument.Parse(entry.ToJsonString());
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateInstallation(invalid.RootElement, "actual library hash"));
    }

    [Fact]
    public void InspectionRejectsWrongDllArchitectureMissingAndExtraImports()
    {
        var inspection = "Format: COFF-import-file-x86-64\n" + string.Concat(WindowsProfile.Kernel32Symbols.Select(x => $"Symbol: {x}\nSymbol: __imp_{x}\n"));
        Kernel32Imports.ValidateLibrary("KERNEL32.dll\n", inspection);
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("OTHER.dll", inspection));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection.Replace("x86-64", "i386", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection.Replace("Symbol: __imp_ExitProcess\n", string.Empty, StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection + "Symbol: unexpected\n"));
    }
}
