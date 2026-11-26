// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
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
    // the Windows row's unmapped bound (G371) and its layer's OS-error prefix with its length, and the none layer's prefix (IMPL 21.6); a
    // target-free template naming one fails (BrokenRuntimeTemplateFails).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "{{__kimi_exit}}\n  [ i8 {{static_release_kind}}, i8 {{heap_release_kind}} ] {{unmapped_bound}}\n", "  [ i8 0, i8 1 ] 65536\n")]
    [InlineData("x86_64-pc-windows-msvc", "{{__kimi_exit}}\n[{{os_prefix_length}} x i8] c\"{{os_prefix}}\"\n", "[8 x i8] c\" (win32=\"\n")]
    [InlineData("x86_64-unknown-none", "{{__kimi_exit}}\n[{{os_prefix_length}} x i8] c\"{{os_prefix}}\"\n", "[11 x i8] c\" (platform=\"\n")]
    public void ARowsRuntimeTemplateTakesTheRowsConstants(string row, string template, string body) => Assert.Equal(
        RuntimeAbi.Exit.GetDefinition(exported: false) + body,
        LlvmModuleText.ExpandRuntime(template, [RuntimeAbi.Exit], RuntimeAbi.RowConstants(TargetProfile.Find(row)!)));

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
        Assert.All(TargetProfile.AllRows, static row => Assert.InRange(RuntimeAbi.ZeroSizedBuffer, 1, row.UnmappedBound - 1));
    }

    // PLAN G358, G366, G371: the one ordered scan writes what replacing each marker of a real runtime template, a row's runtime (its
    // platform layer, the common part and the allocator, with the layer's definitions and the row's constants; the Windows and Linux
    // rows' and the none row's, IMPL 21.6) or the test runtime's (the definitions, the reason indices, the reason table and its count, the
    // ABI constants), by its text throughout the whole text wrote.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("x86_64-unknown-linux-musl")]
    [InlineData("x86_64-unknown-none")]
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

    // IMPL 21.6: the none row's runtime holds Limine's requests of base revision 6, each an internal 8-byte-aligned global in its
    // section and in the runtime's compiler-used list: the start marker, the base-revision tag, the higher-half direct map and memory
    // map requests (the common magic, the request's id, revision 0, no response) and the end marker.
    [Theory]
    [InlineData("start", "[4 x i64] [i64 u0xf6b8f4b39de7d1ae, i64 u0xfab91a6940fcb9cf, i64 u0x785c6ed015d3e316, i64 u0x181e920a7852b9d9]", "_start")]
    [InlineData("base_revision", "[3 x i64] [i64 u0xf9562b2d5c95a6c8, i64 u0x6a7b384944536bdc, i64 6]", "")]
    [InlineData("hhdm", "{ [4 x i64], i64, ptr } { [4 x i64] [i64 u0xc7b1dd30df4c8b88, i64 u0x0a82e883a194f07b, i64 u0x48dcf1cb8ad2b852, i64 u0x63984e959a98244b], i64 0, ptr null }", "")]
    [InlineData("memmap", "{ [4 x i64], i64, ptr } { [4 x i64] [i64 u0xc7b1dd30df4c8b88, i64 u0x0a82e883a194f07b, i64 u0x67cf3d9d378a806f, i64 u0xe304acdfc50c3c62], i64 0, ptr null }", "")]
    [InlineData("end", "[2 x i64] [i64 u0xadc0e0531bb10d03, i64 u0x9572709f31764c62]", "_end")]
    public void TheNoneRuntimeKeepsEachLimineRequestInItsSection(string request, string value, string section)
    {
        var runtime = LlvmModuleText.Frame(TargetProfile.None).Runtime;
        Assert.Contains($"\n@__kimi_limine_{request} = internal global {value}, section \".limine_requests{section}\", align 8\n", runtime, StringComparison.Ordinal);
        var kept = Regex.Match(runtime, @"\n@llvm\.compiler\.used = appending global \[(\d+) x ptr\] \[([^\]]*)\], section ""llvm\.metadata""\n");
        Assert.Contains("ptr @__kimi_limine_" + request, kept.Groups[2].Value.Split(", "));
        Assert.Equal(5, int.Parse(kept.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    // IMPL 21.6: the none row's boot reads the loader's three answers volatile (the tag's third word, each request's response), halts the
    // processor with interrupts disabled when the loader did not accept the revision or left a request unanswered, and runs before the
    // program's platform start, which takes the Boot it filled.
    [Fact]
    public void TheNoneBootReadsTheLoadersAnswersBeforeThePlatformStart()
    {
        var runtime = LlvmModuleText.Frame(TargetProfile.None).Runtime;
        var boot = Definition(runtime, "define internal void @__kimi_boot(ptr %boot) #0 {\n");
        Assert.Equal(3, Regex.Matches(boot, " = load volatile ").Count);
        Assert.Contains("  %accepted_slot = getelementptr i8, ptr @__kimi_limine_base_revision, i64 16\n  %accepted = load volatile i64, ptr %accepted_slot, align 8\n", boot, StringComparison.Ordinal);
        Assert.Contains("  %hhdm_slot = getelementptr i8, ptr @__kimi_limine_hhdm, i64 40\n  %hhdm = load volatile ptr, ptr %hhdm_slot, align 8\n", boot, StringComparison.Ordinal);
        Assert.Contains("  %memmap_slot = getelementptr i8, ptr @__kimi_limine_memmap, i64 40\n  %memmap = load volatile ptr, ptr %memmap_slot, align 8\n", boot, StringComparison.Ordinal);
        Assert.Contains(
            "  %refused = icmp ne i64 %accepted, 0\n  %no_hhdm = icmp eq ptr %hhdm, null\n  %no_memmap = icmp eq ptr %memmap, null\n" +
            "  %unanswered = or i1 %no_hhdm, %no_memmap\n  %failed = or i1 %refused, %unanswered\n  br i1 %failed, label %halt, label %answered\n" +
            "halt:\n  call void @__kimi_halt()\n  unreachable\n",
            boot,
            StringComparison.Ordinal);
        Assert.Contains(
            "\ndefine internal void @__kimi_halt() noreturn #0 {\nentry:\n  br label %halt\nhalt:\n" +
            "  call void asm sideeffect \"cli\\0Ahlt\", \"~{memory},~{dirflag},~{fpsr},~{flags}\"()\n  br label %halt\n}\n",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "  call void @__kimi_boot(ptr %boot)\n  call void @" + RuntimeAbi.PlatformMemberStart.Name + "(ptr %boot)\n",
            Definition(runtime, RuntimeAbi.PlatformStart.GetDefinition(exported: false)),
            StringComparison.Ordinal);
    }

    // IMPL 21.6: each platform routine of the none row tests the busy flag (set: the processor halts), sets it, calls its adapter as the
    // adapter's record spells its result and parameter types, clears it after but in the exit, which never returns, and maps the
    // adapter's result: a write's and a release's 0 is success (-1), their -1 a failure without an OS error, any other result the error;
    // a run below the unmapped bound or not 4,096-aligned is refused as null; the clock's result is returned as it is.
    [Fact]
    public void EachNoneRoutineCallsItsAdapterUnderTheBusyFlagAndMapsItsResult()
    {
        var runtime = LlvmModuleText.Frame(TargetProfile.None).Runtime;
        const string Clear = "  store i8 0, ptr @__kimi_platform_busy, align 1\n";
        var outcome = Clear + "  %succeeded = icmp eq i64 %result, 0\n  %unexplained = icmp eq i64 %result, -1\n  %failure = select i1 %unexplained, i64 " +
            RuntimeAbi.NoOsError.ToString(CultureInfo.InvariantCulture) + ", i64 %result\n  %error = select i1 %succeeded, i64 -1, i64 %failure\n" +
            "  ret i64 %error\n}\n";
        (FunctionAbi Routine, FunctionAbi Adapter, string Mapping)[] routines =
        [
            (RuntimeAbi.PlatformStart, RuntimeAbi.PlatformMemberStart, Clear + "  ret void\n}\n"),
            (RuntimeAbi.Pages, RuntimeAbi.PlatformMemberMapPages, Clear + "  %address = ptrtoint ptr %run to i64\n" +
                $"  %low = icmp ult i64 %address, {TargetProfile.None.UnmappedBound}\n  %offset = and i64 %address, 4095\n" +
                "  %misaligned = icmp ne i64 %offset, 0\n  %refused = or i1 %low, %misaligned\n  %result = select i1 %refused, ptr null, ptr %run\n" +
                "  ret ptr %result\n}\n"),
            (RuntimeAbi.ReleasePages, RuntimeAbi.PlatformMemberReleasePages, outcome),
            (RuntimeAbi.Write, RuntimeAbi.PlatformMemberWrite, outcome),
            (RuntimeAbi.Clock, RuntimeAbi.PlatformMemberClock, Clear + "  ret i64 %result\n}\n"),
            (RuntimeAbi.Exit, RuntimeAbi.PlatformMemberTerminate, "  unreachable\n}\n"),
        ];
        foreach (var (routine, adapter, mapping) in routines)
        {
            var body = Definition(runtime, routine.GetDefinition(exported: false));
            var guard = body.IndexOf(
                "  %busy = load i8, ptr @__kimi_platform_busy, align 1\n  %reentered = icmp ne i8 %busy, 0\n  br i1 %reentered, label %halt, label %call\n" +
                "halt:\n  call void @__kimi_halt()\n  unreachable\ncall:\n  store i8 1, ptr @__kimi_platform_busy, align 1\n",
                StringComparison.Ordinal);
            var arguments = string.Join(", ", adapter.Parameters.Select(static x => Regex.Escape(x.Type) + @" %\w+"));
            var call = Regex.Match(body, $@"\n  (?:%\w+ = )?call {Regex.Escape(adapter.Result)} @{adapter.Name}\({arguments}\)\n");
            Assert.True(guard >= 0 && call.Success && guard < call.Index, routine.Name);
            Assert.Equal(mapping, body[(call.Index + call.Length)..]);
            Assert.Equal(adapter.NoReturn ? 0 : 1, body.Split(Clear).Length - 1);
        }
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
    // identifying field equal and no input, any other member ignored (a sha256 is an assertion the build checks against a one-file
    // supply's file); a field changed or removed, an input added, another kind or a sha256 on a supply of several files (c: T's ruling 79,
    // the Middle's N3; on 0e7a59618 it was accepted and failed every build) is not this compiler's, one text naming the entry. Each row
    // edits the entry written from the supply's row: a JSON value, or null to remove the field.
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
    [InlineData("c", "sha256", "\"any\"")]
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
        foreach (var valid in supply.Files.Length == 1 ? new[] { entry, asserted } : [entry])
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

    // The one definition of a runtime text whose define line is head, from the line after it through its closing "}\n".
    private static string Definition(string runtime, string head)
    {
        var start = runtime.IndexOf("\n" + head, StringComparison.Ordinal);
        Assert.True(start >= 0 && runtime.IndexOf("\n" + head, start + 1, StringComparison.Ordinal) < 0, head);
        start += head.Length + 1;
        return runtime[start..(runtime.IndexOf("\n}\n", start, StringComparison.Ordinal) + 3)];
    }
}
