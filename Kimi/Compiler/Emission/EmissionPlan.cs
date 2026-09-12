// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Compact, compilation-local lowering records.

internal readonly record struct EmissionSlot(int Place, ValueLowering Value);

internal readonly record struct EmissionInstruction(int Operation, int Place, FunctionAbi? Callee);

/// <summary>A reusable, closed plan. No semantic analysis is performed by the LLVM writer.</summary>
internal sealed class EmissionPlan
{
    private readonly LlvmModuleWriter writer = new();
    private readonly StringBuilder locationBuffer = new();
    private byte[] visited = [];
    private SourceDocument? locationSource;
    private int locationOffset;
    private string? locationText;
    private string? locationDirectory;
    private bool complete;

    internal List<EmissionSlot> Slots { get; } = new();

    internal List<EmissionInstruction> Instructions { get; } = new();

    internal LlvmConstant Literal { get; } = new("__kimi_text");

    internal LlvmConstant Location { get; } = new("__kimi_location");

    internal void Clear()
    {
        this.complete = false;
        this.Slots.Clear();
        this.Instructions.Clear();
    }

    internal bool Lower(OwnershipBody body, InvocationKoto call, string directory, out string? failure)
    {
        failure = "The verified CFG needs unsupported control flow, argument acquisition, or cleanup lowering.";
        var count = body.Operations.Count;
        if (count == 0 || body.Operations[0].Kind != OwnershipOperationKind.Entry)
        {
            return false;
        }

        if (this.visited.Length < count)
        {
            Array.Resize(ref this.visited, Math.Max(count, this.visited.Length * 2));
        }

        this.visited.AsSpan(0, count).Clear();
        // Bit 4 records coverage by an existing edge cleanup plan; bits 1/2 record
        // normal/abort reachability. Reuse one scratch buffer without copying plans.
        for (var p = 0; p < body.CleanupPlans.Count; p++)
        {
            var cleanup = body.CleanupPlans[p];
            if ((uint)cleanup.Edge >= (uint)body.Edges.Count || cleanup.Count <= 0 || cleanup.Start < 0 ||
                cleanup.Start > body.CleanupSteps.Count - cleanup.Count)
            {
                return false;
            }

            for (var s = cleanup.Start; s < cleanup.Start + cleanup.Count; s++)
            {
                var step = body.CleanupSteps[s];
                if ((uint)step.Operation >= (uint)count || body.Operations[step.Operation].Kind != OwnershipOperationKind.Cleanup ||
                    body.OperationSteps[step.Operation] != s || (this.visited[step.Operation] & 4) != 0)
                {
                    return false;
                }

                this.visited[step.Operation] |= 4;
            }

            if (body.Edges[cleanup.Edge].To != body.CleanupSteps[cleanup.Start].Operation)
            {
                return false;
            }
        }

        for (var p = 0; p < body.Places.Count; p++)
        {
            var value = WindowsLowering.GetValue(body.Places[p].Type);
            if (value is null)
            {
                return false;
            }

            if (value.Layout.Size != 0)
            {
                this.Slots.Add(new(p, value));
            }
        }

        var argument = -1;
        var produced = -1;
        var invoked = false;
        var cursor = 0;
        while (true)
        {
            if ((uint)cursor >= (uint)count || (this.visited[cursor] & 3) != 0 || !body.IsReachable(cursor))
            {
                return false;
            }

            this.visited[cursor] |= 1;
            var op = body.Operations[cursor];
            switch (op.Kind)
            {
                case OwnershipOperationKind.Entry when cursor == 0:
                case OwnershipOperationKind.Deliver:
                case OwnershipOperationKind.Exit:
                    break;
                case OwnershipOperationKind.Produce:
                    if (op.Place < 0)
                    {
                        return false;
                    }

                    if (ReferenceEquals(body.Places[op.Place].Type, BoundType.String))
                    {
                        if (produced >= 0)
                        {
                            return false;
                        }

                        produced = op.Place;
                        this.Instructions.Add(new(cursor, op.Place, null));
                    }

                    break;
                case OwnershipOperationKind.CallEntry:
                    if (argument >= 0 || produced != op.Place || invoked)
                    {
                        return false;
                    }

                    argument = op.Place;
                    break;
                case OwnershipOperationKind.Call:
                    if (argument < 0 || invoked)
                    {
                        return false;
                    }

                    this.Instructions.Add(new(cursor, argument, WindowsLowering.WriteLine));
                    invoked = true;
                    break;
                case OwnershipOperationKind.Cleanup:
                    var stepIndex = body.OperationSteps[cursor];
                    if (stepIndex < 0 || (this.visited[cursor] & 4) == 0)
                    {
                        return false;
                    }

                    var step = body.CleanupSteps[stepIndex];
                    if (step.Operation != cursor || step.Place != op.Place)
                    {
                        return false;
                    }

                    if (step.Action == CleanupAction.Destroy && ReferenceEquals(body.Places[op.Place].Type, BoundType.String))
                    {
                        this.Instructions.Add(new(cursor, op.Place, WindowsLowering.DestroyString));
                    }

                    break;
                default:
                    return false;
            }

            var next = -1;
            for (var e = body.EdgeHeads[cursor]; e >= 0; e = body.Edges[e].Next)
            {
                var edge = body.Edges[e];
                if (edge.Kind == OwnershipEdgeKind.Abort)
                {
                    // Core's abort is terminal inside the callee. It is not a caller continuation.
                    if (body.Operations[edge.To].Kind != OwnershipOperationKind.Exit || body.EdgeHeads[edge.To] >= 0 || (this.visited[edge.To] & 1) != 0)
                    {
                        return false;
                    }

                    this.visited[edge.To] |= 2;
                }
                else
                {
                    next = edge.To;
                }
            }

            if (op.Kind == OwnershipOperationKind.Exit)
            {
                if (next >= 0 || !invoked)
                {
                    return false;
                }

                break;
            }

            cursor = next; // A missing successor is an error, never an invented ret/unreachable.
        }

        for (var i = 0; i < count; i++)
        {
            if ((body.IsReachable(i) && (this.visited[i] & 3) == 0) ||
                (body.Operations[i].Kind == OwnershipOperationKind.Cleanup && (this.visited[i] & 4) == 0))
            {
                return false;
            }
        }

        this.Literal.SetValue(((StringLiteralKoto)call.ArgumentNodes[0]).Literal);
        this.Location.SetValue(this.GetLocation(call, directory));
        this.complete = true;
        failure = null;
        return true;
    }

