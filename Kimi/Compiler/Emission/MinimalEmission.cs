// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Checked literal-output lowering. Reuses the existing CFG, Symbols, Places and cleanup plans.</summary>
public sealed class MinimalEmission
{
    private static readonly string Runtime = ReadRuntime();
    private readonly Compilation compilation;

    private StringBuilder text => field ??= new(16384);

    private SourceDocument? locationSource;
    private int locationOffset;
    private string? locationText;
    private string? locationDirectory;

    internal MinimalEmission(Compilation compilation) => this.compilation = compilation;

    /// <summary>Verifies the entire selected input against the deliberately small execution subset.</summary>
    /// <param name="failure">A concrete reason when generation cannot proceed.</param>
    /// <returns>Whether the latest analysis proves every operation required by this subset.</returns>
    public bool Validate(out string? failure)
    {
        failure = null;
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout)
        {
            failure = "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }
        else if (!c.Binding.Result.IsComplete || c.Binding.Obligations.Count != 0 || !c.Core.IsValid ||
            c.Kotonoha.HasSourceErrors || c.Kotonoha.DiagnosticCollection.HasErrors || !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            failure = "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }
        else if (c.KotonohaArray.Length != 0 || startup.Kind != StartupKind.Implicit || startup.OutputKind != OutputKind.Application ||
            c.Kotonoha.SourceDocuments.Count != 1 || c.Ownership.Bodies.Count != 1 || c.Kotonoha.RootKoto.NestedContainers.Count != 0)
        {
            failure = "This partial emitter supports one implicit Application document without external modules or declaration containers.";
        }
        else
        {
            var body = c.Ownership.Bodies[0];
            var items = startup.Function!.Body!.Items;
            if (!ReferenceEquals(body.Function, startup.Function) || !body.IsConcrete || !body.IsVerified || items.Count != 1 ||
                items[0] is not InvocationKoto call || !this.IsLiteralCall(call))
            {
                failure = "This partial emitter requires one Core.writeLine call with a string literal; other selected bodies and operations are unsupported.";
                return false;
            }

            // Do not let unused declarations evade the pre-optimization generation gate.
            var members = c.Kotonoha.RootKoto.Members;
            for (var i = 0; i < members.Count; i++)
            {
                if (!ReferenceEquals(members[i], startup.Function) && members[i] is not AliasKoto)
                {
                    failure = "Additional selected implementation bodies are outside the literal-output execution subset.";
                    return false;
                }
            }

            var literal = (StringLiteralKoto)call.ArgumentNodes[0];
            for (var i = 0; i < body.Places.Count; i++)
            {
                var place = body.Places[i];
                if (!ReferenceEquals(place.Type, BoundType.Unit) &&
                    !(ReferenceEquals(place.Type, BoundType.String) && ReferenceEquals(place.Source, literal) && place.Kind == OwnershipPlaceKind.Temporary))
                {
                    failure = "A Place needs unsupported layout, storage or lifetime verification.";
                    return false;
                }
            }

            var calls = 0;
            var entries = 0;
            for (var i = 0; i < body.Operations.Count; i++)
            {
                var op = body.Operations[i];
                switch (op.Kind)
                {
                    case OwnershipOperationKind.Entry:
                    case OwnershipOperationKind.Exit:
                    case OwnershipOperationKind.Deliver:
                    case OwnershipOperationKind.Produce:
                    case OwnershipOperationKind.Cleanup:
                        break;
                    case OwnershipOperationKind.CallEntry when ReferenceEquals(op.Source, call) && op.Place >= 0 &&
                        ReferenceEquals(body.Places[op.Place].Source, literal) &&
                        (body.GetInputState(i, op.Place) & PlaceState.MustInit) != 0:
                        entries++;
                        break;
                    case OwnershipOperationKind.Call when ReferenceEquals(op.Source, call):
                        calls++;
                        break;
                    default:
                        failure = "An ownership operation needs unsupported lowering or Loan/Origin verification.";
                        return false;
                }

                var normals = 0;
                for (var edge = body.EdgeHeads[i]; edge >= 0; edge = body.Edges[edge].Next)
                {
                    var kind = body.Edges[edge].Kind;
                    if (kind == OwnershipEdgeKind.Abort && op.Kind == OwnershipOperationKind.Call)
                    {
                        continue; // The verified Core body never returns on this edge.
                    }

                    if (kind is not (OwnershipEdgeKind.Normal or OwnershipEdgeKind.Return) || ++normals > 1)
                    {
                        failure = "A control-flow edge needs unsupported lowering.";
                        return false;
                    }
                }
            }

            for (var i = 0; i < body.CleanupSteps.Count; i++)
            {
                var step = body.CleanupSteps[i];
                if (step.Action is not (CleanupAction.Skip or CleanupAction.Destroy))
                {
                    failure = "Cleanup is conditional or unsupported.";
                    return false;
                }
            }

            if (calls != 1 || entries != 1)
            {
                failure = "The verified CFG must acquire and invoke the output argument exactly once.";
            }
        }

        return failure is null;
    }

    /// <summary>Writes inspection IR after checking the latest analysis. Does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        if (!this.Validate(out failure))
        {
            return false;
        }

        this.WriteValidatedIr(writer);
        return true;
    }

