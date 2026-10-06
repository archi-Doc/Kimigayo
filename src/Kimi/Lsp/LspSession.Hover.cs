// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;

namespace Kimi.Lsp;

internal sealed partial class LspSession
{
    private readonly HashSet<SourceIdentity> hoverTestDecisions = [];
    private readonly List<UnitState> hoverParticipants = [];
    private bool hoverMarkdown;
    private bool hoverDirty;

    private void DiscardHover()
    {
        foreach (var document in this.documents.Values)
        {
            document.Hover = null;
        }
    }

    // Refresh eligibility only on applied state changes. Requests do no membership scans, validity scans or compiler work.
    private void RefreshHover()
    {
        this.hoverDirty = false;
        try
        {
            var pending = this.checkBase >= 0 || this.eligibleAt is not null;
            foreach (var document in this.documents.Values)
            {
                if (document.Role != DocumentRole.Source || document.Desynchronized)
                {
                    document.Hover = null;
                    continue;
                }

                var members = this.hoverParticipants;
                members.Clear();
                var determined = this.discovery is { Valid: true } && this.undeterminedOwners.Count == 0;
                foreach (var project in this.projects.Values)
                {
                    if (project.Members.Contains(document.Identity) && !project.Valid)
                    {
                        determined = false;
                    }
                }

                foreach (var unit in this.units.Values)
                {
                    var project = this.projects.GetValueOrDefault(unit.Key.Owner);
                    var owns = project is null ? unit.Key.Owner == document.Identity : project.Members.Contains(document.Identity);
                    if (!owns)
                    {
                        continue; // Reading a source as a dependency never makes a consumer participate.
                    }

                    if (project?.HasTestSources != true && !this.hoverTestDecisions.Contains(unit.Key.Owner))
                    {
                        determined = false;
                    }

                    if (unit.Key.Kind != UnitKind.Product || project is null || project.ProductMembers.Contains(document.Identity))
                    {
                        members.Add(unit);
                    }
                }

                if (determined)
                {
                    members.Sort(static (a, b) => a.Key.CompareTo(b.Key));
                    var previous = document.Hover;
                    var sameMembers = previous is not null && previous.Participants.Length == members.Count;
                    if (sameMembers)
                    {
                        for (var i = 0; i < members.Count; i++)
                        {
                            sameMembers &= members[i].Key == previous!.Participants[i].Key;
                        }
                    }

                    if (!sameMembers)
                    {
                        document.Hover = null;
                    }

                    var ready = members.Count != 0;
                    foreach (var unit in members)
                    {
                        ready &= unit.Result is { Valid: true, Output.Outcome: CheckOutcome.Completed, Output.Hover: { } data } &&
                            data.Documents.ContainsKey(document.Identity);
                    }

                    if (ready)
                    {
                        var sameResults = sameMembers;
                        for (var i = 0; sameResults && i < members.Count; i++)
                        {
                            sameResults = previous!.Participants[i].Generation == members[i].Result!.Id;
                        }

                        if (sameResults)
                        {
                            previous!.Revalidated();
                        }
                        else
                        {
                            var adopted = new HoverParticipant[members.Count];
                            for (var i = 0; i < members.Count; i++)
                            {
                                var unit = members[i];
                                adopted[i] = new(unit.Key, unit.Result!.Id, unit.Result.Output.Hover!.Documents[document.Identity]);
                            }

                            document.Hover = new(adopted, this.hoverMarkdown);
                        }

                        continue;
                    }
                }

                if (!pending)
                {
                    document.Hover = null;
                }
                else if (document.Hover is { } retained)
                {
                    retained.Current = false;
                }
            }
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or PendingInputException))
        {
            this.DiscardHover();
            this.Log(2, "Hover adoption failed: " + ex.Message);
        }
        finally
        {
            this.hoverParticipants.Clear(); // The scratch list must never retain retired results or closed-file indexes.
        }
    }
}
