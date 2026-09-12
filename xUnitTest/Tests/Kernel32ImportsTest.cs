// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Compiler;
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
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var definition = File.ReadAllText(Path.Combine(repo, "backend/windows-x64/kernel32.def")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        Assert.Equal(definition, Kernel32Imports.Definition);
        var exports = definition.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2).Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        Assert.True(exports.SetEquals(Kernel32Imports.Symbols));
        var runtime = File.ReadAllText(Path.Combine(repo, "Kimi/Compiler/Emission/WindowsRuntime.ll"));
        foreach (Match match in Regex.Matches(runtime, @"declare dllimport .*?@(\w+)\("))
        {
            Assert.Contains(match.Groups[1].Value, exports);
        }

        var native = File.ReadAllText(Path.Combine(repo, "backend/windows-x64/tests/native.c"));
        foreach (Match match in Regex.Matches(native, @"(?m)^__declspec\(dllimport\).*\b(\w+)\([^();]*\);"))
        {
            Assert.Contains(match.Groups[1].Value, exports);
        }
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

    [Fact]
    public void InspectionRejectsWrongDllArchitectureMissingAndExtraImports()
    {
        var inspection = "Format: COFF-import-file-x86-64\n" + string.Concat(Kernel32Imports.Symbols.Select(x => $"Symbol: {x}\nSymbol: __imp_{x}\n"));
        Kernel32Imports.ValidateLibrary("KERNEL32.dll\n", inspection);
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("OTHER.dll", inspection));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection.Replace("x86-64", "i386", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection.Replace("Symbol: __imp_ExitProcess\n", string.Empty, StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => Kernel32Imports.ValidateLibrary("KERNEL32.dll", inspection + "Symbol: unexpected\n"));
    }
}
