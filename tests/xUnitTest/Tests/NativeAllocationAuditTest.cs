// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The native allocation audit fails closed: every spelling of an allocation and every mentioned transfer routine
/// is counted, and a reference the interposer cannot redirect, an unknown import, a routine without its anchor or a bound
/// with nothing to count fails instead of escaping the counters.</summary>
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
        => Audit(name, Append, 1, 16, transformIr: ir => ir.Replace("call ptr @HeapAlloc(", spelling, StringComparison.Ordinal));

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
    [InlineData("HeapReAlloc")]
    [InlineData("VirtualAlloc")]
    [InlineData("RtlAllocateHeap")]
    public void UnknownImportFails(string import)
        => Assert.Throws<ContainsException>(() => Audit("Import", Append, 1, 16, transformIr: ir => ir + "declare dllimport ptr @" + import + "(ptr)\n"));

    [Fact]
    public void ArrayGrowthUnderABoundIsCounted()
        => Audit("Growth", Growth, 2, 48, bound: 16);

    [Fact]
    public void RespelledTransferRoutineIsCounted()
        => Audit("Transfer", Entries, 2, 288, bound: 96, transformIr: Respell);

    [Fact]
    public void TransferRoutineWithoutItsAnchorFails()
        => Assert.Throws<EqualException>(() => Audit("Unanchored", Entries, 2, 288, bound: 96, transformIr: Unanchor));

    [Fact]
    public void BoundWithoutTransferRoutineFails()
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Uncounted", "let n = 1", 0, 0, bound: 0));
        Assert.Contains("transfer bound", failure.Message, StringComparison.Ordinal);
    }

    // Audits source as fixture NativeAllocationAudit<name> with as many releases as allocations and, under a bound, exactly
    // that many transferred bytes.
    private static void Audit(string name, string source, int allocations, long bytes, long bound = -1, Func<string, string>? transformIr = null)
        => NativeAllocationAudit.WriteFixture(
            "NativeAllocationAudit" + name,
            source,
            allocations,
            allocations,
            bytes,
            maxTransferredBytes: bound,
            minTransferredBytes: Math.Max(bound, 0),
            transformIr: transformIr);

    // Another spelling of the transfer routine's definition, as a different generator might write it.
    private static string Respell(string ir)
        => ir.Replace("define internal void @__kimi_transfer_bytes(", "define internal void @\"__kimi_transfer_bytes\"(", StringComparison.Ordinal);

    // The respelled routine without its anchor: the copy is a memmove, as lowering MoveBytes by its contract would write it.
    private static string Unanchor(string ir)
        => Respell(ir).Replace("@llvm.memcpy.p0.p0.i64(ptr %destination", "@llvm.memmove.p0.p0.i64(ptr %destination", StringComparison.Ordinal);
}
