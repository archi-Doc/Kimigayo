// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Text;
using System.Text.Json;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Testing;
using SimpleCommandLine;

namespace Kimi.Command;

// kimi test's own result (IMPL 20.6.4; DECISIONS R9-M12; PLAN G1012). No exception type decides a fault here: the command's own Input
// error (CommandInputException: options and selection) prints its message, exit 2; preparation, generation and the native build's
// toolchain failure are results with their own owners, exit 2; a runner step's failure (the run directory, a project's artifact
// directory, its startup, the run) is the runner's, exit 3, an output failure (ArtifactFiles.IsOutputFailure) printing its message.
// Any other exception is the command's fault: one CheckFaulted_Kd without a location, rendered on standard error, and its code and
// message as the result's errors entry, exit 2, or 3 in a runner step. Cancellation returns 130.
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
            // Compiler diagnostics, a fault's record among them, must never corrupt the machine-readable result stream.
            Console.SetOut(Console.Error);
            options = TestOptions.Parse(args);
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
                throw new CommandInputException("Every selected project must load successfully.");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in solution.Projects.Values.OrderBy(x => x.FilePath ?? x.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                project.KimiOptions = options.Compiler;
                project.SolutionLanguageVersion = solution.SolutionFile.Configuration.LangVersion;
                if (project.PrepareTests(cancellationToken) is not { } compilation)
                {
                    errors.Add("Test semantic verification failed: " + project.Name);
                    exit = 2;
                    continue;
                }

                if (!identities.Add(compilation.Tests.ProjectId))
                {
                    // The project's selection error (IMPL 20.6.2: 2); the loop goes on to the later projects.
                    errors.Add(project.Name + ": Conflicting project identity; configure distinct TestProjectId values: " + compilation.Tests.ProjectId);
                    exit = 2;
                    continue;
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
                throw new CommandInputException("No test cases match the selection.");
            }

            if (options.List || !outcomes.Any(x => x.Selected))
            {
                return;
            }

            var input = Solution.ResolveInputPath(options.Input ?? Directory.GetCurrentDirectory());
            var resultRoot = options.ResultsDirectory ?? Path.GetFullPath(Path.Combine(Directory.Exists(input) ? input : Path.GetDirectoryName(input)!, "bin", "test-results"));
            runDirectory = Path.Combine(resultRoot, "run-" + Guid.NewGuid().ToString("N"));

            // The runner's steps (IMPL 20.6.4: 3) in DECISIONS R9-M12's order: the run directory with .active, each project's artifact
            // directory, each project's startup and the run. A step's failure ends the command (RunnerFailed).
            try
            {
                Directory.CreateDirectory(runDirectory);
                active = new FileStream(Path.Combine(runDirectory, ".active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunnerFailed(ex);
                return;
            }

            var artifacts = new Dictionary<TestCatalog, ArtifactPaths>();
            var artifactIndex = 0;
            foreach (var (project, compilation, _) in prepared)
            {
                if (!outcomes.Any(x => ReferenceEquals(x.Catalog, compilation.Tests) && x.Selected))
                {
                    continue;
                }

                var directory = Path.Combine(runDirectory, "artifacts", "p" + (artifactIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RunnerFailed(ex);
                    return;
                }

                var stem = Path.Combine(directory, "tests");
                var paths = ArtifactPaths.Create(stem + ".ll", project.ProjectFile.Optimization);
                if (!project.PublishArtifacts(compilation, paths, null))
                {
                    errors.Add("Test generation failed: " + project.Name); // Its records are rendered, as kimi build's (R5 C0a).
                    exit = 2;
                    continue;
                }

                // The toolchain's classification around its call only (PLAN G984): a build failure (IMPL 20.6.4: 2).
                try
                {
                    await NativeToolchain.Build(project, paths, (severity, message) => kimigayo.WriteLine(severity, message), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
                {
                    errors.Add(project.Name + ": " + ex.Message);
                    exit = 2;
                    continue;
                }

                // The runner's startup: the pinned executable, its case manifest and the artifact map (R9-M12; a startup failure ends
                // the project loop).
                try
                {
                    pinnedExecutables.Add(new FileStream(paths.Executable, FileMode.Open, FileAccess.Read, FileShare.Read));
                    compilation.Tests.WriteManifest(stem + ".test.json", paths.Executable);
                    artifacts.Add(compilation.Tests, paths);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RunnerFailed(ex);
                    return;
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
            try
            {
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunnerFailed(ex);
                return;
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
        catch (CommandInputException ex)
        {
            errors.Add(ex.Message);
            exit = 2;
        }
        catch (Exception ex)
        {
            // An exception no result owns ends the command's own result in its load, check or build steps (IMPL 20.6.4: 2).
            Fault(ex);
            exit = 2;
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

        // A runner step's failure (IMPL 20.6.4: 3): an output failure is its message, any other exception the command's fault.
        void RunnerFailed(Exception ex)
        {
            exit = 3;
            if (ArtifactFiles.IsOutputFailure(ex))
            {
                errors.Add(ex.Message);
            }
            else
            {
                Fault(ex);
            }
        }

        // The command's own Faulted result (SPEC 23.2.2, DIAGNOSTICS rule 9): its one CheckFaulted_Kd, with no location, is rendered
        // on standard error with its Note, and the result's errors list names its code and message once.
        void Fault(Exception ex)
        {
            var fault = DiagnosticFaults.Create(DiagnosticFault.Exception, ex, null);
            kimigayo.Render(fault, string.Empty);
            errors.Add(fault.Diagnostics[0].Code + ": " + fault.Diagnostics[0].Message);
        }

        void PersistJson()
        {
            var temporary = Path.Combine(runDirectory!, "result.pending.json");
            using (var file = new FileStream(temporary, FileMode.Create))
            {
                WriteJson(file);
                file.Flush(true);
            }

            ArtifactFiles.Replace(temporary, Path.Combine(runDirectory!, "result.json"));
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
