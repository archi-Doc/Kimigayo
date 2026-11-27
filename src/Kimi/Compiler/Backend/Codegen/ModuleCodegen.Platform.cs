// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The platform part of ModuleCodegen's frame (IMPL 21.6; the Tatara plan's decision 30, B9d2), on a row whose program supplies its
// platform (TargetProfile.ProgramPlatform; nothing on another row), written by Start and placed by Assemble after the definitions and
// before the start. An Application's (PlatformAdapters): its storage @__kimi_platform (PlatformStorage: zero-filled, of the platform
// Type's layout, the result of the receiverless row's witness, `start`; never destroyed, as Self is Copy) and one adapter per PlatformRows
// row, defined by its record (RuntimeAbi.PlatformMember*), which calls the row's witness, roots 1 to 6 (Compilation.OutputRoots,
// positional; Compilation.PlatformRoots checks their count, so the span is trusted), through FunctionCodegen's entry call in the adapter it
// begins. Write reads the witnesses once, before any item (Witnesses), so each witness's signature carries Abi.Flags.PlatformMember and
// FunctionCodegen's frame brackets its every run, whoever calls it (IMPL 21.6.3). Each argument's source is chosen by the row and the
// witness's signature: the storage for a receiver and a result slot; a Text.Utf8Slice owned slot written by FunctionCodegen
// (WriteTextSlot) from the adapter's next two parameters; another value or owned slot the adapter's next parameter, of its Type. The
// call's value is returned unchanged (the template maps it), void returns, a noreturn call is followed by `unreachable` (FunctionCodegen's
// End). A Library's (PlatformDeclarations, for inspection): the six adapters' declarations, so the template's calls resolve: one module
// cannot both declare and define a symbol. A witness parameter without a source (of another kind, a Text.Utf8Slice slot without the
// adapter's two parameters, or a pass-through of another Type than the adapter parameter's), an adapter parameter left over, or a result
// or noreturn other than the adapter's is a broken contract at the witness's site.
internal sealed partial class ModuleCodegen
{
    /// <summary>The platform storage, the program's platform value (IMPL 21.6), spelled by FunctionCodegen's Value prefix P.</summary>
    internal const string PlatformStorage = "__kimi_platform";

    private readonly StringWriter platformText = new(CultureInfo.InvariantCulture); // The platform part of the latest Write (Start).
    private readonly int[] witnesses = new int[PlatformRows.Rows.Length]; // The latest Write's witnesses' items, by row (-1: none).

    // The witnesses' items, Binding's PlatformWitnesses, into witnesses before any item is written: roots 1 to 6, in PlatformRows' order,
    // exactly when Binding published a platform (StartupResult.Platform, null on a hosted row, in a test build and in a Library), the
    // fact on which Compilation.OutputRoots appends them (positional; Compilation.PlatformRoots checks their count, so the span is
    // trusted); -1 otherwise. It is the one place ModuleCodegen reads roots 1 to 6 by position: Signature marks each witness's body
    // (Abi.Flags.PlatformMember) and PlatformAdapters calls it.
    private void Witnesses(in StartupResult startup, ReadOnlySpan<int> roots)
    {
        this.witnesses.AsSpan().Fill(-1);
        if (startup.Platform is not null)
        {
            roots[1..].CopyTo(this.witnesses);
        }
    }

    // A Library's platform part into platformText: the adapters' declarations.
    private void PlatformDeclarations()
    {
        var text = this.platformText;
        if (!this.row.ProgramPlatform)
        {
            return;
        }

        foreach (var row in PlatformRows.Rows)
        {
            text.Write("declare ");
            WriteDeclaration(text, row.Adapter);
        }
    }

    // An Application's platform part into platformText: the storage and the adapters over the witnesses' items, one per PlatformRows row.
    private void PlatformAdapters()
    {
        if (!this.row.ProgramPlatform)
        {
            return;
        }

        for (var i = 0; i < PlatformRows.Rows.Length; i++)
        {
            this.MemberAdapter(PlatformRows.Rows[i], this.witnesses[i]);
        }
    }

    // A row's adapter over its witness's item (and, before the receiverless row's, the storage, of its witness's result).
    private void MemberAdapter(in PlatformRow row, int item)
    {
        var body = this.program.Body(this.set.Items[item].BodyOrdinal);
        if (this.Signature(item) is not { } witness || this.Inputs(item, body, out _) is not { } result)
        {
            return; // Recorded.
        }

        var (text, site) = (this.platformText, this.set.Items[item].Site);
        if (!row.Receiver && !this.Storage(result, site))
        {
            return;
        }

        this.bodies.BeginFrameFunction(text, row.Adapter);
        text.Write(row.Adapter.GetDefinition(exported: false));
        text.Write("entry:\n");
        Span<FunctionCodegen.Value> arguments = stackalloc FunctionCodegen.Value[witness.Parameters.Length];
        var next = 0; // The adapter's next parameter.
        var given = row.Adapter.Parameters.Length;
        for (var p = 0; p < arguments.Length; p++)
        {
            var parameter = witness.Parameters[p];
            if (parameter.Kind == AbiParameterKind.ResultSlot || (row.Receiver && parameter.LogicalIndex == 0))
            {
                arguments[p] = new('P', 0);
            }
            else if (parameter.Kind == AbiParameterKind.OwnedSlot &&
                this.parameters[parameter.LogicalIndex] is { Symbol.LibraryDeclaration: KimiDeclarationId.Utf8Slice } slice)
            {
                if (next + 1 >= given)
                {
                    next = -1; // No source: the slot's data and length.
                    break;
                }

                arguments[p] = this.bodies.WriteTextSlot(slice, new('p', next), new('p', next + 1));
                next += 2;
            }
            else if (parameter.Kind is AbiParameterKind.Value or AbiParameterKind.OwnedSlot && next < given &&
                parameter.Type == row.Adapter.Parameters[next].Type)
            {
                arguments[p] = new('p', next++);
            }
            else
            {
                next = -1; // No source.
                break;
            }
        }

        if (next != given || witness.Result != row.Adapter.Result || witness.NoReturn != row.Adapter.NoReturn)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return;
        }

        this.bodies.End(witness, this.bodies.WriteEntryCall(witness, arguments));
    }

    // The platform storage, of the platform Type's layout (its size in bytes and its alignment); false, recorded, without one.
    private bool Storage(BoundType platform, SourceRef site)
    {
        if (this.Layout.Of(platform).Value is not { } value)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return false;
        }

        var text = this.platformText;
        text.Write('@');
        text.Write(PlatformStorage);
        text.Write(" = internal global [");
        WriteNumber(text, value.Layout.Size);
        text.Write(" x i8] zeroinitializer, align ");
        WriteNumber(text, value.Layout.Alignment);
        text.Write('\n');
        return true;
    }
}
