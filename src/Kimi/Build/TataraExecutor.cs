// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Nodes;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    /// <summary>The native build's work in process, by <see cref="Tatara"/> from the toolchain root's tatara.dll. Until unit A4 only
    /// tests select it, through BuildManifest's factory overload, which unit A4 removes when this executor becomes the build's; no
    /// command, option or environment variable does.</summary>
    internal sealed class TataraExecutor : NativeExecutor
    {
        private readonly Tatara tatara;
        private readonly NativeExecutorContext context;

        private TataraExecutor(Tatara tatara, NativeExecutorContext context)
        {
            this.tatara = tatara;
            this.context = context;
        }

        // Loads and checks the root's tatara.dll and identifies it in the record's tools.
        internal static NativeExecutor Create(NativeExecutorContext context, JsonObject identities)
        {
            var tatara = Tatara.Load(context.Root);
            identities.Add("tatara", new JsonObject
            {
                ["path"] = Redact(tatara.Path, context.RedactionRoot), ["build"] = tatara.Build, ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion,
            });
            return new TataraExecutor(tatara, context);
        }

        internal override Task Optimize(string ir, string optimizedIr)
        {
            this.tatara.Optimize(this.context, ir, optimizedIr);
            return Task.CompletedTask;
        }

        // The level is the optimization's digit (O0, O2).
        internal override Task Emit(string ir, string obj, string optimization)
        {
            this.tatara.Emit(this.context, ir, obj, optimization[1] - '0');
            return Task.CompletedTask;
        }

        internal override Task<ObjectFacts> Inspect(string obj) => Task.FromResult(this.tatara.Inspect(this.context, obj));

        internal override Task Link(IReadOnlyList<string> arguments)
        {
            this.tatara.Link(this.context, arguments);
            return Task.CompletedTask;
        }
    }
}
