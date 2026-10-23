// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Benchmark;

// Measures attachment only, with parsing outside the window and both input trees alive for retained-memory comparison.
internal static class SyntaxEditMeasurements
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var size in new[] { 64, 256, 1024, 4096 })
        {
            var text = new StringBuilder("func moved()\n");
            for (var i = 0; i < size; i++)
            {
                text.Append("    _ = 1 + 2\n");
            }

            foreach (var scope in new[] { "same-module", "cross-module" })
            {
                var samples = new List<object>();
                for (var sample = 0; sample < 7; sample++)
                {
                    var recipient = Compilation.CreateForTest();
                    var donor = scope == "same-module" ? recipient : Compilation.CreateForTest();
                    Require(recipient.Prepare(WindowsProfile.Target));
                    if (!ReferenceEquals(recipient, donor))
                    {
                        Require(donor.Prepare(WindowsProfile.Target));
                    }

                    recipient.Kotonoha.AddSource(new SourceDocument("target.kimi", "func target() => ()"));
                    donor.Kotonoha.AddSource(new SourceDocument("donor.kimi", text.ToString()));
                    Require(!donor.Diagnostics.HasErrors && !recipient.Diagnostics.HasErrors);
                    var moved = donor.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "moved");
                    var original = recipient.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "target");
                    Require(KotoHelper.Replace(moved.Parent!, moved, new CodeBlockKoto(moved.CodeContext)));
                    var parent = original.Parent!;
                    var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    var replaced = KotoHelper.Replace(parent, original, moved);
                    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    var retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
                    Require(replaced);
                    var check = new OwnershipCheck(recipient.Kotonoha);
                    check.Visit(moved);
                    samples.Add(new { milliseconds = elapsed, allocatedBytes = allocated, retainedDelta = retainedAfter - retainedBefore, nodes = check.Count });
                    GC.KeepAlive(donor);
                    GC.KeepAlive(recipient);
                    GC.KeepAlive(original);
                }

                results.Add(new { size, scope, samples });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                compiler = Compilation.CompilerIdentity,
                runtime = Environment.Version.ToString(),
                samples = 7,
                results,
            },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new InvalidOperationException("The measured syntax edit failed its ownership contract.");
        }
    }

    private sealed class OwnershipCheck(Kotonoha module) : KotoVisitor
    {
        internal int Count { get; private set; }

        public override void Visit(Koto node)
        {
            Require(ReferenceEquals(module, node.Kotonoha));
            this.Count++;
            base.Visit(node);
        }
    }
}
