// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kimi.Checking;
using Kimi.Command;
using Kimi.Compiler;

#pragma warning disable SA1402 // The worker protocol is one vocabulary.

namespace Kimi.Lsp;

/// <summary>The role of an open document (SPEC 23.4.2).</summary>
internal enum DocumentRole : byte
{
    /// <summary>A <c>.kimi</c> source document.</summary>
    Source,

    /// <summary>A <c>.kimiproj</c> project document.</summary>
    Project,
}

/// <summary>An open document as the worker sees it at the base.</summary>
/// <param name="Identity">The document.</param>
/// <param name="Role">The role.</param>
/// <param name="LastEvent">The number of its last event.</param>
internal readonly record struct OpenDocumentView(SourceIdentity Identity, DocumentRole Role, long LastEvent);

/// <summary>One input re-validation considers.</summary>
/// <param name="Key">The input.</param>
/// <param name="State">The retained state, if any.</param>
/// <param name="Marked">Whether an event marked it.</param>
/// <param name="Watched">Whether a watched-file event marked it.</param>
internal readonly record struct RevalidationTarget(InputKey Key, InputState? State, bool Marked, bool Watched);

/// <summary>Everything a workspace check starts from, captured by the state owner in the base step.</summary>
internal sealed record CheckStart
{
    public required long Base { get; init; }

    public required RevalidationTarget[] Targets { get; init; }

    /// <summary>Gets the base-step text of every marked open document; null for a desynchronized one.</summary>
    public required Dictionary<SourceIdentity, string?> BaseTexts { get; init; }

    public required OpenDocumentView[] Documents { get; init; }

    public required LspSettings Settings { get; init; }

    public required Dictionary<SourceIdentity, LoadedProject> Projects { get; init; }

    public required Dictionary<UnitKey, UnitResult?> Units { get; init; }

    public required Func<ImmutableHashSet<InputKey>> ChangedAfterBase { get; init; }

    public required Action<object> Post { get; init; }

    public required Kimigayo Kimigayo { get; init; }

    public required Func<StoreSnapshot, CheckInputs, UnitPlan, CheckInputSource, CancellationToken, CheckOutput> Runner { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}

/// <summary>Asks the state owner to commit the comparisons of re-validation; the snapshot completes the handshake.</summary>
/// <param name="Comparisons">The observed states.</param>
/// <param name="Committed">Completes with the committed snapshot.</param>
internal sealed record CommitRequest(List<(InputKey Key, InputState State)> Comparisons, TaskCompletionSource<StoreSnapshot> Committed);

/// <summary>The products of discovery and derivation (SPEC 23.4.3, 23.4.4).</summary>
internal sealed record DerivationDone
{
    public required List<LoadedProject> NewProjects { get; init; }

    public required HashSet<SourceIdentity> Reached { get; init; }

    public required DiscoveryRecord Record { get; init; }

    public required HashSet<UnitKey> Required { get; init; }

    public required HashSet<SourceIdentity> KeepOwners { get; init; }

    public required HashSet<SourceIdentity> TestSourceOwners { get; init; }
}

/// <summary>One unit's new result.</summary>
/// <param name="Result">The result.</param>
internal sealed record UnitDone(UnitResult Result);

/// <summary>The product units of one owner were visited; a decided owner fixes whether its test unit is required.</summary>
/// <param name="Owner">The owner.</param>
/// <param name="Decided">Whether every product unit was checked or reused.</param>
/// <param name="RequiresTest">Whether the test unit is required.</param>
/// <param name="TestKey">The test unit key.</param>
internal sealed record ProductsDone(SourceIdentity Owner, bool Decided, bool RequiresTest, UnitKey TestKey);

/// <summary>The workspace check ended.</summary>
/// <param name="Failure">An unexpected exception, or null.</param>
internal sealed record CheckDone(Exception? Failure);

/// <summary>Runs one workspace check on the worker (SPEC 23.4.6): re-validation, discovery, derivation and visiting.</summary>
internal sealed class WorkspaceCheck
{
    private readonly CheckStart start;
    private readonly Dictionary<string, List<string>> openByDirectory;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceCheck"/> class.</summary>
    /// <param name="start">The captured start state.</param>
    public WorkspaceCheck(CheckStart start)
    {
        this.start = start;
        this.openByDirectory = CheckInputs.GroupOpenPaths(start.Documents.Select(static x => x.Identity.Value));
    }

