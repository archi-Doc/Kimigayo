// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The handle-mode-specific text: rc and arc clone and drop, their release transitions, and a Sealed handle's direct release; the clone
// and release routines' signatures and a factory's control value by mode.
internal static partial class ObjectRuntime
{
    private static readonly FunctionAbi CloneRc = Retain("__kimi_clone_rc");

    private static readonly FunctionAbi CloneArc = Retain("__kimi_clone_arc");

    private static readonly FunctionAbi DropObject = Released("__kimi_drop_object");

    private static readonly FunctionAbi DropRc = Released("__kimi_drop_rc");

    private static readonly FunctionAbi DropArc = Released("__kimi_drop_arc");

    /// <summary>Gets the retain routine of a counted handle's mode, rc or arc (SPEC 13.6.2: clone), which WriteRcObjects and WriteArcObjects
    /// write: its result slot, the borrowed handle slot and the clone's location, at which a count at the maximum Aborts.</summary>
    /// <param name="counting">The mode's counting, NonAtomic or Atomic.</param>
    /// <returns>The signature.</returns>
    internal static FunctionAbi Clone(ObjectCountingStep counting) => counting == ObjectCountingStep.Atomic ? CloneArc : CloneRc;

    /// <summary>Gets the release of an owning handle's mode through its descriptor's finalizer (SHARED_OBJECT_RUNTIME, Release), which
    /// WriteRuntime writes: obj's <c>__kimi_drop_object</c>, rc's and arc's counted transitions before it; each takes the handle slot and
    /// the location a failure inside the release reports.</summary>
    /// <param name="counting">The mode's counting.</param>
    /// <returns>The signature.</returns>
    internal static FunctionAbi Release(ObjectCountingStep counting) => counting switch
    {
        ObjectCountingStep.None => DropObject,
        ObjectCountingStep.NonAtomic => DropRc,
        _ => DropArc,
    };

    /// <summary>Gets the static control value a factory stores for a handle's mode (SHARED_OBJECT_RUNTIME, Creation): none for obj, and
    /// for a counted mode the even encoding of one strong reference.</summary>
    /// <param name="counting">The mode's counting.</param>
    /// <returns>0 or 2.</returns>
    internal static int InitialControl(ObjectCountingStep counting) => counting == ObjectCountingStep.None ? 0 : 2;

    // The final release of a Sealed handle (IMPL 21.1.3) into a drop routine whose %slot holds it: the counted release transition
    // of rc or arc, then the payload's drop routine, named by its symbol (empty for a payload without one), then the free.
    internal static void WriteSealedDrop(TextWriter output, ObjectCountingStep counting, ReadOnlySpan<char> payloadDrop)
    {
        output.Write("  %header = load ptr, ptr %slot, align 8\n");
        if (counting == ObjectCountingStep.NonAtomic)
        {
            WriteRcReleaseTransition(output);
        }
        else if (counting == ObjectCountingStep.Atomic)
        {
            WriteArcReleaseTransition(output);
        }

        if (!payloadDrop.IsEmpty)
        {
            output.Write("  %payload = getelementptr i8, ptr %header, i64 16\n  call void @");
            output.Write(payloadDrop);
            output.Write("(ptr %payload, ptr %location, i64 %length)\n");
        }

        // Every current factory uses this allocator. Destruction may Abort; free follows only its normal return.
        output.Write("  call void @__kimi_free(ptr %header, ptr %location, i64 %length)\n");
        if (counting != ObjectCountingStep.None)
        {
            output.Write("  br label %done\ndone:\n");
        }
    }

    // A release routine's signature, the parameters its text names.
    private static FunctionAbi Released(string name) => new(
        name,
        "void",
        [
            new("ptr", "slot", AbiParameterKind.OwnedSlot, 0), new("ptr", "location", AbiParameterKind.Location),
            new("i64", "length", AbiParameterKind.LocationLength),
        ]);