    internal void WriteValidatedIr(TextWriter writer)
    {
        this.text.Clear();
        var b = this.text;
        var body = this.compilation.Ownership.Bodies[0];
        var call = (InvocationKoto)this.compilation.Binding.StartupItems[0];
        var literal = (StringLiteralKoto)call.ArgumentNodes[0];
        b.Append("; Kimigayo partial literal-output emitter; pre-optimization inspection IR\ntarget triple = \"").Append(WindowsProfile.Target)
            .Append("\"\ntarget datalayout = \"").Append(WindowsProfile.DataLayout).Append("\"\n%kimi.string = type { ptr, i64, i8 }\n@_fltused = global i32 0, align 4\n");
        var length = this.Constant("text", literal.Literal);
        var location = this.Location(call);
        var locationLength = this.Constant("location", location);
        b.Append(Runtime);
        b.Append("\ndefine internal void @__kimi_entry_body() #0 {\nentry:\n");
        for (var p = 0; p < body.Places.Count; p++)
        {
            if (ReferenceEquals(body.Places[p].Type, BoundType.String))
            {
                b.Append("  %p").Append(p).Append(" = alloca %kimi.string, align 8\n");
            }
        }

        b.Append("  br label %op0\n");
        var argument = -1;
        for (var i = 0; i < body.Operations.Count; i++)
        {
            if (!body.IsReachable(i))
            {
                continue;
            }

            var op = body.Operations[i];
            b.Append("op").Append(i).Append(":\n");
            if (op.Kind == OwnershipOperationKind.Produce && op.Place >= 0 && ReferenceEquals(body.Places[op.Place].Type, BoundType.String))
            {
                b.Append("  store %kimi.string { ptr @__kimi_text, i64 ").Append(length).Append(", i8 0 }, ptr %p").Append(op.Place).Append(", align 8\n");
            }
            else if (op.Kind == OwnershipOperationKind.CallEntry)
            {
                argument = op.Place;
            }
            else if (op.Kind == OwnershipOperationKind.Call)
            {
                b.Append("  call void @__kimi_write_line(ptr %p").Append(argument).Append(", ptr @__kimi_location, i64 ").Append(locationLength).Append(")\n");
            }
            else if (op.Kind == OwnershipOperationKind.Cleanup)
            {
                var stepIndex = body.OperationSteps[i];
                if (stepIndex >= 0 && body.CleanupSteps[stepIndex].Action == CleanupAction.Destroy && ReferenceEquals(body.Places[op.Place].Type, BoundType.String))
                {
                    b.Append("  call void @__kimi_destroy_string(ptr %p").Append(op.Place).Append(", ptr @__kimi_location, i64 ").Append(locationLength).Append(")\n");
                }
            }

            var next = -1;
            for (var e = body.EdgeHeads[i]; e >= 0; e = body.Edges[e].Next)
            {
                if (body.Edges[e].Kind != OwnershipEdgeKind.Abort)
                {
                    next = body.Edges[e].To;
                }
            }

            if (next >= 0)
            {
                b.Append("  br label %op").Append(next).Append('\n');
            }
            else
            {
                var incoming = body.IncomingEdges[i];
                b.Append(incoming >= 0 && body.Edges[incoming].Kind == OwnershipEdgeKind.Abort ? "  unreachable\n" : "  ret void\n");
            }
        }

        b.Append("}\ndefine void @__kimi_start() noreturn #0 {\nentry:\n  call void @__kimi_entry_body()\n  call void @__kimi_exit(i32 0)\n  unreachable\n}\n")
            .Append("attributes #0 = { uwtable(async) \"target-cpu\"=\"x86-64\" \"target-features\"=\"+sse2\" \"denormal-fp-math\"=\"ieee,ieee\" }\n")
            .Append("!llvm.module.flags = !{!0}\n!0 = !{i32 8, !\"PIC Level\", i32 2}\n");
        foreach (var chunk in b.GetChunks())
        {
            writer.Write(chunk.Span);
        }
    }

