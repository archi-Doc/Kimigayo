// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Nodes;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    /// <summary>The native build's work by the toolchain's tools, one process each (ExecuteTool, in the output directory):
    /// O0 launches six tools, O2 eight. Temporary (Tatara A2 to A4): unit A4 deletes it.</summary>
    private sealed class ExternalToolExecutor : NativeExecutor
    {
        private static readonly string[] ToolNames = ["opt", "llc", "lld-link", "llvm-nm", "llvm-readobj"];

        private readonly NativeExecutorContext context;
        private readonly Dictionary<string, string> tools;

        private ExternalToolExecutor(NativeExecutorContext context, Dictionary<string, string> tools)
        {
            this.context = context;
            this.tools = tools;
        }

        // The build's tools, from the selected bin directory, each identified in the record.
        internal static NativeExecutor Create(NativeExecutorContext context, JsonObject identities)
        {
            var tools = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in ToolNames)
            {
                var tool = RequireInstalledFile(Path.Combine(context.Bin, name + ".exe"));
                tools.Add(name, tool);
                identities.Add(name, new JsonObject { ["path"] = Redact(tool, context.RedactionRoot) });
            }

            return new ExternalToolExecutor(context, tools);
        }

        internal override async Task Optimize(string ir, string optimizedIr)
        {
            await this.Tool("opt", "-passes=verify", "-disable-output", ir);
            await this.Tool("opt", "-S", "-passes=default<O2>", "-mtriple=" + this.context.Triple, ir, "-o", optimizedIr);
        }

        internal override async Task Emit(string ir, string obj, string optimization)
        {
            await this.Tool("opt", "-passes=verify", "-disable-output", ir);
            string[] arguments =
            [
                "-" + optimization, "-filetype=obj", "-mtriple=" + this.context.Triple, "-mcpu=" + this.context.Cpu,
                "-mattr=" + this.context.Features, "-relocation-model=" + this.context.RelocationModel,
                "-code-model=" + this.context.CodeModel, ir, "-o", Path.GetFileName(obj),
            ];
            await this.Tool("llc", arguments);
        }

        // The two symbol listings and the unwind and directive listing; the flags are the listing's "RuntimeFunction" and "DEFAULTLIB" in
        // any case.
        internal override async Task<ObjectFacts> Inspect(string obj)
        {
            var undefined = await this.Tool("llvm-nm", "--undefined-only", "--format=posix", obj);
            var defined = await this.Tool("llvm-nm", "--defined-only", "--extern-only", "--format=posix", obj);
            var inspection = await this.Tool("llvm-readobj", "--unwind", "--coff-directives", obj);
            return new ObjectFacts(
                PosixSymbols(undefined),
                PosixSymbols(defined),
                inspection.Contains("RuntimeFunction", StringComparison.Ordinal),
                inspection.Contains("DEFAULTLIB", StringComparison.OrdinalIgnoreCase));
        }

        internal override Task Link(IReadOnlyList<string> arguments) => this.Tool("lld-link", [.. arguments]);

        private Task<string> Tool(string name, params string[] args)
            => ExecuteTool(this.tools[name], args, this.context.OutputDirectory, this.context.Report, this.context.CancellationToken);
    }

    // llvm-nm's --format=posix lines: the name, then the kind's letter, the value and the size, each after one space. The name is
    // everything before the last three fields, so a symbol with a space or a tab (SPEC 22.6) is read exactly. The tool control
    // comparison reads it too; unit A4 moves it there.
    internal static List<ObjectSymbol> PosixSymbols(string listing)
    {
        var symbols = new List<ObjectSymbol>();
        foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.AsSpan().TrimEnd('\r');
            var size = text.LastIndexOf(' ');
            var value = size > 0 ? text[..size].LastIndexOf(' ') : -1;
            var kind = value > 0 ? text[..value].LastIndexOf(' ') : -1;
            if (kind < 1 || value != kind + 2)
            {
                throw new InvalidDataException("Unexpected llvm-nm line: " + line);
            }

            symbols.Add(new ObjectSymbol(text[..kind].ToString(), text[kind + 1]));
        }

        return symbols;
    }
}
