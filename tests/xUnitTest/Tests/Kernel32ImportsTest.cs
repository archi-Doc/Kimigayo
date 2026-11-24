// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
        Assert.Equal(ToolchainIdentity.Kernel32DefinitionSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition))));
        var exports = definition.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2).Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        Assert.True(exports.SetEquals(TargetProfile.Windows.Supply("kernel32")!.Symbols));
        foreach (Match match in Regex.Matches(RuntimeTemplates(), @"declare dllimport .*?@(\w+)\("))
        {
            Assert.Contains(match.Groups[1].Value, exports);
        }

        foreach (var suite in (string[])["native.c", "memory.c"])
        {
            var source = File.ReadAllText(Path.Combine(repo, "src/backend/windows-x64/tests", suite));
            foreach (Match match in Regex.Matches(source, @"(?m)^__declspec\(dllimport\).*\b(\w+)\([^();]*\);"))
            {
                Assert.Contains(match.Groups[1].Value, exports);
            }
        }
    }

    // PLAN G137: every runtime template splices each shared ABI definition at exactly one marker and keeps no unexpanded marker;
    // a broken template fails closed. Every `{{` opens a marker (G358), so `{{{` fails. A row's constant marker is unexpanded in a
    // template expanded without the row's constants (the Tatara plan's B7b2a, N2).
    [Theory]
    [InlineData("; no marker\n")]
    [InlineData("{{__kimi_exit}}\n{{__kimi_exit}}\n")]
    [InlineData("{{__kimi_exit}}\n{{__kimi_unknown}}\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{reason_unknown}})\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{reason_size)\n")]
    [InlineData("{{__kimi_exit}}")]
    [InlineData("{{{__kimi_exit}}\n")]
    [InlineData("{{__kimi_exit}}\n  call void @__kimi_abort(i32 {{{reason_size}})\n")]
    [InlineData("{{__kimi_exit}}\n  [ i8 {{static_release_kind}} ] {{unmapped_bound}}\n")]
    public void BrokenRuntimeTemplateFails(string template)
        => Assert.Throws<InvalidDataException>(() => LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit], []));

    // A definition's marker line ends in `\n` or `\r\n`; `\r\n` becomes `\n` and a lone `\r` stays; adjacent markers each expand; an
    // ABI constant's marker takes the constant (G366), the string release kinds among them (G371).
    [Theory]
    [InlineData("{{__kimi_exit}}\r\n  unreachable\n}\n", "  unreachable\n}\n")]
    [InlineData("{{__kimi_exit}}\n  ret\r void\r\r\n}\r", "  ret\r void\r\n}\r")]
    [InlineData("{{__kimi_exit}}\n{{abort_reason_count}}{{reason_size}}{{reason_alloc}}\n", "1812\n")]
    [InlineData("{{__kimi_exit}}\n  ret ptr inttoptr (i64 {{zero_sized_buffer}} to ptr)\n", "  ret ptr inttoptr (i64 4096 to ptr)\n")]
    public void RuntimeTemplateSplicesEachDefinitionOnce(string template, string body)
        => Assert.Equal(RuntimeAbi.Exit.GetDefinition(exported: false) + body, LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit], []));

    // A row's constant markers take the row's constants, which only the row's runtime expansion is given (the Tatara plan's B7b2a, N2):
    // the Windows row's unmapped bound (G371) and its layer's OS-error prefix with its length; a target-free template naming one fails
    // (BrokenRuntimeTemplateFails).
    [Theory]
    [InlineData("{{__kimi_exit}}\n  [ i8 {{static_release_kind}}, i8 {{heap_release_kind}} ] {{unmapped_bound}}\n", "  [ i8 0, i8 1 ] 65536\n")]
    [InlineData("{{__kimi_exit}}\n[{{os_prefix_length}} x i8] c\"{{os_prefix}}\"\n", "[8 x i8] c\" (win32=\"\n")]
    public void ARowsRuntimeTemplateTakesTheRowsConstants(string template, string body) => Assert.Equal(
        RuntimeAbi.Exit.GetDefinition(exported: false) + body,
        LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit], RuntimeAbi.RowConstants(TargetProfile.Windows)));

    // IMPL 21.4.7 (the Tatara plan's decision 24, B7r4): the memory part defines exactly TargetProfile.MemorySymbols, in order, each
    // external under #1, and declares only an LLVM intrinsic; the footer's #1 is #0 plus "no-builtins".
    [Fact]
    public void TheMemoryPartDefinesTheMemorySymbols()
    {
        var text = LlvmModuleText.MemoryRuntime;
        Assert.Equal(TargetProfile.MemorySymbols, Regex.Matches(text, @"(?m)^define (?:ptr|i32) @(\w+)\([^)]*\) #1 \{$").Select(static x => x.Groups[1].Value));
        Assert.Equal(TargetProfile.MemorySymbols.Length, Regex.Matches(text, @"(?m)^define ").Count);
        Assert.All(Regex.Matches(text, @"(?m)^declare [^@]*@([\w.]+)\("), static x => Assert.StartsWith("llvm.", x.Groups[1].Value, StringComparison.Ordinal));
        var footer = LlvmModuleText.Frame(TargetProfile.Windows).Footer;
        var profile = Regex.Match(footer, @"(?m)^attributes #0 = \{ (.*) \}$").Groups[1].Value;
        Assert.Contains("\"target-cpu\"", profile, StringComparison.Ordinal);
        Assert.Contains($"\nattributes #1 = {{ {profile} \"no-builtins\" }}\n", footer, StringComparison.Ordinal);
    }

    // PLAN G371: what is read without a marker. HeapBuffer.intoString (Formatting.kimi) gives its string the release kind 0 or 1 as a
    // literal, so Static is 0 and Heap 1; the zero-sized buffer lies below every row's unmapped bound, so __kimi_free never releases it.
    [Fact]
    public void TheRuntimeTemplatesUnmarkedFactsHold()
    {
        Assert.Equal((0, 1), (RuntimeAbi.StaticReleaseKind, RuntimeAbi.HeapReleaseKind));
        Assert.All(TargetProfile.Rows, static row => Assert.InRange(RuntimeAbi.ZeroSizedBuffer, 1, row.UnmappedBound - 1));
    }

    // PLAN G358, G366, G371: the one ordered scan writes what replacing each marker of a real runtime template, a row's runtime (its
    // platform layer, the common part and the allocator, with the layer's definitions and the row's constants; the Windows and Linux
    // rows') or the test runtime's (the definitions, the reason indices, the reason table and its count, the ABI constants), by its text
    // throughout the whole text wrote.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("x86_64-unknown-linux-musl")]
    [InlineData("TestRuntime")]
    public void RuntimeTemplateExpandsAsEachMarkersReplacement(string runtime)
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var (template, definitions, constants) = TargetProfile.Find(runtime) is { } row
            ? (LlvmModuleText.RuntimeTemplate(RuntimeAbi.Layer(row)), RuntimeAbi.Layer(row).Definitions, RuntimeAbi.RowConstants(row))
            : (File.ReadAllText(Path.Combine(repo, "src/Kimi/Testing/TestRuntime.ll")), [], []);
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
            Assert.True(AbortReasons.Expand(name, text) || RuntimeAbi.Expand(name, constants, text));
            expected = expected.Replace(marker.Value, text.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(expected, LlvmModuleText.ExpandRuntime(template, definitions, constants));
    }

    // PLAN G366: a runtime is a template, so a definition marker line the Windows row's runtime lacks or repeats fails it.
    [Theory]
    [InlineData("{{__kimi_destroy_string}}\n", "")]
    [InlineData("{{__kimi_destroy_string}}\n", "{{__kimi_destroy_string}}\n{{__kimi_destroy_string}}\n")]
    public void ABrokenMarkerLineOfTheRuntimeFails(string marker, string replacement)
    {
        var layer = RuntimeAbi.Layer(TargetProfile.Windows);
        var template = LlvmModuleText.RuntimeTemplate(layer);
        Assert.Contains(marker, template, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => LlvmModuleText.ExpandRuntime(
            template.Replace(marker, replacement, StringComparison.Ordinal), layer.Definitions, RuntimeAbi.RowConstants(TargetProfile.Windows)));
    }

    // PLAN G358, G371: a process's first code generation builds its runtime texts once, the test runtime's among them, each read,
    // expanded in one scan and kept: about 10 bytes per char of the texts (1,768,896 B for 178,644 chars on .NET 10, R8-S1e, the deleted
    // formatting runtime then included). The bound is 11 bytes per char; the per-marker whole-text copies G358 removed cost 51 bytes per
    // char.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public Task AFreshProcessBuildsTheRuntimeTextsOnce()
        => FreshProcess.RunExplicit(typeof(Kernel32ImportsTest), nameof(Kernel32ImportsTest.TheRuntimeTextsOfAFreshProcess));

    // AFreshProcessBuildsTheRuntimeTextsOnce's child: the texts' class initializations and the Windows row's frame, after the
    // initializations of the records and rows they read, against the length of every text they keep.
    [Trait("Purpose", "Allocation")]
    [Fact(Explicit = true)]
    public void TheRuntimeTextsOfAFreshProcess()
    {
        RuntimeHelpers.RunClassConstructor(typeof(RuntimeAbi).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AbortReasons).TypeHandle);
        var allocated = AllocationMeasurement.MeasureOnce(static () =>
        {
            var frame = LlvmModuleText.Frame(TargetProfile.Windows);
            LlvmModuleText.WriteRuntime(TextWriter.Null, frame, null);
            LlvmModuleText.WriteRuntime(TextWriter.Null, frame, TestRuntime.Text);
        });
        var texts = new StringWriter();
        var windows = LlvmModuleText.Frame(TargetProfile.Windows);
        LlvmModuleText.WriteRuntime(texts, windows, null);
        LlvmModuleText.WriteRuntime(texts, windows, TestRuntime.Text);
        texts.Write(LlvmModuleText.TryAllocate + LlvmModuleText.TransferBytes);
        var chars = texts.GetStringBuilder().Length;
        Assert.True(allocated <= 11L * chars, $"runtime texts allocation {allocated} for {chars} chars");
    }

    // Every runtime template's kernel32 declaration (C-468's N3) is a row of the table, with its signature, and each row is declared once.
    [Fact]
    public void RuntimeDeclarationTableMatchesTheEmittedDeclarations()
    {
        var declared = new List<string>();
        foreach (Match match in Regex.Matches(RuntimeTemplates(), @"(?m)^declare dllimport (\S+) @(\w+)\(([^)]*)\)(.*)$"))
        {
            var symbol = match.Groups[2].Value;
            declared.Add(symbol);
            var entry = Assert.Single(TargetProfile.Windows.RuntimeDeclarations, x => x.Symbol == symbol);

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

        Assert.Equal(TargetProfile.Windows.RuntimeDeclarations.Select(static x => x.Symbol).Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));

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

    // IMPL 20.4 (Tatara B6a): a libraries entry naming a compiler's supply is accepted as this compiler writes it, its kind and every
    // identifying field equal and no input, any other member ignored (a sha256 is an assertion the build checks); a field changed or
    // removed, an input added or another kind is not this compiler's, one text naming the entry. Each row edits the entry written from
    // the supply's row: a JSON value, or null to remove the field.
    [Theory]
    [InlineData("kernel32", "generator", "\"other\"")]
    [InlineData("kernel32", "generator", null)]
    [InlineData("kernel32", "dll", "\"OTHER.dll\"")]
    [InlineData("kernel32", "definitionSha256", "\"wrong\"")]
    [InlineData("kernel32", "definitionSha256", null)]
    [InlineData("kernel32", "kind", "\"static\"")]
    [InlineData("kernel32", "input", "\"kernel32.lib\"")]
    [InlineData("kimi_backend", "resolution", "\"other\"")]
    [InlineData("kimi_backend", "resolution", null)]
    [InlineData("kimi_backend", "resolution", "7")]
    [InlineData("kimi_backend", "kind", "\"import\"")]
    [InlineData("kimi_backend", "input", "\"override.lib\"")]
    [InlineData("c", "resolution", "\"other\"")]
    [InlineData("c", "resolution", null)]
    [InlineData("c", "kind", "\"import\"")]
    [InlineData("c", "input", "\"libc.a\"")]
    public void ACompilerSupplysEntryIsAcceptedOnlyAsWritten(string name, string field, string? value)
    {
        var supply = Array.Find(ToolchainIdentity.Supplies, x => x.Supply.Name == name)!;
        var written = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(written))
        {
            LinkManifest.WriteCompilerSupply(json, supply);
        }

        var entry = JsonNode.Parse(written.WrittenSpan)!.AsObject();
        var asserted = JsonNode.Parse(written.WrittenSpan)!.AsObject();
        asserted["sha256"] = "any";
        Assert.Equal(name, entry["name"]!.GetValue<string>());
        foreach (var valid in new[] { entry, asserted })
        {
            using var accepted = JsonDocument.Parse(valid.ToJsonString());
            LinkManifest.CheckCompilerSupply(accepted.RootElement, supply);
        }

        if (value is null)
        {
            Assert.True(entry.Remove(field));
        }
        else
        {
            entry[field] = JsonNode.Parse(value);
        }

        using var invalid = JsonDocument.Parse(entry.ToJsonString());
        Assert.Equal(
            $"The link manifest's 'libraries' entry '{name}' is not this compiler's. Re-emit the manifest.",
            Assert.Throws<InvalidDataException>(() => LinkManifest.CheckCompilerSupply(invalid.RootElement, supply)).Message);
    }

    // Every runtime template's text (C-468's N3): the Backend's embedded templates in name order, then the test runtime's.
    private static string RuntimeTemplates()
    {
        var assembly = typeof(LlvmModuleText).Assembly;
        var text = new StringBuilder();
        foreach (var name in assembly.GetManifestResourceNames().Where(static x => x.StartsWith("Kimi.Compiler.Backend.", StringComparison.Ordinal) &&
            x.EndsWith(".ll.in", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            text.Append(reader.ReadToEnd()).Append('\n');
        }

        return text.Append(TestRuntime.Text).ToString();
    }
}
