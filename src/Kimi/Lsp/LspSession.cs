// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Lsp;

/// <summary>
/// The state owner of the language server (SPEC 23.1): the single thread that processes the queue in order and alone
/// changes shared state — documents, revisions, marks, event numbers, bases, adopted results, required units and
/// publication decisions.
/// </summary>
internal sealed class LspSession : IDisposable
{
    private const string WatchRegistration = "kimi-watched-files";

    private readonly LspSender sender;
    private readonly Action<WorkspaceCheck> startWorker;
    private readonly Action<object> post;
    private readonly Kimigayo kimigayo = Kimigayo.CreateSilent();
    private readonly InputStore store = new();
    private readonly Dictionary<SourceIdentity, OpenDocument> documents = new();
    private readonly Dictionary<string, OpenDocument> documentsByUri = new(StringComparer.Ordinal);
    private readonly Dictionary<SourceIdentity, LoadedProject> projects = new();
    private readonly Dictionary<UnitKey, UnitState> units = new();
    private readonly Dictionary<SourceIdentity, HashSet<UnitKey>> contributors = new();
    private readonly Dictionary<SourceIdentity, (LspDiagnostic[] Payload, int? Version)> sent = new();
    private readonly Dictionary<SourceIdentity, LspRepair[]> repairs = new();
    private readonly List<(UnitKey Key, LspDiagnostic[] Payload)> contributions = [];
    private readonly List<LspDiagnostic[]> orderedContributions = [];
    private readonly HashSet<SourceIdentity> changedReports = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<SourceIdentity> undeterminedOwners = [];
    private DiscoveryRecord? discovery;
    private bool derivationAdopted;
    private LspSettings settings = new();
    private bool initialized;
    private bool clientInitialized;
    private bool shutdownRequested;
    private bool watchSupported;
    private bool relatedInformationSupported;
    private bool codeActionsSupported;
    private long? eligibleAt;
    private long checkBase = -1;
    private int nextRequestId;

    /// <summary>Initializes a new instance of the <see cref="LspSession"/> class.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="startWorker">Starts a workspace check on the worker.</param>
    /// <param name="post">Enqueues a worker product for this state owner.</param>
    public LspSession(LspSender sender, Action<WorkspaceCheck> startWorker, Action<object> post)
    {
        this.sender = sender;
        this.startWorker = startWorker;
        this.post = post;
    }

    /// <summary>Gets a value indicating whether <c>exit</c> ended the session.</summary>
    public bool Exited { get; private set; }

    /// <summary>Gets the process exit code: 0 after <c>shutdown</c>, otherwise 1.</summary>
    public int ExitCode => this.shutdownRequested ? 0 : 1;

    /// <summary>Gets the time at which the pending check becomes eligible, or null.</summary>
    public long? Deadline => this.checkBase < 0 && !this.shutdownRequested && !this.Exited ? this.eligibleAt : null;

    /// <summary>Gets a value indicating whether a workspace check is running.</summary>
    public bool Checking => this.checkBase >= 0;

    /// <summary>Gets or sets the runner that checks one unit; tests replace it.</summary>
    public Func<StoreSnapshot, CheckInputs, UnitPlan, CheckInputSource, CancellationToken, CheckOutput>? Runner { get; set; }

    /// <summary>Gets the input store, for tests.</summary>
    internal InputStore Store => this.store;

    /// <summary>Gets the required units, for tests.</summary>
    internal IReadOnlyDictionary<UnitKey, UnitState> Units => this.units;

