// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Kimi.Testing;

internal static class TestWorker
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task Run(string executable, string directory, string runDirectory, int index, TestSettings settings, SortedDictionary<string, string> snapshot, TestOutcome outcome, TestBudget budget, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "kimi-test-" + Guid.NewGuid().ToString("N"));
        var environment = new SortedDictionary<string, string>(snapshot, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in settings.Environment)
        {
            environment[name] = value;
        }

        environment["TMP"] = temporary;
        environment["TEMP"] = temporary;
        environment["KIMI_TEST_CASE"] = index.ToString(CultureInfo.InvariantCulture);
        environment["KIMI_TEST_LIMIT"] = settings.DiagnosticCount.ToString(CultureInfo.InvariantCulture);
        environment["KIMI_TEST_BYTES"] = settings.DiagnosticBytes.ToString(CultureInfo.InvariantCulture);
        environment["KIMI_TEST_TEMP"] = Convert.ToHexString(Encoding.UTF8.GetBytes(temporary));
        var started = Stopwatch.GetTimestamp();
        var output = Path.Combine(runDirectory, outcome.Test.CaseId + ".stdout");
        var error = Path.Combine(runDirectory, outcome.Test.CaseId + ".stderr");
        var recoveryStarted = 0L;
        var grace = TestSettings.Duration(settings.RecoveryGrace);
        using var ioCancellation = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(temporary);
            using var child = TestProcess.Start(executable, directory, environment);
            outcome.Started = true;
            var channel = Receive(child.Result, outcome, settings, budget, ioCancellation.Token);
            var stdout = Drain(child.Stdout, output, settings.LogBytes, budget, ioCancellation.Token);
            var stderr = Drain(child.Stderr, error, settings.LogBytes, budget, ioCancellation.Token);
            var deadline = TestSettings.Duration(settings.Timeout);
            while (!child.Wait(0))
            {
                if (cancellationToken.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= deadline || channel.IsFaulted || stdout.IsFaulted || stderr.IsFaulted)
                {
                    outcome.Termination = cancellationToken.IsCancellationRequested ? "cancelled" : Stopwatch.GetElapsedTime(started) >= deadline ? "timeout" : "runnerError";
                    break;
                }

                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
            }

            if (outcome.Termination == "notStarted")
            {
                outcome.Termination = child.ExitCode == 0 ? "normal" : "crash";
            }

            if (child.Wait(0))
            {
                outcome.NativeExitCode = child.ExitCode;
            }

            // Kill remaining descendants even when the selected case exited normally.
            child.Kill();
            recoveryStarted = Stopwatch.GetTimestamp();
            var drains = Task.WhenAll(channel, stdout, stderr);
            while ((!child.IsRecovered || !drains.IsCompleted) && Stopwatch.GetElapsedTime(recoveryStarted) < grace)
            {
                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
            }

            if (!child.IsRecovered || !drains.IsCompleted)
            {
                outcome.Errors.Add("Case recovery deadline expired.");
                ioCancellation.Cancel();
                _ = drains.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                try
                {
                    await drains.ConfigureAwait(false);
                    outcome.OmittedLogBytes = stdout.Result + stderr.Result;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    outcome.Errors.Add(ex.Message);
                }
            }

            if (outcome.Termination == "normal" && !outcome.Completed)
            {
                outcome.Errors.Add("A zero process exit without a valid completion record is not a successful test.");
            }
            else if (!outcome.HandshakeCompleted && outcome.Termination is not ("timeout" or "cancelled"))
            {
                outcome.Errors.Add("The child did not establish its assigned test identity.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException or TimeoutException)
        {
            outcome.Errors.Add(ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    if (recoveryStarted == 0)
                    {
                        recoveryStarted = Stopwatch.GetTimestamp();
                    }

                    var remaining = grace - Stopwatch.GetElapsedTime(recoveryStarted);
                    var deletion = Task.Run(() => Directory.Delete(temporary, true), CancellationToken.None);
                    _ = deletion.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    await deletion.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                outcome.Errors.Add("Temporary directory recovery failed: " + ex.Message);
            }

            outcome.Microseconds = (long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000);
            if (outcome.ExitCode != 0)
            {
                outcome.Stdout = File.Exists(output) ? output : null;
                outcome.Stderr = File.Exists(error) ? error : null;
            }
            else
            {
                try
                {
                    File.Delete(output);
                    File.Delete(error);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    outcome.Errors.Add("Log cleanup failed: " + ex.Message);
                    outcome.Stdout = File.Exists(output) ? output : null;
                    outcome.Stderr = File.Exists(error) ? error : null;
                }
            }
        }
    }

    internal static async Task Receive(Stream input, TestOutcome outcome, TestSettings settings, TestBudget budget, CancellationToken cancellationToken = default)
    {
        var frame = new byte[65536];
        var state = 0;
        var retainedBytes = 0;
        ulong lastIssue = 0;
        while (true)
        {
            var first = await input.ReadAsync(frame.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (first == 0)
            {
                return;
            }

            await input.ReadExactlyAsync(frame.AsMemory(first, 4 - first), cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(frame);
            if (length is < 32 or > 65536)
            {
                throw new InvalidDataException("Invalid test frame length.");
            }

            await input.ReadExactlyAsync(frame.AsMemory(4, length - 4), cancellationToken).ConfigureAwait(false);
            var header = frame.AsSpan(0, 32);
            var version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            var issue = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
            var site = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
            var phase = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
            var count = BinaryPrimitives.ReadUInt64LittleEndian(header[24..]);
            if (version != 1 || state == 3 || count < outcome.FailureCount || phase is < 1 or > 4)
            {
                throw new InvalidDataException("Invalid test frame version, order or counter.");
            }

            outcome.FailureCount = count;
            var payload = frame.AsSpan(32, length - 32);
            if (state < 2)
            {
                var expected = state == 0 ? outcome.Catalog.ArtifactId : outcome.Test.CaseId;
                if (kind != state || payload.Length != 67 || !payload.SequenceEqual(Encoding.ASCII.GetBytes(expected)) || issue != 0 || count != 0 || site != -1)
                {
                    throw new InvalidDataException("Test artifact or case handshake mismatch.");
                }

                state++;
                outcome.HandshakeCompleted = state == 2;
                continue;
            }

            switch (kind)
            {
                case 2:
                    if (issue == 0 || lastIssue == ulong.MaxValue || issue != lastIssue + 1 || issue != count || (uint)site >= (uint)outcome.Catalog.Sites.Count || payload.Length != 0)
                    {
                        throw new InvalidDataException("Invalid verification failure record.");
                    }

                    lastIssue = issue;
                    if (outcome.Failures.Count < settings.DiagnosticCount && retainedBytes <= settings.DiagnosticBytes - 32 && budget.Detail(32))
                    {
                        retainedBytes += 32;
                        outcome.Failures.Add(new(issue, outcome.Catalog.Sites[site])
                        {
                            Phase = Phase(phase),
                            MessageTruncated = outcome.Catalog.Sites[site].Node.Message is not null,
                        });
                    }

                    break;
                case 3:
                    var failure = outcome.Failures.Find(x => x.Issue == issue);
                    if (issue == 0 || issue > lastIssue || site is < 0 or > 1)
                    {
                        throw new InvalidDataException("Invalid verification message record.");
                    }

                    var validBytes = payload.Length;
                    if (site == 1)
                    {
                        while (validBytes > 0 && Rune.DecodeLastFromUtf8(payload[..validBytes], out _, out _) != System.Buffers.OperationStatus.Done)
                        {
                            validBytes--;
                        }
                    }

                    try
                    {
                        _ = StrictUtf8.GetCharCount(payload[..validBytes]);
                    }
                    catch (DecoderFallbackException ex)
                    {
                        throw new InvalidDataException("Invalid verification message encoding.", ex);
                    }

                    if (failure is not null)
                    {
                        if (failure.Message is not null)
                        {
                            throw new InvalidDataException("Repeated verification message.");
                        }

                        var available = Math.Min(payload.Length + 32, Math.Max(0, settings.DiagnosticBytes - retainedBytes));
                        var reserved = budget.Bytes(available, false);
                        retainedBytes += reserved;
                        if (reserved < 32)
                        {
                            break;
                        }

                        var keep = reserved - 32;
                        while (keep > 0 && Rune.DecodeLastFromUtf8(payload[..keep], out _, out _) != System.Buffers.OperationStatus.Done)
                        {
                            keep--;
                        }

                        failure.Message = StrictUtf8.GetString(payload[..keep]);
                        failure.MessageTruncated = site == 1 || keep != payload.Length;
                    }

                    break;
                case 4:
                case 5:
                    if (payload.Length != 0 || (count != 0 && lastIssue == 0) ||
                        (kind == 4 && (count == 0 || issue > lastIssue || (uint)site >= (uint)outcome.Catalog.Sites.Count)) ||
                        (kind == 5 && (issue != 0 || site != -1)))
                    {
                        throw new InvalidDataException("Invalid Abort record.");
                    }

                    // Main worker chooses timeout/cancellation independently of reported Abort.
                    if (outcome.Termination is "notStarted" or "crash")
                    {
                        outcome.Termination = kind == 4 ? "requireAbort" : "abort";
                    }

                    outcome.TerminationSite = site >= 0 ? site : null;
                    outcome.TerminationPhase = Phase(phase);

                    state = 3;
                    break;
                case 7:
                    if (issue == 0 || issue > lastIssue || payload.Length != 24 || site != -1)
                    {
                        throw new InvalidDataException("Invalid scalar snapshot.");
                    }

                    var captured = outcome.Failures.Find(x => x.Issue == issue);
                    var side = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    var scalarKind = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
                    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
                    var bits = BinaryPrimitives.ReadUInt128LittleEndian(payload[8..]);
                    if (side is < 0 or > 1 || scalarKind is < 1 or > 4 ||
                        (scalarKind == 1 ? width != 1 : scalarKind == 4 ? width is not (32 or 64) : width is not (8 or 16 or 32 or 64 or 128)) ||
                        (width < 128 && bits >> width != 0) || (captured is not null && captured.Values.Any(x => x.Side == side)))
                    {
                        throw new InvalidDataException("Invalid scalar snapshot type.");
                    }

                    if (captured is not null && captured.Values.Count < 2 && retainedBytes <= settings.DiagnosticBytes - 56 && budget.Bytes(56, false) == 56)
                    {
                        retainedBytes += 56;
                        captured.Values.Add((side, scalarKind, width, bits.ToString("x32", CultureInfo.InvariantCulture)));
                    }

                    break;
                case 6:
                    if (payload.Length != 0 || issue != 0 || site != -1 || (count != 0 && lastIssue == 0))
                    {
                        throw new InvalidDataException("Invalid completion record.");
                    }

                    outcome.Completed = true;
                    state = 3;
                    break;
                default:
                    throw new InvalidDataException("Unknown test frame kind.");
            }
        }
    }

    private static async Task<long> Drain(Stream input, string path, int remaining, TestBudget budget, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        var buffer = new byte[8192];
        long omitted = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            var keep = budget.Bytes(Math.Min(read, remaining), true);
            await output.WriteAsync(buffer.AsMemory(0, keep), cancellationToken).ConfigureAwait(false);
            remaining -= keep;
            omitted = checked(omitted + read - keep);
        }

        return omitted;
    }

    private static string Phase(int phase) => phase switch { 2 => "cleanup", 3 => "initialization", 4 => "shutdown", _ => "body" };
}