    /// <summary>Gets the host target for implicit projects, or empty when the host has none.</summary>
    public static string HostTarget { get; } = OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 ? WindowsProfile.Target : string.Empty;

    /// <summary>Places a check's diagnostics at their report URIs (SPEC 23.4.7) and sorts each URI's list.</summary>
    /// <param name="output">The check output.</param>
    /// <param name="sources">The source files the check read.</param>
    /// <param name="display">The project file or implicit source.</param>
    /// <returns>The diagnostics per report URI.</returns>
    public static Dictionary<SourceIdentity, LspDiagnostic[]> Place(CheckOutput output, IReadOnlyList<SourceIdentity> sources, SourceIdentity display)
    {
        var reports = new Dictionary<SourceIdentity, LspDiagnostic[]>(sources.Count);
        foreach (var source in sources)
        {
            if (!source.IsBuiltIn)
            {
                reports.TryAdd(source, []); // Most checked sources have no diagnostics and share the empty array.
            }
        }

        if (output.Diagnostics.Length == 0)
        {
            return reports;
        }

        var lists = new Dictionary<SourceIdentity, List<LspDiagnostic>>();
        foreach (var diagnostic in output.Diagnostics)
        {
            var (uri, range) = diagnostic.Location.IsEmpty || diagnostic.Location.IsBuiltIn ? (display, default) : (diagnostic.Location, diagnostic.Range ?? default);
            if (!lists.TryGetValue(uri, out var list))
            {
                list = [];
                lists.Add(uri, list);
            }

            list.Add(new(range, (int)diagnostic.Severity, diagnostic.Code, "kimigayo", diagnostic.Message));
        }

        foreach (var (uri, list) in lists)
        {
            list.Sort(Compare);
            reports[uri] = Deduplicate(list);
        }

        return reports;
    }

    /// <summary>Orders published diagnostics by range, code, severity and message.</summary>
    /// <param name="left">The first diagnostic.</param>
    /// <param name="right">The second diagnostic.</param>
    /// <returns>The ordering.</returns>
    public static int Compare(LspDiagnostic left, LspDiagnostic right)
    {
        var order = left.Range.CompareTo(right.Range);
        if (order == 0)
        {
            order = string.CompareOrdinal(left.Code, right.Code);
        }

        if (order == 0)
        {
            order = left.Severity.CompareTo(right.Severity);
        }

        return order == 0 ? string.CompareOrdinal(left.Message, right.Message) : order;
    }

    /// <summary>Removes adjacent duplicates of a sorted list.</summary>
    /// <param name="sorted">The sorted diagnostics.</param>
    /// <returns>The distinct diagnostics.</returns>
    public static LspDiagnostic[] Deduplicate(List<LspDiagnostic> sorted)
    {
        if (sorted.Count == 0)
        {
            return [];
        }

        var values = CollectionsMarshal.AsSpan(sorted);
        var count = 1;
        for (var i = 1; i < values.Length; i++)
        {
            if (!values[i].Equals(values[count - 1]))
            {
                values[count++] = values[i];
            }
        }

        return values[..count].ToArray();
    }

    /// <summary>Checks one unit through the shared check entry.</summary>
    /// <param name="kimigayo">The silent compiler service.</param>
    /// <param name="plan">The unit plan.</param>
    /// <param name="debug">The session's <c>Debug</c> setting.</param>
    /// <param name="source">The recording input source.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The output.</returns>
    public static CheckOutput RunCheck(Kimigayo kimigayo, UnitPlan plan, bool debug, CheckInputSource source, CancellationToken cancellationToken)
    {
        Project project;
        if (plan.Project?.Project is { } loaded)
        {
            project = loaded;
        }
        else
        {
            try
            {
                project = Project.CreateFromSource(kimigayo, plan.Key.Owner.Value, new KimiOptions { Target = plan.Key.Target, Debug = debug });
            }
            catch (PlatformNotSupportedException ex)
            {
                return Blocked(DiagnosticCode.ProjectPreparationFailed_Kd, plan.Display, ex.Message);
            }
        }

        return CheckService.Run(project, plan.Key.Target, plan.Mode, debug, source, cancellationToken);
    }

