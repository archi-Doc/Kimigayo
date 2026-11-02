// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Checking;

namespace Kimi.Lsp;

// The locations the server sends: the state owner alone maps a document's identity to the URI it sends for it, the spelling the client
// opened the document with, or the canonical spelling (SourceIdentity.ToUri) of a document the client never named (DECISIONS R5-M180).
// FindOpen is the inverse. Every sent location is spelled when it is sent: a publication's URI, its related locations, a code action's
// diagnostics and hover links; one reaching the sender unspelled is a broken contract.
internal sealed partial class LspSession
{
    private readonly Dictionary<SourceIdentity, (LspDiagnostic[] Placed, LspDiagnostic[] Sent, int? Version, string Uri, long Spelling)> sent = new();
    private long spelling;

    // The one check before the sender: the JSON context omits a null URI, so an unspelled location would be sent silently.
    private static LspDiagnostic[] Spelled(LspDiagnostic[] diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            foreach (var related in diagnostic.RelatedInformation ?? [])
            {
                if (related.Location.Uri is null)
                {
                    throw new UnreachableException("Internal invariant: a related location reached the sender unspelled (LspSession.Spell).");
                }
            }
        }

        return diagnostics;
    }

    private string UriOf(SourceIdentity document)
        => this.documents.GetValueOrDefault(document)?.Uri ?? document.ToUri();

    // An open or a close changes UriOf: payloads are spelled again when next reconsidered, and Hover renderings are forgotten.
    private void Respell()
    {
        this.spelling++;
        foreach (var document in this.documents.Values)
        {
            document.Hover?.ForgetRenderings();
        }
    }

    // SPEC 23.4: a document's payload is sent unless it equals what was last sent for it; a document never sent counts as sent empty.
    // A list sent under another spelling of the document (opened or closed since) is cleared there first. An unchanged placed payload
    // under unchanged spellings keeps what was sent, so only a payload that may be sent is spelled.
    private void Publish(SourceIdentity document, LspDiagnostic[] payload)
    {
        var version = this.documents.GetValueOrDefault(document)?.Version;
        var uri = this.UriOf(document);
        var known = this.sent.Remove(document, out var last);
        if (known && last.Uri != uri)
        {
            this.Notify(last.Uri, null, []);
            known = false;
        }

        var spelled = known && last.Spelling == this.spelling && last.Placed.AsSpan().SequenceEqual(payload) ? last.Sent : this.Spell(payload);
        if (payload.Length != 0)
        {
            this.sent.Add(document, (payload, spelled, version, uri, this.spelling));
        }

        if (known ? payload.Length == 0 || last.Version != version || !last.Sent.AsSpan().SequenceEqual(spelled) : payload.Length != 0)
        {
            this.Notify(uri, version, spelled);
        }
    }

    private void Notify(string uri, int? version, LspDiagnostic[] payload)
    {
        var parameters = new PublishDiagnosticsParams { Uri = uri, Version = version, Diagnostics = Spelled(payload) };
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