    /// <summary>Processes one queued item: a client message, the end of the input or a worker product.</summary>
    /// <param name="item">The item.</param>
    /// <param name="now">The current time in milliseconds.</param>
    public void Process(object item, long now)
    {
        if (this.Exited)
        {
            return;
        }

        switch (item)
        {
            case LspMessage message:
                this.OnMessage(message, now);
                break;
            case InvalidFrame invalid:
                this.sender.Error(null, invalid.Code, invalid.Message);
                break;
            case CheckTimer:
                break;
            case EndOfInput:
                this.Dispose();
                break;
            case CommitRequest commit:
                this.OnCommit(commit);
                break;
            case DerivationDone derivation:
                this.OnDerivation(derivation);
                break;
            case UnitDone done:
                this.OnUnit(done.Result);
                break;
            case ProductsDone products:
                this.OnProducts(products);
                break;
            case CheckDone done:
                this.OnCheckDone(done);
                break;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.Exited)
        {
            return;
        }

        this.Exited = true;
        this.shutdown.Cancel();
        this.shutdown.Dispose();
        foreach (var document in this.documents.Values)
        {
            document.Text.Dispose();
        }

        this.documents.Clear();
        this.documentsByUri.Clear();
    }

    /// <summary>Starts the pending check when its deadline has passed and the worker is free (SPEC 23.4.6).</summary>
    /// <param name="now">The current time in milliseconds.</param>
    public void Tick(long now)
    {
        if (this.Deadline is not { } deadline || deadline > now || !this.initialized)
        {
            return;
        }

        this.eligibleAt = null;
        this.checkBase = this.store.Begin();
        var baseTexts = new Dictionary<SourceIdentity, string?>();
        var views = new OpenDocumentView[this.documents.Count];
        var index = 0;
        foreach (var document in this.documents.Values)
        {
            var entry = this.store.Find(InputKey.File(document.Identity));
            if (entry is { Marked: true })
            {
                baseTexts.Add(document.Identity, document.Desynchronized ? null : document.Text.ToString());
            }

            views[index++] = new(document.Identity, document.Role, entry?.LastEvent ?? 0);
        }

        var results = new Dictionary<UnitKey, UnitResult?>(this.units.Count);
        foreach (var (key, unit) in this.units)
        {
            results.Add(key, unit.Result);
        }

        var settings = this.settings;
        var runner = this.Runner ?? ((_, _, plan, source, token) => WorkspaceCheck.RunCheck(this.kimigayo, plan, settings.Debug, source, token));
        this.startWorker(new(new CheckStart
        {
            Base = this.checkBase,
            Targets = this.store.RevalidationTargets(),
            BaseTexts = baseTexts,
            Documents = views,
            Settings = settings,
            RelatedInformation = this.relatedInformationSupported,
            Projects = new(this.projects),
            Units = results,
            ChangedAfterBase = () => this.store.ChangedAfterBase,
            Post = this.post,
            Kimigayo = this.kimigayo,
            Runner = runner,
            CancellationToken = this.shutdown.Token,
        }));
    }