    /// <summary>Runs the check to completion; every product goes to the state owner's queue.</summary>
    public void Run()
    {
        Exception? failure = null;
        try
        {
            var request = new CommitRequest(this.Revalidate(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            this.start.Post(request);
            var snapshot = request.Committed.Task.WaitAsync(this.start.CancellationToken).GetAwaiter().GetResult(); // Exit cancels a commit it never answers.
            var inputs = new CheckInputs(snapshot, this.start.ChangedAfterBase);
            var plans = this.Discover(inputs);
            this.Visit(inputs, plans);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        this.start.Post(new CheckDone(failure));
    }

    private static CheckOutput Blocked(DiagnosticCode code, SourceIdentity location, object? argument = null)
        => new(CheckOutcome.Blocked, false, TestPresence.Unknown, [CheckService.Create(code, location, argument)]);

    private static LoadedProject CreateLoaded(SourceIdentity path, Project project, SnapshotInputSource source)
    {
        var directory = Path.GetDirectoryName(path.Value)!;
        var members = new HashSet<SourceIdentity>();
        foreach (var file in project.KimiFiles)
        {
            members.Add(SourceIdentity.FromPath(file));
        }

        foreach (var test in project.ProjectFile.TestSources)
        {
            try
            {
                members.Add(SourceIdentity.FromPath(Path.GetFullPath(test, directory)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // The check reports the invalid path.
            }
        }

        return new()
        {
            Path = path,
            Project = project,
            Members = members,
            ProductReferences = References(project.ProjectFile.Dependencies, directory),
            TestReferences = References(project.ProjectFile.TestDependencies, directory),
            HasTestSources = project.ProjectFile.TestSources.Length != 0,
            Inputs = source.GetRecorded(),
        };
    }

    private static string[] References(Dictionary<string, DependencyReference> references, string directory)
    {
        var paths = new List<string>(references.Count);
        foreach (var reference in references.Values)
        {
            if (reference.Project is { } project)
            {
                try
                {
                    paths.Add(Path.GetFullPath(project, directory));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // The check reports the invalid reference.
                }
            }
        }

        return paths.ToArray();
    }

    // Lists the project files in a directory and in every ancestor; false when a listing is pending or unreadable,
    // which leaves membership undetermined, so it cannot establish an implicit project.
    private static bool TryListCandidates(SnapshotInputSource record, string directory, List<SourceIdentity> list)
    {
        var determined = true;
        for (string? current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                foreach (var path in record.GetFiles(current, InputKey.ProjectPattern))
                {
                    list.Add(SourceIdentity.FromPath(path));
                }
            }
            catch (Exception ex) when (ex is PendingInputException or IOException)
            {
                determined = false;
            }
        }

        return determined;
    }

    private static bool IsPending(CheckInputs inputs, UnitPlan plan, UnitResult? previous)
    {
        foreach (var member in plan.Members)
        {
            if (inputs.IsPending(InputKey.File(member)))
            {
                return true;
            }
        }

        foreach (var input in previous?.Inputs ?? [])
        {
            if (inputs.IsPending(input.Key))
            {
                return true;
            }
        }

        foreach (var input in plan.Project?.Inputs ?? [])
        {
            if (inputs.IsPending(input.Key))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 23.4.5: compare marked inputs, disk inputs whose stamp changed, and unestablished inputs.
    private List<(InputKey Key, InputState State)> Revalidate()
    {
        var comparisons = new List<(InputKey, InputState)>();
        var open = new HashSet<SourceIdentity>();
        foreach (var document in this.start.Documents)
        {
            open.Add(document.Identity);
        }

        foreach (var target in this.start.Targets)
        {
            this.start.CancellationToken.ThrowIfCancellationRequested();
            if (this.Observe(target, open) is { } state)
            {
                comparisons.Add((target.Key, state));
            }
        }

        return comparisons;
    }

    private InputState? Observe(RevalidationTarget target, HashSet<SourceIdentity> open)
    {
        var (key, previous, marked, watched) = target;
        var path = key.Identity.Value;
        if (key.Kind == InputKind.Listing)
        {
            var stamp = DiskReader.DirectoryStamp(path);
            var reusable = !watched && previous is { Established: true, DiskNames: not null } && previous.Stamp == stamp;
            if (!marked && reusable)
            {
                return null;
            }

            var disk = reusable ? previous! : DiskReader.ReadListing(path, key.Pattern);
            return DiskReader.MergeListing(key, disk, CollectionsMarshal.AsSpan(this.openByDirectory.GetValueOrDefault(path)));
        }

        if (this.start.BaseTexts.TryGetValue(key.Identity, out var text))
        {
            var (fileStamp, length) = DiskReader.Stat(path);
            var bom = !watched && previous?.Content is { } content && previous.Stamp == fileStamp && previous.Length == length ? content.HasBom : DiskReader.HasBom(path);
            return text is null
                ? InputState.Unestablished(DesynchronizedInputException.Failure, fileStamp, true, length)
                : new() { Content = SourceContent.FromText(text, bom), Overlay = true, Stamp = fileStamp, Length = length };
        }

        if (open.Contains(key.Identity) && previous is { Overlay: true } overlay)
        {
            var (fileStamp, length) = DiskReader.Stat(path);
            if (!watched && overlay.Stamp == fileStamp && overlay.Length == length)
            {
                return null;
            }

            var content = overlay.Content is { Text: { } overlayText } ? SourceContent.FromText(overlayText, DiskReader.HasBom(path)) : overlay.Content;
            return overlay.Established
                ? new() { Content = content, Overlay = true, Stamp = fileStamp, Length = length }
                : InputState.Unestablished(overlay.Failure!, fileStamp, true, length);
        }

        if (!marked && previous is { Established: true, Overlay: false })
        {
            var (fileStamp, length) = DiskReader.Stat(path);
            if (previous.Absent ? length < 0 : previous.Stamp == fileStamp && previous.Length == length)
            {
                return null;
            }
        }

        return DiskReader.ReadFile(path);
    }

    // SPEC 23.4.3 and 23.4.4: grow the loaded projects to a fixed point, then derive the required units and their plans.
    private List<UnitPlan> Discover(CheckInputs inputs)
    {
        var settings = this.start.Settings;
        var loaded = new Dictionary<SourceIdentity, LoadedProject?>();
        var newProjects = new List<LoadedProject>();
        var record = new SnapshotInputSource(inputs);
        var openSources = new List<SourceIdentity>();
        var openProjects = new HashSet<SourceIdentity>();
        foreach (var document in this.start.Documents)
        {
            (document.Role == DocumentRole.Source ? (ICollection<SourceIdentity>)openSources : openProjects).Add(document.Identity);
        }

        var selected = new HashSet<SourceIdentity>(settings.SelectedProjects);
        var candidates = new Dictionary<SourceIdentity, List<SourceIdentity>>();
        var undeterminedDocuments = new HashSet<SourceIdentity>();
        var byDirectory = new Dictionary<string, (List<SourceIdentity> List, bool Undetermined)>(SourceIdentity.PathComparer);
        foreach (var document in openSources)
        {
            // The documents of one directory share their candidates, which are read once.
            var directory = Path.GetDirectoryName(document.Value) ?? string.Empty;
            if (!byDirectory.TryGetValue(directory, out var found))
            {
                var list = new List<SourceIdentity>();
                found = (list, !TryListCandidates(record, directory, list));
                byDirectory.Add(directory, found);
            }

            if (found.Undetermined)
            {
                undeterminedDocuments.Add(document);
            }

            candidates.Add(document, found.List);
        }

        foreach (var root in selected.Concat(openProjects).Concat(candidates.Values.SelectMany(static x => x)))
        {
            this.Load(root, inputs, loaded, newProjects);
        }

        var active = new HashSet<SourceIdentity>();
        var expanded = new HashSet<SourceIdentity>();
        var pendingProjects = new Stack<SourceIdentity>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (path, item) in loaded.ToArray())
            {
                if (item?.Project is null || active.Contains(path) ||
                    !(selected.Contains(path) || openProjects.Contains(path) || openSources.Exists(item.Members.Contains)))
                {
                    continue;
                }

                active.Add(path);
                changed = true;
                foreach (var reference in item.ProductReferences)
                {
                    this.LoadProducts(SourceIdentity.FromPath(reference), inputs, loaded, newProjects, expanded, pendingProjects);
                }

                foreach (var reference in item.TestReferences)
                {
                    this.LoadProducts(SourceIdentity.FromPath(reference), inputs, loaded, newProjects, expanded, pendingProjects);
                }
            }
        }

        var roots = new HashSet<SourceIdentity>(selected);
        roots.UnionWith(openProjects);
        foreach (var list in candidates.Values)
        {
            roots.UnionWith(list);
        }

        var plans = new List<UnitPlan>();
        var required = new HashSet<UnitKey>();
        var keepOwners = new HashSet<SourceIdentity>();
        var testSourceOwners = new HashSet<SourceIdentity>();
        foreach (var (path, item) in loaded)
        {
            if (item is null)
            {
                keepOwners.Add(path); // Undetermined: its units stay as they are.
            }
            else if (item.Project is null)
            {
                var existing = this.start.Units.Keys.Where(x => x.Owner == path && x.Kind != UnitKind.FailedRoot).ToArray();
                foreach (var key in existing)
                {
                    required.Add(key);
                    plans.Add(new(key, item, path, key.Kind == UnitKind.Test ? CheckMode.Test : CheckMode.Product, []));
                }

                if (existing.Length == 0 && roots.Contains(path))
                {
                    var key = new UnitKey(path, UnitKind.FailedRoot, string.Empty);
                    required.Add(key);
                    plans.Add(new(key, item, path, CheckMode.Product, []));
                }
            }
            else if (active.Contains(path))
            {
                var members = item.Members.ToArray();
                var targets = item.Project.ProjectFile.Targets;
                var productTargets = settings.AllTargets ? targets : settings.Target is { } target ? [target] :
                    targets.Length == 1 ? [targets[0]] : targets.Contains(HostTarget, StringComparer.Ordinal) && HostTarget.Length != 0 ? [HostTarget] : [];
                if (productTargets.Length == 0)
                {
                    var key = new UnitKey(path, UnitKind.Product, string.Empty);
                    required.Add(key);
                    plans.Add(new(key, item, path, CheckMode.Product, members));
                    continue;
                }

                foreach (var productTarget in productTargets)
                {
                    var key = new UnitKey(path, UnitKind.Product, productTarget);
                    required.Add(key);
                    plans.Add(new(key, item, path, CheckMode.Product, members));
                }

                var testKey = new UnitKey(path, UnitKind.Test, productTargets.Contains(WindowsProfile.Target, StringComparer.Ordinal) ? WindowsProfile.Target : string.Empty);
                plans.Add(new(testKey, item, path, CheckMode.Test, members));
                if (item.HasTestSources)
                {
                    required.Add(testKey);
                    testSourceOwners.Add(path);
                }
            }
        }

        foreach (var document in openSources)
        {
            var owned = loaded.Values.Any(x => x?.Project is not null && x.Members.Contains(document));
            if (owned)
            {
                continue;
            }

            if (undeterminedDocuments.Contains(document) || candidates[document].Exists(x => loaded.GetValueOrDefault(x) is not { Project: not null }))
            {
                keepOwners.Add(document);
                continue;
            }

            var target = settings.Target ?? HostTarget;
            var product = new UnitKey(document, UnitKind.Product, target);
            required.Add(product);
            plans.Add(new(product, null, document, CheckMode.Product, [document]));
            plans.Add(new(new(document, UnitKind.Test, target == WindowsProfile.Target ? target : string.Empty), null, document, CheckMode.Test, [document]));
        }

        this.start.Post(new DerivationDone
        {
            NewProjects = newProjects,
            Reached = new(loaded.Keys),
            Record = new DiscoveryRecord { Inputs = record.GetRecorded() },
            Required = required,
            KeepOwners = keepOwners,
            TestSourceOwners = testSourceOwners,
        });

        return plans;
    }

    private void LoadProducts(SourceIdentity path, CheckInputs inputs, Dictionary<SourceIdentity, LoadedProject?> loaded, List<LoadedProject> newProjects, HashSet<SourceIdentity> expanded, Stack<SourceIdentity> pending)
    {
        pending.Push(path);
        while (pending.TryPop(out path))
        {
            this.start.CancellationToken.ThrowIfCancellationRequested();
            if (!expanded.Add(path))
            {
                continue;
            }

            // Loading a candidate is separate from following it as a dependency.
            this.Load(path, inputs, loaded, newProjects);
            if (loaded[path] is { Project: not null } project)
            {
                foreach (var reference in project.ProductReferences)
                {
                    pending.Push(SourceIdentity.FromPath(reference));
                }
            }
        }
    }

    private void Load(SourceIdentity path, CheckInputs inputs, Dictionary<SourceIdentity, LoadedProject?> loaded, List<LoadedProject> newProjects)
    {
        if (loaded.ContainsKey(path))
        {
            return;
        }

        var previous = this.start.Projects.GetValueOrDefault(path);
        if (inputs.IsPending(InputKey.File(path)))
        {
            loaded.Add(path, previous); // The base rule: keep the previous state, or stay undetermined.
            return;
        }

        if (previous is not null && inputs.Snapshot.Valid.Contains(previous.Id))
        {
            loaded.Add(path, previous);
            return;
        }

        var source = new SnapshotInputSource(inputs);
        LoadedProject item;
        try
        {
            item = Project.TryCreate(this.start.Kimigayo, null, path.Value, source, out var project, out var failure)
                ? CreateLoaded(path, project, source)
                : new LoadedProject { Path = path, Failure = failure ?? "The project could not be loaded.", Inputs = source.GetRecorded() };
        }
        catch (PendingInputException)
        {
            loaded.Add(path, previous);
            return;
        }

        if (source.HasPendingInput)
        {
            loaded.Add(path, previous); // Project loading can translate an input exception to a load failure.
            return;
        }

        newProjects.Add(item);
        loaded.Add(path, item);
    }

    // SPEC 23.4.6: the most recently edited owners first, product units before the test unit.
    private void Visit(CheckInputs inputs, List<UnitPlan> plans)
    {
        var lastEvents = new Dictionary<SourceIdentity, long>();
        foreach (var document in this.start.Documents)
        {
            lastEvents[document.Identity] = document.LastEvent;
        }

        var owners = plans.GroupBy(static x => x.Key.Owner)
            .Select(group => (Owner: group.Key, Plans: group.ToList(), Recent: group.SelectMany(static x => x.Members).Append(group.Key).Max(x => lastEvents.GetValueOrDefault(x))))
            .OrderByDescending(static x => x.Recent)
            .ThenBy(static x => x.Owner);
        foreach (var (owner, ownerPlans, _) in owners)
        {
            this.start.CancellationToken.ThrowIfCancellationRequested();
            var decided = true;
            var requiresTest = false;
            UnitPlan? testPlan = null;
            foreach (var plan in ownerPlans.OrderBy(static x => x.Key))
            {
                if (plan.Key.Kind == UnitKind.Test)
                {
                    testPlan = plan;
                    continue;
                }

                var presence = this.VisitUnit(inputs, plan);
                decided &= presence is not null;
                requiresTest |= presence is TestPresence.Yes or TestPresence.Unknown;
            }

            if (testPlan is null || !decided)
            {
                continue;
            }

            requiresTest |= testPlan.Project?.HasTestSources == true;
            if (testPlan.Project is { Project: null })
            {
                this.VisitUnit(inputs, testPlan); // A failing project keeps its test unit Blocked.
                continue;
            }

            this.start.Post(new ProductsDone(owner, decided, requiresTest, testPlan.Key));
            if (requiresTest)
            {
                this.VisitUnit(inputs, testPlan);
            }
        }
    }

    // Returns the unit's test presence, or null when it was skipped.
    private TestPresence? VisitUnit(CheckInputs inputs, UnitPlan plan)
    {
        var previous = this.start.Units.GetValueOrDefault(plan.Key);
        if (IsPending(inputs, plan, previous))
        {
            return null;
        }

        if (previous is not null && inputs.Snapshot.Valid.Contains(previous.Id) && previous.Output.Outcome != CheckOutcome.Faulted)
        {
            return previous.Output.Presence;
        }

        var source = new SnapshotInputSource(inputs);
        CheckOutput output;
        try
        {
            output = this.Check(inputs, plan, source);
        }
        catch (PendingInputException)
        {
            return null;
        }

        if (source.HasPendingInput)
        {
            return null; // A compiler boundary may have translated the pending-input exception.
        }

        var result = new UnitResult
        {
            Key = plan.Key,
            Output = output,
            Reports = Place(output, source.Sources, plan.Display),
            Inputs = source.GetRecorded(plan.Project?.Inputs),
        };
        this.start.Post(new UnitDone(result));
        return output.Presence;
    }

    private CheckOutput Check(CheckInputs inputs, UnitPlan plan, SnapshotInputSource source)
    {
        if (plan.Key.Kind == UnitKind.FailedRoot || plan.Project is { Project: null })
        {
            return Blocked(DiagnosticCode.ProjectLoadFailed_Kd, plan.Display, plan.Project?.Failure ?? "The project could not be loaded.");
        }

        if (plan.Key.Target.Length == 0)
        {
            return plan.Mode == CheckMode.Test
                ? Blocked(DiagnosticCode.TestTargetUnavailable_Kd, plan.Display)
                : Blocked(DiagnosticCode.TargetSelectionRequired_Kd, plan.Display);
        }

        return this.start.Runner(inputs.Snapshot, inputs, plan, source, this.start.CancellationToken);
    }
}
