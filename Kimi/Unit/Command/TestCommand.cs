// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Testing;
using SimpleCommandLine;

namespace Kimi.Command;

[SimpleCommand("test")]
public sealed class TestCommand(Kimigayo kimigayo, Solution solution) : ISimpleCommand
{
    public async Task Execute(string[] args, CancellationToken cancellationToken)
    {
        var output = Console.Out;
        var errors = new List<string>();
        var outcomes = new List<TestOutcome>();
        var prepared = new List<(Project Project, Compilation Compilation, TestSettings Settings)>();
        var pinnedExecutables = new List<FileStream>();
        TestOptions? options = null;
        var exit = 0;
        string? runDirectory = null;
        FileStream? active = null;
        try
        {
            options = TestOptions.Parse(args);
            // Compiler diagnostics must never corrupt the machine-readable result stream.
            Console.SetOut(Console.Error);
            var snapshot = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
            {
                if (item.Key is string name && item.Value is string value && !name.StartsWith("KIMI_TEST_", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot[name] = value;
                }
            }

            solution.LoadForBuild(null, options.Compiler, options.Input is null ? [] : [options.Input]);
            solution.PrepareProject(null);
            if (solution.Projects.Count == 0 || solution.SolutionFile.Projects.Any(x => !solution.Projects.ContainsKey(x)))
            {
                throw new InvalidDataException("Every selected project must load successfully.");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in solution.Projects.Values.OrderBy(x => x.FilePath ?? x.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                project.KimiOptions = options.Compiler;
                project.SolutionLanguageVersion = solution.SolutionFile.Configuration.LangVersion;
                try
                {
                    if (project.PrepareTests(cancellationToken) is not { } compilation)
                    {
                        errors.Add("Test semantic verification failed: " + project.Name);
                        exit = 2;
                        continue;
                    }

                    if (!identities.Add(compilation.Tests.ProjectId))
                    {
                        throw new InvalidDataException("Conflicting project identity; configure distinct TestProjectId values: " + compilation.Tests.ProjectId);
                    }

                    prepared.Add((project, compilation, options.Settings(project.ProjectFile.Test)));
                    foreach (var test in compilation.Tests.Cases)
                    {
                        outcomes.Add(new(test, compilation.Tests)
                        {
                            Selected = options.Get("--case") is { } id ? test.CaseId == id : options.Get("--filter") is not { } filter || test.Name.Contains(filter, StringComparison.Ordinal),
                        });
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add(project.Name + ": " + ex.Message);
                    exit = 2;
                }
            }

            outcomes.Sort(static (a, b) =>
            {
                var order = string.CompareOrdinal(a.Test.Name, b.Test.Name);
                if (order == 0)
                {
                    order = string.CompareOrdinal(a.Test.File, b.Test.File);
                }

                if (order == 0)
                {
                    order = string.CompareOrdinal(a.Catalog.ProjectId, b.Catalog.ProjectId);
                }

                return order == 0 ? string.CompareOrdinal(a.Test.CaseId, b.Test.CaseId) : order;
            });
            if (exit != 0)
            {
                return;
            }

            if (!outcomes.Any(x => x.Selected) && (!options.AllowEmpty || options.Get("--case") is not null))
            {
                throw new InvalidDataException("No test cases match the selection.");
            }

            if (options.List || !outcomes.Any(x => x.Selected))
            {
                return;
            }

            var input = Solution.ResolveInputPath(options.Input ?? Directory.GetCurrentDirectory());
            var resultRoot = Path.GetFullPath(options.Get("--results-dir") ?? Path.Combine(Directory.Exists(input) ? input : Path.GetDirectoryName(input)!, "bin", "test-results"));
            runDirectory = Path.Combine(resultRoot, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            active = new FileStream(Path.Combine(runDirectory, ".active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var artifacts = new Dictionary<TestCatalog, ArtifactPaths>();
            var artifactIndex = 0;
            foreach (var (project, compilation, _) in prepared)
            {
                if (!outcomes.Any(x => ReferenceEquals(x.Catalog, compilation.Tests) && x.Selected))
                {
                    continue;
                }

                var directory = Path.Combine(runDirectory, "artifacts", "p" + (artifactIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                var stem = Path.Combine(directory, "tests");
                var paths = new ArtifactPaths(stem + ".ll", stem + ".link.json", stem + ".link.build.json", stem + "." + project.ProjectFile.Optimization);
                if (!EmissionArtifacts.Publish(compilation, paths, out _, out var failure))
                {
                    errors.Add(project.Name + ": " + failure);
                    exit = 2;
                    continue;
                }

                try
                {
                    await NativeToolchain.Build(project, paths, (severity, message) => kimigayo.WriteLine(severity, message), cancellationToken).ConfigureAwait(false);
                    pinnedExecutables.Add(new FileStream(paths.Executable, FileMode.Open, FileAccess.Read, FileShare.Read));
                    compilation.Tests.WriteManifest(stem + ".test.json", paths.Executable);
                    artifacts.Add(compilation.Tests, paths);
                }
                catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
                {
                    errors.Add(project.Name + ": " + ex.Message);
                    exit = 2;
                }
            }

            if (exit != 0)
            {
                return;
            }

            var budget = new TestBudget(options);
            var selected = outcomes.Where(x => x.Selected).ToArray();
            var next = -1;
            var stopped = 0;
            var workers = new Task[Math.Min(options.Jobs, selected.Length)];
            for (var w = 0; w < workers.Length; w++)
            {
                workers[w] = Worker();
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            foreach (var outcome in outcomes.Where(x => x.Selected))
            {
                if (!outcome.Started)
                {
                    outcome.Termination = cancellationToken.IsCancellationRequested ? "cancelled" : "notStarted";
                }

                exit = Math.Max(exit, outcome.ExitCode);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                exit = 130;
            }

            async Task Worker()
            {
                while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref stopped) == 0)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= selected.Length)
                    {
                        return;
                    }

                    var outcome = selected[index];
                    var owner = prepared.Find(x => ReferenceEquals(x.Compilation.Tests, outcome.Catalog));
                    try
                    {
                        await TestWorker.Run(artifacts[outcome.Catalog].Executable, Path.GetFullPath(owner.Project.Directory), runDirectory, outcome.Catalog.Cases.IndexOf(outcome.Test), owner.Settings, snapshot, outcome, budget, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        outcome.Errors.Add("Internal test worker error: " + ex.Message);
                    }

                    if (outcome.Errors.Count != 0)
                    {
                        Interlocked.Exchange(ref stopped, 1);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            exit = 130;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            errors.Add(ex.Message);
            exit = runDirectory is null ? 2 : 3;
        }
        catch (Exception ex)
        {
            errors.Add("Internal test runner error: " + ex.Message);
            exit = 3;
        }
        finally
        {
            foreach (var pinned in pinnedExecutables)
            {
                pinned.Dispose();
            }

            Console.SetOut(output);
            foreach (var outcome in outcomes.Where(x => x.Selected && !x.Started))
            {
                outcome.NotStartedReason = options?.List == true ? "listing" : exit == 130 ? "cancelled" : exit == 2 ? "verificationOrBuildFailed" : "runnerStopped";
            }

            if (runDirectory is not null)
            {
                try
                {
                    PersistJson();
                    File.WriteAllText(Path.Combine(runDirectory, ".kimi-test-run"), "1");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add("Result persistence failed: " + ex.Message);
                    exit = exit == 130 ? 130 : 3;
                }

                active?.Dispose();
                try
                {
                    File.Delete(Path.Combine(runDirectory, ".active"));
                    TestResultStore.Prune(Path.GetDirectoryName(runDirectory)!);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add("Result retention failed: " + ex.Message);
                    exit = exit == 130 ? 130 : 3;
                    try
                    {
                        PersistJson();
                    }
                    catch (Exception persistence) when (persistence is IOException or UnauthorizedAccessException)
                    {
                        errors.Add("Result update failed: " + persistence.Message);
                    }
                }
            }

            if (options?.Json == true || Enumerable.Range(0, Math.Max(0, args.Length - 1)).Any(i => args[i] == "--format" && args[i + 1] == "json"))
            {
                using var buffer = new MemoryStream();
                WriteJson(buffer);
                output.WriteLine(Encoding.UTF8.GetString(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
            }
            else
            {
                foreach (var outcome in outcomes.Where(x => x.Selected))
                {
                    output.WriteLine(options?.List == true ? $"{outcome.Test.CaseId} {outcome.Catalog.ProjectId}::{outcome.Test.Name}" :
                        $"{(!outcome.Started ? "NOT STARTED" : outcome.ExitCode == 0 ? "PASS" : "FAIL")} {outcome.Catalog.ProjectId}::{outcome.Test.Name} ({outcome.Termination}, failures={outcome.FailureCount})");
                    foreach (var failure in outcome.Failures)
                    {
                        output.WriteLine($"  {failure.Site.File}:{failure.Site.Line}:{failure.Site.Column}: {failure.Site.Expression}{(failure.Message is null ? string.Empty : ": " + failure.Message)}");
                        foreach (var value in failure.Values)
                        {
                            output.WriteLine($"    {(value.Side == 0 ? "left" : "right")}: {value.Width}-bit value 0x{value.Bits}");
                        }
                    }

                    foreach (var error in outcome.Errors)
                    {
                        output.WriteLine("  " + error);
                    }

                    if (outcome.Stdout is not null || outcome.Stderr is not null)
                    {
                        output.WriteLine($"  stdout: {outcome.Stdout}; stderr: {outcome.Stderr}; omitted log bytes: {outcome.OmittedLogBytes}");
                    }
                }

                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                output.WriteLine($"Selected {outcomes.Count(x => x.Selected)}; exit {exit}.");
                if (runDirectory is not null)
                {
                    output.WriteLine("Results: " + Path.Combine(runDirectory, "result.json"));
                }
            }

            Environment.ExitCode = exit;
        }

        void PersistJson()
        {
            var temporary = Path.Combine(runDirectory!, "result.pending.json");
            using (var file = new FileStream(temporary, FileMode.Create))
            {
                WriteJson(file);
                file.Flush(true);
            }

            File.Move(temporary, Path.Combine(runDirectory!, "result.json"), true);
        }

        void WriteJson(Stream destination)
        {
            using var writer = new Utf8JsonWriter(destination, new() { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("operation", options?.List == true ? "list" : "run");
            if (options is not null)
            {
                writer.WriteStartObject("settings");
                writer.WriteString("filter", options.Get("--filter"));
                writer.WriteString("case", options.Get("--case"));
                writer.WriteBoolean("allowEmpty", options.AllowEmpty);
                writer.WriteNumber("jobs", options.Jobs);
                writer.WriteNumber("diagnosticCount", options.Number("--run-diagnostic-count", 100000));
                writer.WriteNumber("diagnosticBytes", options.Number("--run-diagnostic-bytes", 67108864));
                writer.WriteNumber("logBytes", options.Number("--run-log-bytes", 268435456));
                writer.WriteString("resultDirectory", runDirectory);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("projects");
            foreach (var (project, compilation, settings) in prepared)
            {
                writer.WriteStartObject();
                writer.WriteString("identity", compilation.Tests.ProjectId);
                writer.WriteString("path", project.FilePath ?? project.Directory);
                writer.WriteString("target", compilation.BuildMetadata!.TargetTriple);
                writer.WriteString("timeout", settings.Timeout);
                writer.WriteString("recoveryGrace", settings.RecoveryGrace);
                writer.WriteNumber("diagnosticCount", settings.DiagnosticCount);
                writer.WriteNumber("diagnosticBytes", settings.DiagnosticBytes);
                writer.WriteNumber("logBytes", settings.LogBytes);
                writer.WriteStartArray("environmentNames");
                foreach (var name in settings.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("cases");
            foreach (var outcome in outcomes)
            {
                outcome.Write(writer);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("summary");
            writer.WriteNumber("selected", outcomes.Count(x => x.Selected));
            writer.WriteNumber("passed", outcomes.Count(x => x.Started && x.ExitCode == 0));
            writer.WriteNumber("failed", outcomes.Count(x => x.Started && x.ExitCode != 0));
            writer.WriteEndObject();
            writer.WriteStartArray("errors");
            foreach (var error in errors)
            {
                writer.WriteStringValue(error);
            }

            writer.WriteEndArray();
            writer.WriteNumber("exitCode", exit);
            writer.WriteEndObject();
        }
    }
}