    private static DocumentRole? RoleOf(SourceIdentity identity)
        => identity.Value.EndsWith(".kimi", StringComparison.OrdinalIgnoreCase) ? DocumentRole.Source :
            identity.Value.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase) ? DocumentRole.Project : null;

    // The receive loop read the parameters; a failure is raised here, where the method's rules decide how to report it.
    private static T? Read<T>(LspMessage message)
        where T : class
        => message.ParamsError is { } error ? throw new JsonException(error) : (T?)message.Params;

    // SPEC 23.4.8: two non-empty ranges intersect as half-open ranges; a point lies within a range with the end included; two points are equal.
    private static bool Matches(SourceRange sent, SourceRange requested)
    {
        var sentEmpty = sent.Start.CompareTo(sent.End) == 0;
        var requestedEmpty = requested.Start.CompareTo(requested.End) == 0;
        if (!sentEmpty && !requestedEmpty)
        {
            return sent.Start.CompareTo(requested.End) < 0 && requested.Start.CompareTo(sent.End) < 0;
        }

        if (sentEmpty && requestedEmpty)
        {
            return sent.Start.CompareTo(requested.Start) == 0;
        }

        var (point, range) = sentEmpty ? (sent.Start, requested) : (requested.Start, sent);
        return range.Start.CompareTo(point) <= 0 && point.CompareTo(range.End) <= 0;
    }

    // The candidates two contributors agree on, in the first contributor's order; null when none.
    private static LspRepair[]? Agree(LspRepair[]? agreed, LspRepair[]? other)
    {
        if (agreed is null || other is null)
        {
            return null;
        }

        List<LspRepair>? kept = null;
        foreach (var candidate in agreed)
        {
            if (Array.IndexOf(other, candidate) >= 0)
            {
                (kept ??= []).Add(candidate);
            }
        }

        return kept?.ToArray();
    }

    private void OnMessage(LspMessage message, long now)
    {
        if (message.Method is null)
        {
            if (message.Id is null)
            {
                this.sender.Error(null, -32600, "Invalid request.");
                return;
            }

            if (message.Error is { } error)
            {
                this.Log(2, "The client rejected a server request: " + error);
            }

            return; // A response to a server request.
        }

        var isRequest = message.Id is not null;
        if (!isRequest && message.Method is LspMethods.Initialize or LspMethods.Shutdown)
        {
            return; // Lifecycle requests must carry an ID.
        }

        if (message.Method == LspMethods.Exit)
        {
            if (isRequest)
            {
                this.sender.Error(message.Id, -32600, "exit must be a notification.");
                return;
            }

            this.Dispose();
            return;
        }

        if (this.shutdownRequested)
        {
            if (isRequest)
            {
                this.sender.Error(message.Id, -32600, "The server is shutting down.");
            }

            return;
        }

        if (!this.initialized && message.Method != LspMethods.Initialize)
        {
            if (isRequest)
            {
                this.sender.Error(message.Id, -32002, "The server is not initialized.");
            }

            return;
        }

        if (isRequest && message.Method is LspMethods.Initialized or LspMethods.DidOpen or LspMethods.DidChange or LspMethods.DidClose or LspMethods.DidChangeWatchedFiles)
        {
            this.sender.Error(message.Id, -32600, "The method must be a notification.");
            return;
        }

        try
        {
            switch (message.Method)
            {
                case LspMethods.Initialize:
                    this.OnInitialize(message);
                    break;
                case LspMethods.Initialized:
                    this.OnInitialized(now);
                    break;
                case LspMethods.Shutdown:
                    this.shutdownRequested = true;
                    this.eligibleAt = null;
                    this.shutdown.Cancel();
                    this.sender.Result<object>(message.Id, null, null);
                    break;
                case LspMethods.DidOpen:
                    this.OnDidOpen(Read<DidOpenTextDocumentParams>(message), now);
                    break;
                case LspMethods.DidChange:
                    this.OnDidChange(Read<DidChangeTextDocumentParams>(message), now);
                    break;
                case LspMethods.DidClose:
                    this.OnDidClose(Read<DidCloseTextDocumentParams>(message), now);
                    break;
                case LspMethods.DidChangeWatchedFiles:
                    this.OnWatchedFiles(Read<DidChangeWatchedFilesParams>(message), now);
                    break;
                case LspMethods.CodeAction:
                    this.OnCodeAction(message);
                    break;
                case LspMethods.Hover:
                    this.OnHover(message);
                    break;
                default:
                    if (isRequest)
                    {
                        this.sender.Error(message.Id, -32601, "Method not found: " + message.Method);
                    }

                    break;
            }
        }
        catch (JsonException ex)
        {
            if (isRequest)
            {
                this.sender.Error(message.Id, -32602, "Invalid params: " + ex.Message);
            }
            else
            {
                this.Log(2, "Invalid params of " + message.Method + ": " + ex.Message);
            }
        }
    }

    private void OnInitialize(LspMessage message)
    {
        if (this.initialized)
        {
            this.sender.Error(message.Id, -32600, "initialize was already received.");
            return;
        }

        var parameters = Read<InitializeParams>(message);
        this.settings = LspSettings.Parse(parameters?.InitializationOptions, x => this.Log(2, x));
        this.watchSupported = parameters?.Capabilities?.Workspace?.DidChangeWatchedFiles?.DynamicRegistration == true;
        this.relatedInformationSupported = parameters?.Capabilities?.TextDocument?.PublishDiagnostics?.RelatedInformation == true;

        // SPEC 23.4.8: code actions need code action literals and versioned document changes, both optional client features.
        this.codeActionsSupported = parameters?.Capabilities?.TextDocument?.CodeAction?.CodeActionLiteralSupport is not null &&
            parameters.Capabilities?.Workspace?.WorkspaceEdit?.DocumentChanges == true;
        this.initialized = true;
        var result = new InitializeResult
        {
            Capabilities = new()
            {
                TextDocumentSync = new() { OpenClose = true, Change = 2 },
                CodeActionProvider = this.codeActionsSupported ? new() { CodeActionKinds = ["quickfix"] } : null,
            },
            ServerInfo = new() { Name = "Kimi Language Server", Version = CompilerRelease.Version },
        };
        this.sender.Result(message.Id, result, LspJsonContext.Default.InitializeResult);
    }

    private void OnInitialized(long now)
    {
        if (this.clientInitialized)
        {
            return;
        }

        this.clientInitialized = true;
        if (this.watchSupported)
        {
            var registration = new Registration
            {
                Id = WatchRegistration,
                Method = LspMethods.DidChangeWatchedFiles,
                RegisterOptions = new() { Watchers = [new() { GlobPattern = "**/*.kimi" }, new() { GlobPattern = "**/*.kimiproj" }, new() { GlobPattern = "**/*.kimi.lock.json" }] },
            };
            this.sender.Request(++this.nextRequestId, LspMethods.RegisterCapability, new RegistrationParams { Registrations = [registration] }, LspJsonContext.Default.RegistrationParams);
        }

        if (this.settings.SelectedProjects.Length != 0)
        {
            this.Schedule(now);
        }
    }

    private void OnDidOpen(DidOpenTextDocumentParams? parameters, long now)
    {
        if (parameters?.TextDocument is not { } item || !SourceIdentity.TryFromUri(item.Uri, out var identity) || RoleOf(identity) is not { } role)
        {
            return;
        }

        var text = item.Text ?? throw new JsonException("textDocument.text must be a string.");
        if (this.documents.TryGetValue(identity, out var existing))
        {
            existing.Text.Replace(text); // A repeated open is a full-text event and resynchronizes the document.
            existing.Desynchronized = false;
            existing.Version = item.Version;
            this.Event(now, false, InputKey.File(identity));
            return;
        }

        var document = new OpenDocument(item.Uri, identity, role, new(text), item.Version);
        this.documents.Add(identity, document);
        this.documentsByUri.TryAdd(item.Uri, document);
        this.OpenCloseEvent(now, false, identity);
    }

    private void OnDidChange(DidChangeTextDocumentParams? parameters, long now)
    {
        if (parameters is null)
        {
            return;
        }

        var changes = parameters.ContentChanges;
        var readable = changes is not null && !changes.Exists(static change => change?.Text is null);
        if (parameters.TextDocument is { } identifier && this.FindChanged(identifier.Uri) is { } document)
        {
            if (identifier.Version <= document.Version)
            {
                this.Log(4, $"Non-increasing version {identifier.Version} after {document.Version}: {identifier.Uri}");
            }

            document.Version = identifier.Version;
            if (changes is null)
            {
                this.Desynchronize(document);
            }

            foreach (var change in changes ?? [])
            {
                if (change?.Text is not { } replacement)
                {
                    this.Desynchronize(document); // The client applied a change the server cannot read.
                }
                else if (change.Range is not { } range)
                {
                    document.Text.Replace(replacement);
                    document.Desynchronized = false;
                }
                else if (!document.Desynchronized && !document.Text.TryApply(range.Start, range.End, replacement))
                {
                    this.Desynchronize(document);
                }
            }

            this.Event(now, false, InputKey.File(document.Identity));
        }

        if (!readable)
        {
            throw new JsonException("contentChanges must contain text changes with string text.");
        }
    }

    private void OnDidClose(DidCloseTextDocumentParams? parameters, long now)
    {
        if (parameters?.TextDocument is not { } identifier || this.FindOpen(identifier.Uri) is not { } document)
        {
            return;
        }

        this.documents.Remove(document.Identity);
        this.documentsByUri.Remove(document.Uri);
        document.Text.Dispose();
        this.OpenCloseEvent(now, false, document.Identity);
    }

    // SPEC 23.4.8: answered from the diagnostics last sent for the URI, without a check and without reading context.diagnostics.
    // Candidates are returned only while every contributor's result is valid; an edited, reopened or desynchronized document has
    // no candidates until the next adoption restores them.
    private void OnCodeAction(LspMessage message)
    {
        var parameters = Read<CodeActionParams>(message);
        List<CodeAction>? actions = null;
        if (this.codeActionsSupported && parameters is not null && this.FindOpen(parameters.TextDocument?.Uri) is { } document &&
            (parameters.Context?.Only is not { } only || Array.IndexOf(only, "quickfix") >= 0) &&
            this.ContributorsValid(document.Identity) && this.repairs.TryGetValue(document.Identity, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (Matches(candidate.Diagnostic.Range, parameters.Range))
                {
                    (actions ??= []).Add(new()
                    {
                        Title = candidate.Title,
                        Diagnostics = [candidate.Diagnostic],
                        Edit = new() { DocumentChanges = [new() { TextDocument = new() { Uri = document.Uri, Version = document.Version }, Edits = candidate.Edits }] },
                    });
                }
            }
        }

        this.sender.Result(message.Id, actions?.ToArray() ?? [], LspJsonContext.Default.CodeActionArray);
    }

    private bool ContributorsValid(SourceIdentity uri)
    {
        if (!this.contributors.TryGetValue(uri, out var set) || set.Count == 0)
        {
            return false;
        }

        foreach (var key in set)
        {
            if (this.units.GetValueOrDefault(key)?.Result is not { Valid: true })
            {
                return false;
            }
        }

        return true;
    }

    private void OnHover(LspMessage message)
    {
        var parameters = Read<HoverParams>(message);
        EffectHover? agreed = null;
        if (parameters is not null && this.FindOpen(parameters.TextDocument?.Uri) is { Desynchronized: false } document &&
            this.ContributorsValid(document.Identity) && this.contributors.TryGetValue(document.Identity, out var contributors))
        {
            var first = true;
            foreach (var key in contributors)
            {
                EffectHover? found = null;
                foreach (var candidate in this.units[key].Result!.Output.Hover?.Effects ?? [])
                {
                    if (candidate.Source == document.Identity && candidate.Range.Start.CompareTo(parameters.Position) <= 0 &&
                        candidate.Range.End.CompareTo(parameters.Position) > 0 &&
                        (found is null || (candidate.Range.Start.CompareTo(found.Range.Start) >= 0 && candidate.Range.End.CompareTo(found.Range.End) <= 0)))
                    {
                        found = candidate;
                    }
                }

                if (!first && agreed != found)
                {
                    agreed = null;
                    break;
                }

                agreed = found;
                first = false;
            }
        }

        var result = agreed is null ? null : new HoverResult { Contents = new() { Value = agreed.Text }, Range = agreed.Range };
        this.sender.Result(message.Id, result, LspJsonContext.Default.HoverResult);
    }

    private void OnWatchedFiles(DidChangeWatchedFilesParams? parameters, long now)
    {
        if (parameters is not null && (parameters.Changes is null || parameters.Changes.Exists(static change => change is null)))
        {
            throw new JsonException("changes must contain file events.");
        }

        foreach (var change in parameters?.Changes ?? [])
        {
            if (SourceIdentity.TryFromUri(change.Uri, out var identity))
            {
                this.OpenCloseEvent(now, true, identity);
            }
        }
    }

    // An open document is found by the URI it was opened with, so an edit parses no URI; another spelling of the same
    // file still finds it through its identity. A JSON null URI finds nothing.
    private OpenDocument? FindOpen(string? uri)
        => uri is null ? null : this.documentsByUri.GetValueOrDefault(uri) ??
            (SourceIdentity.TryFromUri(uri, out var identity) ? this.documents.GetValueOrDefault(identity) : null);

    private OpenDocument? FindChanged(string? uri)
    {
        var document = this.FindOpen(uri);
        if (document is null && SourceIdentity.TryFromUri(uri, out _))
        {
            this.Log(4, "A change to a document that is not open was ignored: " + uri);
        }

        return document;
    }

    private void Desynchronize(OpenDocument document)
    {
        if (!document.Desynchronized)
        {
            document.Desynchronized = true;
            this.Log(2, "The document is out of sync after an inapplicable change; close and reopen it: " + document.Uri);
        }
    }

    // SPEC 23.4.5: an open, close or watched event marks the file and the listing of its directory that matches it.
    private void OpenCloseEvent(long now, bool watched, SourceIdentity identity)
    {
        if (InputKey.TryGetListing(identity, out var listing))
        {
            this.Event(now, watched, InputKey.File(identity), listing);
        }
        else
        {
            this.Event(now, watched, InputKey.File(identity));
        }
    }

    private void Event(long now, bool watched, params ReadOnlySpan<InputKey> keys)
    {
        this.store.Event(this.checkBase, watched, keys);
        this.Schedule(now);
    }

    private void Schedule(long now)
        => this.eligibleAt = now + this.settings.QuietPeriod;

    // SPEC 23.4.5: commit the comparisons under the base rule and hand the worker the committed snapshot.
    private void OnCommit(CommitRequest commit)
    {
        if (this.shutdownRequested || this.checkBase < 0)
        {
            commit.Committed.TrySetCanceled();
            return;
        }

        var released = new List<DerivedItem>();
        var invalidated = new List<DerivedItem>();
        foreach (var (key, state) in commit.Comparisons)
        {
            this.store.Commit(key, state, this.checkBase, released, invalidated);
        }

        var items = new List<DerivedItem>(this.projects.Values);
        foreach (var unit in this.units.Values)
        {
            if (unit.Result is { } result)
            {
                items.Add(result);
            }
        }

        commit.Committed.TrySetResult(this.store.Snapshot(this.checkBase, items));
        foreach (var item in released)
        {
            if (item is UnitResult result)
            {
                this.Reconsider(result.Reports.Keys);
            }
        }
    }

    // SPEC 23.4.3 and 23.4.4: adopt the loaded projects, then change the required units only by an adopted derivation.
    private void OnDerivation(DerivationDone derivation)
    {
        this.undeterminedOwners.Clear();
        this.derivationAdopted = false;
        if (this.shutdownRequested)
        {
            return;
        }

        this.undeterminedOwners.UnionWith(derivation.KeepOwners);
        foreach (var project in derivation.NewProjects)
        {
            if (!this.store.TryRegister(project, this.checkBase))
            {
                this.undeterminedOwners.Add(project.Path); // Its units rest on a discarded item, so they stay as they are.
                continue;
            }

            if (this.projects.Remove(project.Path, out var previous))
            {
                this.store.Unregister(previous);
            }

            this.projects.Add(project.Path, project);
        }

        if (!this.store.TryRegister(derivation.Record, this.checkBase))
        {
            return; // Discovery read an input changed after the base; the next check derives again.
        }

        if (this.discovery is not null)
        {
            this.store.Unregister(this.discovery);
        }

        this.discovery = derivation.Record;
        this.derivationAdopted = true;
        List<SourceIdentity>? unreached = null;
        foreach (var path in this.projects.Keys)
        {
            if (!derivation.Reached.Contains(path))
            {
                (unreached ??= []).Add(path);
            }
        }

        foreach (var path in unreached ?? [])
        {
            this.projects.Remove(path, out var project);
            this.store.Unregister(project!);
        }

        var productOwners = new HashSet<SourceIdentity>();
        foreach (var key in derivation.Required)
        {
            if (this.undeterminedOwners.Contains(key.Owner))
            {
                continue;
            }

            if (!this.units.ContainsKey(key))
            {
                this.units.Add(key, new(key));
            }

            if (key.Kind == UnitKind.Product)
            {
                productOwners.Add(key.Owner);
            }
        }

        List<UnitKey>? retired = null;
        foreach (var key in this.units.Keys)
        {
            // A test unit of an owner with product units is decided when its product results are adopted.
            if (!derivation.Required.Contains(key) && !this.undeterminedOwners.Contains(key.Owner) &&
                !(key.Kind == UnitKind.Test && productOwners.Contains(key.Owner)))
            {
                (retired ??= []).Add(key);
            }
        }

        foreach (var key in retired ?? [])
        {
            this.Retire(key);
        }
    }

    // SPEC 23.4.4: the adopted product results of the current check decide whether the test unit is required.
    private void OnProducts(ProductsDone products)
    {
        if (this.shutdownRequested || !this.derivationAdopted || !products.Decided || this.undeterminedOwners.Contains(products.Owner))
        {
            return;
        }

        foreach (var unit in this.units.Values)
        {
            if (unit.Key.Owner == products.Owner && unit.Key.Kind == UnitKind.Product && unit.Result is not { Valid: true })
            {
                return; // Only adopted product results may decide whether a test unit retires.
            }
        }

        List<UnitKey>? retired = null;
        foreach (var key in this.units.Keys)
        {
            if (key.Owner == products.Owner && key.Kind == UnitKind.Test && !(products.RequiresTest && key == products.TestKey))
            {
                (retired ??= []).Add(key);
            }
        }

        foreach (var key in retired ?? [])
        {
            this.Retire(key);
        }

        if (products.RequiresTest && !this.units.ContainsKey(products.TestKey))
        {
            this.units.Add(products.TestKey, new(products.TestKey));
        }
    }

    private void OnUnit(UnitResult result)
    {
        if (this.shutdownRequested || !this.units.TryGetValue(result.Key, out var unit) || !this.store.TryRegister(result, this.checkBase))
        {
            return; // SPEC 23.4.6: a stale result, or one of a unit outside the adopted required set, is discarded.
        }

        var previous = unit.Result;
        if (previous is not null)
        {
            this.store.Unregister(previous);
        }

        unit.Result = result;
        if (result.Output.HoverFault is { } hoverFault)
        {
            this.Log(2, $"Hover information unavailable for {result.Key.Owner}: {hoverFault}");
        }

        if (previous?.Output.Outcome != result.Output.Outcome)
        {
            this.Log(3, $"{result.Key.Owner} ({result.Key.Kind}{(result.Key.Target.Length == 0 ? string.Empty : " " + result.Key.Target)}): {result.Output.Outcome}");
        }

        var uris = this.changedReports; // Reconsidering never adopts a result, so the set is free again afterwards.
        uris.UnionWith(result.Reports.Keys);
        if (previous is not null)
        {
            foreach (var uri in previous.Reports.Keys)
            {
                if (uris.Add(uri))
                {
                    this.RemoveContributor(uri, result.Key);
                }
            }
        }

        foreach (var uri in result.Reports.Keys)
        {
            if (!this.contributors.TryGetValue(uri, out var set))
            {
                set = [];
                this.contributors.Add(uri, set);
            }

            set.Add(result.Key);
        }

        this.Reconsider(uris);
        uris.Clear();
    }

    private void OnCheckDone(CheckDone done)
    {
        this.checkBase = -1;
        if (done.Failure is { } failure && !this.shutdownRequested)
        {
            this.Log(1, "The workspace check failed: " + failure);
        }

        this.store.Release(key => key.Kind == InputKind.File && this.documents.ContainsKey(key.Identity));
    }

    private void Retire(UnitKey key)
    {
        if (!this.units.Remove(key, out var unit) || unit.Result is not { } result)
        {
            return;
        }

        this.store.Unregister(result);
        foreach (var uri in result.Reports.Keys)
        {
            this.RemoveContributor(uri, key);
        }

        this.Reconsider(result.Reports.Keys);
    }

    private void RemoveContributor(SourceIdentity uri, UnitKey key)
    {
        if (this.contributors.TryGetValue(uri, out var set) && set.Remove(key) && set.Count == 0)
        {
            this.contributors.Remove(uri);
        }
    }

    // SPEC 23.4.7: send a URI only when all its contributors are valid; the payload merges them in contributor order.
    private void Reconsider(IEnumerable<SourceIdentity> uris)
    {
        if (this.shutdownRequested)
        {
            return;
        }

        foreach (var uri in uris)
        {
            var contributions = this.contributions;
            contributions.Clear();
            var ready = true;
            LspRepair[]? agreed = null;
            var first = true;
            if (this.contributors.TryGetValue(uri, out var set))
            {
                foreach (var key in set)
                {
                    if (this.units.GetValueOrDefault(key)?.Result is not { } result || !result.Valid)
                    {
                        ready = false;
                        break;
                    }

                    if (result.Reports[uri] is { Length: > 0 } contribution)
                    {
                        contributions.Add((key, contribution));
                    }

                    // SPEC 23.4.8: a candidate of the URI is one that every contributor sends with equal values.
                    var candidates = result.Repairs.GetValueOrDefault(uri);
                    agreed = first ? candidates : Agree(agreed, candidates);
                    first = false;
                }
            }

            if (!ready)
            {
                this.repairs.Remove(uri);
                continue;
            }

            if (agreed is { Length: > 0 })
            {
                this.repairs[uri] = agreed;
            }
            else
            {
                this.repairs.Remove(uri);
            }

            LspDiagnostic[] payload = [];
            if (contributions.Count == 1)
            {
                payload = contributions[0].Payload; // A single contributor is already ordered.
            }
            else if (contributions.Count > 1)
            {
                contributions.Sort(static (x, y) => x.Key.CompareTo(y.Key));
                this.orderedContributions.Clear();
                foreach (var (_, contribution) in contributions)
                {
                    this.orderedContributions.Add(contribution);
                }

                payload = WorkspaceCheck.Merge(this.orderedContributions);
            }

            var document = this.documents.GetValueOrDefault(uri);
            var version = document?.Version;
            if (payload.Length == 0)
            {
                if (!this.sent.Remove(uri))
                {
                    continue; // A URI never sent counts as sent empty.
                }
            }
            else if (this.sent.TryGetValue(uri, out var last) && last.Version == version && last.Payload.AsSpan().SequenceEqual(payload))
            {
                continue;
            }
            else
            {
                this.sent[uri] = (payload, version);
            }

            var parameters = new PublishDiagnosticsParams { Uri = document?.Uri ?? uri.ToUri(), Version = version, Diagnostics = payload };
            this.sender.Notify(LspMethods.PublishDiagnostics, parameters, LspJsonContext.Default.PublishDiagnosticsParams);
        }
    }

    private void Log(int type, string message)
        => this.sender.Notify(LspMethods.LogMessage, new LogMessageParams { Type = type, Message = message }, LspJsonContext.Default.LogMessageParams);

    /// <summary>One required unit and its latest adopted result.</summary>
    /// <param name="key">The unit key.</param>
    internal sealed class UnitState(UnitKey key)
    {
        /// <summary>Gets the unit key.</summary>
        public UnitKey Key { get; } = key;

        /// <summary>Gets or sets the latest adopted result.</summary>
        public UnitResult? Result { get; set; }
    }

    private sealed class OpenDocument(string uri, SourceIdentity identity, DocumentRole role, TextDocument text, int version)
    {
        public string Uri { get; } = uri;

        public SourceIdentity Identity { get; } = identity;

        public DocumentRole Role { get; } = role;

        public TextDocument Text { get; } = text;

        public int Version { get; set; } = version;

        public bool Desynchronized { get; set; }
    }
}