    private static string ReadRuntime()
    {
        using var stream = typeof(MinimalEmission).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.WindowsRuntime.ll")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private bool IsLiteralCall(InvocationKoto call)
    {
        var plan = call.BoundCall;
        return plan is not null && ReferenceEquals(plan.Target, this.compilation.Core.WriteLine) && plan.Receiver is null &&
            plan.TypeArguments.Length == 0 && plan.Origins.Length == 0 && ReferenceEquals(plan.ReturnType, BoundType.Unit) &&
            call.AttributeChain is null && call.ArgumentNodes.Count == 1 && call.ArgumentNodes[0] is StringLiteralKoto { AttributeChain: null } &&
            plan.ArgumentOperations.Length == 1 && plan.ArgumentOperations[0].Kind == ArgumentOperationKind.Value &&
            ReferenceEquals(plan.ArgumentOperations[0].ParameterType, BoundType.String) && plan.ArgumentToParameter[0] == 0;
    }

    private int Constant(string name, string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        this.text.Append("@__kimi_").Append(name).Append(" = private unnamed_addr constant [").Append(length).Append(" x i8] c\"");
        Span<byte> bytes = stackalloc byte[4];
        const string Hex = "0123456789ABCDEF";
        foreach (var rune in value.EnumerateRunes())
        {
            var count = rune.EncodeToUtf8(bytes);
            for (var i = 0; i < count; i++)
            {
                this.text.Append('\\').Append(Hex[bytes[i] >> 4]).Append(Hex[bytes[i] & 15]);
            }
        }

        this.text.Append("\", align 1\n");
        return length;
    }

    private string Location(Koto node)
    {
        var source = node.CodeContext.SourceDocument!;
        var projectDirectory = this.compilation.Project.Directory;
        if (ReferenceEquals(this.locationSource, source) && this.locationOffset == node.Span.Start &&
            this.locationDirectory == projectDirectory && this.locationText is not null)
        {
            return this.locationText;
        }

        var mark = this.text.Length;
        Span<char> hex = stackalloc char[8];
        var logicalPath = Path.IsPathFullyQualified(source.Path) && projectDirectory.Length > 0 ?
            Path.GetRelativePath(Path.GetFullPath(projectDirectory), source.Path) : source.Path;
        foreach (var rune in logicalPath.EnumerateRunes())
        {
            if (rune.Value == '\\')
            {
                this.text.Append("\\\\");
            }
            else if (rune.Value < 32 || rune.Value >= 127)
            {
                rune.Value.TryFormat(hex, out var count, "X", CultureInfo.InvariantCulture);
                this.text.Append("\\u{").Append(hex[..count]).Append('}');
            }
            else
            {
                this.text.Append((char)rune.Value);
            }
        }

        var position = source.GetPosition(node.Span.Start);
        this.text.Append(':').Append(position.Line + 1).Append(':').Append(position.Character + 1);
        var result = this.text.ToString(mark, this.text.Length - mark);
        this.text.Length = mark;
        this.locationSource = source;
        this.locationOffset = node.Span.Start;
        this.locationDirectory = projectDirectory;
        return this.locationText = result;
    }
}