    // A retain routine's signature, the parameters its text names.
    private static FunctionAbi Retain(string name) => new(
        name,
        "void",
        [
            new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "value", AbiParameterKind.Value, 0),
            new("ptr", "location", AbiParameterKind.Location), new("i64", "length", AbiParameterKind.LocationLength),
        ])
    {
        ResultSlot = true,
    };

    private static void WriteRcReleaseTransition(TextWriter output)
        => output.Write("""
              %control = getelementptr i8, ptr %header, i64 8
              %old = load i64, ptr %control, align 8
              %next = sub i64 %old, 2
              store i64 %next, ptr %control, align 8
              %final = icmp eq i64 %old, 2
              br i1 %final, label %destroy, label %done
            destroy:

            """);

    // Weak and cyclic factories remain closed: every reachable control is inline and even. The complete word is
    // compared and the observed value is retried; opening migration must add representation dispatch at the retry.
    private static void WriteArcReleaseTransition(TextWriter output)
        => output.Write("""
              %control = getelementptr i8, ptr %header, i64 8
              %initial = load atomic i64, ptr %control monotonic, align 8
              br label %release
            release:
              %old = phi i64 [ %initial, %entry ], [ %observed, %release ]
              %next = sub i64 %old, 2
              %exchange = cmpxchg ptr %control, i64 %old, i64 %next release monotonic, align 8
              %observed = extractvalue { i64, i1 } %exchange, 0
              %updated = extractvalue { i64, i1 } %exchange, 1
              br i1 %updated, label %released, label %release
            released:
              %final = icmp eq i64 %old, 2
              br i1 %final, label %destroy, label %done
            destroy:
              fence acquire

            """);

    private static void WriteArcObjects(TextWriter output)
    {
        output.Write("""
            define internal void @__kimi_clone_arc(ptr %ret, ptr %value, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %value, align 8
              %control = getelementptr i8, ptr %header, i64 8
              %initial = load atomic i64, ptr %control monotonic, align 8
              br label %check
            check:
              %old = phi i64 [ %initial, %entry ], [ %observed, %retain ]
              %maximum = icmp eq i64 %old, -2
              br i1 %maximum, label %overflow, label %retain
            retain:
              %next = add i64 %old, 2
              %exchange = cmpxchg ptr %control, i64 %old, i64 %next monotonic monotonic, align 8
              %observed = extractvalue { i64, i1 } %exchange, 0
              %updated = extractvalue { i64, i1 } %exchange, 1
              br i1 %updated, label %publish, label %check
            publish:
              store ptr %header, ptr %ret, align 8
              ret void
            overflow:
              call void @__kimi_abort(i32
            """);
        output.Write(' ');
        WriteNumber(output, AbortReasons.ReferenceCount);
        output.Write("""
            , ptr %location, i64 %length, i64 -2)
              unreachable
            }
            define internal void @__kimi_drop_arc(ptr %slot, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %slot, align 8

            """);
        WriteArcReleaseTransition(output);
        output.Write("""
              call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)
              br label %done
            done:
              ret void
            }

            """);
    }

    // IMPL 21.1.3: rc remains inline until Weak operations become executable. Clone changes only the count;
    // final release stores zero before destruction, and never accesses the header after finalization.
    private static void WriteRcObjects(TextWriter output)
    {
        output.Write("""
            define internal void @__kimi_clone_rc(ptr %ret, ptr %value, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %value, align 8
              %control = getelementptr i8, ptr %header, i64 8
              %old = load i64, ptr %control, align 8
              %maximum = icmp eq i64 %old, -2
              br i1 %maximum, label %overflow, label %retain
            retain:
              %next = add i64 %old, 2
              store i64 %next, ptr %control, align 8
              store ptr %header, ptr %ret, align 8
              ret void
            overflow:
              call void @__kimi_abort(i32
            """);
        output.Write(' ');
        WriteNumber(output, AbortReasons.ReferenceCount);
        output.Write("""
            , ptr %location, i64 %length, i64 -2)
              unreachable
            }
            define internal void @__kimi_drop_rc(ptr %slot, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %slot, align 8

            """);
        WriteRcReleaseTransition(output);
        output.Write("""
              call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)
              br label %done
            done:
              ret void
            }

            """);
    }
}
