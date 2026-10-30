// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The native allocation audit fails closed: every spelling of an allocation and every mentioned transfer routine
/// is counted, and a reference the interposer cannot redirect, an unknown declaration, a routine without its anchor or an
/// anchor without its routine, a bound with nothing to count or a count outside its bound fails instead of escaping the
/// counters.</summary>
public class NativeAllocationAuditTest
{
    // One growth from empty: one 16-byte allocation and no copy.
    private const string Append = "var values: Array<i32> = []\nvalues@uniq.append(1)";

    // Two growths (16 and 32 bytes); the second copies four elements (16 bytes).
    private const string Growth = "var values: Array<i32> = []\nvar i = 0\nwhile i < 5\n    values@uniq.append(i)\n    i += 1";

    // Two Dictionary storages (96 and 192 bytes); the growth transfers the four occupied slots (96 bytes).
    private const string Entries = "var entries: Dictionary<i32, i32> = [:]\nvar i = 0\nwhile i < 5\n    _ = entries.tryInsert(i, i)\n    i += 1";

    // An exit that bypasses __kimi_exit, as a generator calling the import directly would write it.
    private const string DirectExit = "define internal void @audit_direct_exit() {\nentry:\n  call void @ExitProcess(i32 0)\n  unreachable\n}\n";

    [Theory]
    [InlineData("Control", "call ptr @HeapAlloc(")]
    [InlineData("TailNoalias", "tail call noalias ptr @HeapAlloc(")]
    public void EveryAllocationSpellingIsCounted(string name, string spelling)
        => Audit(name, Append, 1, 16, transformIr: ir =>
        {
            Assert.Contains("call ptr @HeapAlloc(", ir, StringComparison.Ordinal); // Every runtime allocation site, respelled.
            return ir.Replace("call ptr @HeapAlloc(", spelling, StringComparison.Ordinal);
        });

    // A count below or above its bound fails the run.
    [Theory]
    [InlineData("BelowBound", 2L, 3L)]
    [InlineData("AboveBound", 0L, 0L)]
    public void CountOutsideItsBoundFails(string name, long minimum, long maximum)
        => NativeAllocationAudit.WriteFixture("NativeAllocationAudit" + name, Append, new(minimum, maximum), 1, 16, exit: 93, stderr: string.Empty);

    [Theory]
    [InlineData("HeapAlloc")]
    [InlineData("HeapFree")]
    [InlineData("__kimi_exit")]
    public void UnredirectedReferenceFails(string symbol)
    {
        var stray = "@audit_stray = private constant ptr @" + symbol + "\n";
        Assert.Throws<DoesNotContainException>(() => Audit("Stray", Append, 1, 16, transformIr: ir => ir + stray));
    }

    [Fact]
    public void DirectExitFails()
        => Assert.Throws<DoesNotContainException>(() => Audit("Exit", Append, 1, 16, transformIr: ir => ir + DirectExit));

    [Theory]
    [InlineData("declare dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    [InlineData("declare dllimport ptr @VirtualAlloc(ptr, i64, i32, i32)")]
    [InlineData("declare dllimport ptr @RtlAllocateHeap(ptr, i32, i64)")]
    [InlineData("declare extern_weak dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    [InlineData("declare ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    public void UnknownDeclarationFails(string declaration)
        => Assert.Throws<ContainsException>(() => Audit("Declaration", Append, 1, 16, transformIr: ir => ir + declaration + "\n"));

    [Fact]
    public void ArrayGrowthUnderABoundIsCounted()
        => Audit("Growth", Growth, 2, 48, transferred: 16);

    [Fact]
    public void RespelledTransferRoutineIsCounted()
        => Audit("Transfer", Entries, 2, 288, transferred: 96, transformIr: Respell);

    [Fact]
    public void TransferRoutineWithoutItsAnchorFails()
        => Assert.Throws<EqualException>(() => Audit("Unanchored", Entries, 2, 288, transferred: 96, transformIr: Unanchor));

    // A renamed growth routine whose body keeps its anchor would leave its copies uncounted beside the transfer routine.
    [Fact]
    public void AnchorWithoutItsRoutineFails()
        => Assert.Throws<EqualException>(() => Audit("Renamed", Entries, 2, 288, transferred: 96, transformIr: Rename));

    // DECISIONS R5-M145: the byte copy written inline, as MIR code generation wrote it before both generators called one routine, leaves a
    // transferring program's copies uncounted, so even a bound whose minimum is 0 fails instead of passing vacuously, also when the routine
    // stays defined beside the inline copies (a module that defines the storage byte primitives as a pair).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnInlineByteCopyFails(bool defined)
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Inline", Entries, 2, 288, transferred: new(0, 96), transformIr: defined ? InlineCalls : Inline));
        Assert.Contains("byte copy", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundWithoutTransferRoutineFails()
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Uncounted", "let n = 1", 0, 0, transferred: 0));
        Assert.Contains("transfer bound", failure.Message, StringComparison.Ordinal);
    }

    // Audits source as fixture NativeAllocationAudit<name> with as many releases as allocations.
    private static void Audit(
        string name,
        string source,
        int allocations,
        long bytes,
        NativeAllocationAudit.Bound? transferred = null,
        Func<string, string>? transformIr = null)
        => NativeAllocationAudit.WriteFixture(
            "NativeAllocationAudit" + name,
            source,
            allocations,
            allocations,
            bytes,
            transferred: transferred,
            transformIr: transformIr);

    // Another spelling of the transfer routine's definition, as a different generator might write it. A missing definition fails with a
    // ContainsException, not the EqualException TransferRoutineWithoutItsAnchorFails expects through Unanchor.
    private static string Respell(string ir)
    {
        const string Definition = "define internal void @__kimi_transfer_bytes(";
        Assert.Contains(Definition, ir, StringComparison.Ordinal);
        return ir.Replace(Definition, "define internal void @\"__kimi_transfer_bytes\"(", StringComparison.Ordinal);
    }

    // The respelled routine without its anchor: the copy is a memmove, as lowering MoveBytes by its contract would write it.
    private static string Unanchor(string ir)
        => Respell(ir).Replace("@llvm.memcpy.p0.p0.i64(ptr %destination", "@llvm.memmove.p0.p0.i64(ptr %destination", StringComparison.Ordinal);

    // The byte copy's calls written as the intrinsic they call, its definition removed.
    private static string Inline(string ir)
    {
        var definition = Regex.Match(ir, @"(?ms)^define internal void @__kimi_transfer_bytes\(.*?^\}\n").Value;
        Assert.NotEmpty(definition);
        return InlineCalls(ir.Replace(definition, string.Empty, StringComparison.Ordinal));
    }

    // The byte copy's calls written as the intrinsic they call, its definition kept.
    private static string InlineCalls(string ir)
    {
        var inlined = Regex.Replace(ir, @"call void @__kimi_transfer_bytes\(([^)]*)\)", "call void @llvm.memcpy.p0.p0.i64($1, i1 false)");
        Assert.NotEqual(ir, inlined);
        return inlined;
    }

    // The growth routine under another name with its body unchanged.
    private static string Rename(string ir)
        => ir.Replace("__kimi_array_grow", "__kimi_array_enlarge", StringComparison.Ordinal);
}
