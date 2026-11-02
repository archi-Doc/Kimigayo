// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;

namespace Kimi.Lsp;

// The locations the server sends: the state owner alone maps a document's identity to the URI it sends for it, the spelling the client
// opened the document with, or the canonical spelling of a document the client never named (DECISIONS R5-M180). FindOpen is the
// inverse. Every sent location is spelled when it is sent: a publication's URI, its related locations, a code action's diagnostics and
// hover links.
internal sealed partial class LspSession
{
    private readonly Dictionary<SourceIdentity, (LspDiagnostic[] Payload, int? Version, string Uri)> sent = new();

    private string UriOf(SourceIdentity document)
        => this.NamedUri(document) ?? document.ToUri();

    private string? NamedUri(SourceIdentity document)
        => this.documents.GetValueOrDefault(document)?.Uri;

    // SPEC 23.4: a document's payload is sent unless it equals what was last sent for it; a document never sent counts as sent empty.
    // A list sent under another spelling of the document (opened or closed since) is cleared there first.
    private void Publish(SourceIdentity document, LspDiagnostic[] payload)
    {
        var version = this.documents.GetValueOrDefault(document)?.Version;
        var uri = this.UriOf(document);
        payload = this.Spell(payload);
        var known = this.sent.Remove(document, out var last);
        if (known && last.Uri != uri)
        {
            this.Notify(last.Uri, null, []);
            known = false;
        }

        if (payload.Length != 0)
        {
            this.sent.Add(document, (payload, version, uri));
        }

        if (known ? payload.Length == 0 || last.Version != version || !last.Payload.AsSpan().SequenceEqual(payload) : payload.Length != 0)
        {
            this.Notify(uri, version, payload);
        }
    }

    private void Notify(string uri, int? version, LspDiagnostic[] payload)
    {
        var parameters = new PublishDiagnosticsParams { Uri = uri, Version = version, Diagnostics = payload };
        this.sender.Notify(LspMethods.PublishDiagnostics, parameters, LspJsonContext.Default.PublishDiagnosticsParams);
    }

    // A payload without related locations is sent as placed; another is copied with each location's URI.
    private LspDiagnostic[] Spell(LspDiagnostic[] payload)
    {
        LspDiagnostic[]? spelled = null;
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i].RelatedInformation is not null)
            {
                spelled ??= (LspDiagnostic[])payload.Clone();
                spelled[i] = this.Spell(payload[i]);
            }
        }

        return spelled ?? payload;
    }

    private LspDiagnostic Spell(LspDiagnostic diagnostic)
        => diagnostic.RelatedInformation is not { } related ? diagnostic : diagnostic with
        {
            RelatedInformation = Array.ConvertAll(related, x => x with { Location = x.Location with { Uri = this.UriOf(x.Location.Document) } }),
        };
}