    internal void WriteIr(TextWriter output)
    {
        if (!this.complete)
        {
            throw new InvalidOperationException("LLVM writing requires a complete emission plan.");
        }

        this.writer.Write(this, output);
    }

    private string GetLocation(Koto node, string projectDirectory)
    {
        var source = node.CodeContext.SourceDocument!;
        if (ReferenceEquals(this.locationSource, source) && this.locationOffset == node.Span.Start &&
            this.locationDirectory == projectDirectory && this.locationText is not null)
        {
            return this.locationText;
        }

        var text = this.locationBuffer;
        text.Clear();
        Span<char> hex = stackalloc char[8];
        var logicalPath = Path.IsPathFullyQualified(source.Path) && projectDirectory.Length > 0 ?
            Path.GetRelativePath(Path.GetFullPath(projectDirectory), source.Path) : source.Path;
        foreach (var rune in logicalPath.EnumerateRunes())
        {
            if (rune.Value == '\\')
            {
                text.Append("\\\\");
            }
            else if (rune.Value < 32 || rune.Value >= 127)
            {
                rune.Value.TryFormat(hex, out var count, "X", CultureInfo.InvariantCulture);
                text.Append("\\u{").Append(hex[..count]).Append('}');
            }
            else
            {
                text.Append((char)rune.Value);
            }
        }

        var position = source.GetPosition(node.Span.Start);
        text.Append(':').Append(position.Line + 1).Append(':').Append(position.Character + 1);
        this.locationSource = source;
        this.locationOffset = node.Span.Start;
        this.locationDirectory = projectDirectory;
        return this.locationText = text.ToString();
    }
}
