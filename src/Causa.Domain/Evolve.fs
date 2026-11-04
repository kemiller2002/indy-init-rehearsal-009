// Owner: domain. evolve and replay: the investigation is a fold of its history. Pure and total.
namespace Causa.Domain.Content

[<RequireQualifiedAccess>]
module Evolve =
    let private replaceBy (idOf: 'a -> string) (item: 'a) (items: 'a list) =
        items |> List.map (fun x -> if idOf x = idOf item then item else x)

    let private checkAll (checks: (FieldRule * string) list) =
        checks
        |> List.tryPick (fun (rule, value) -> match Rules.check rule value with Error m -> Some m | Ok _ -> None)
        |> function
            | Some m -> Error m
            | None -> Ok()

    /// The catalog's value rules hold for every value an event carries (a saved document is not trusted).
    let private admissible (event: ContentEvent) =
        let details (d: Details) = [ Rules.title, d.Title; Rules.problemStatement, d.ProblemStatement; Rules.question, d.Question ]

        match event with
        | Started(id, _, d) -> checkAll ((Rules.identifier, id) :: details d)
        | DetailsEdited d -> checkAll (details d)
        | ObservationRecorded o | ObservationEdited o -> checkAll [ Rules.observationText, o.Text; Rules.observationSource, o.Source ]
        | HypothesisRecorded h | HypothesisEdited h -> checkAll [ Rules.hypothesisStatement, h.Statement ]
        | EvidenceAdmitted e | EvidenceEdited e -> checkAll [ Rules.evidenceLabel, e.Label; Rules.evidenceSource, e.Source; Rules.evidenceUrl, e.Url ]
        | RelationshipRecorded r -> checkAll [ Rules.rationale, r.Rationale ]
        | RelationshipReassessed(_, _, rationale) -> checkAll [ Rules.rationale, rationale ]
        | RelationshipWithdrawn(_, reason) -> checkAll [ Rules.rationale, reason ]
        | ConclusionRecorded c | ConclusionRevised c -> checkAll [ Rules.conclusionStatement, c.Statement ]

    let private known (what: string) (ids: string list) (id: string) (sequence: int) =
        if List.contains id ids then Ok() else Error $"event {sequence} refers to {what} {id}, which does not exist"

    /// The identifiers an event refers to must exist (edits, links, reassessments, withdrawals).
    let private referencesExist (inv: ContentInvestigation) (record: EventRecord) =
        let n = record.Sequence
        let ids (xs: 'a list) (idOf: 'a -> string) = xs |> List.map idOf

        match record.Event with
        | ObservationEdited o -> known "observation" (ids inv.Observations (fun x -> x.Id)) o.Id n
        | HypothesisEdited h -> known "hypothesis" (ids inv.Hypotheses (fun x -> x.Id)) h.Id n
        | EvidenceEdited e -> known "evidence" (ids inv.Evidence (fun x -> x.Id)) e.Id n
        | RelationshipRecorded r ->
            known "evidence" (ids inv.Evidence (fun x -> x.Id)) r.EvidenceId n
            |> Result.bind (fun () -> known "hypothesis" (ids inv.Hypotheses (fun x -> x.Id)) r.HypothesisId n)
        | RelationshipReassessed(id, _, _)
        | RelationshipWithdrawn(id, _) -> known "link" (ids inv.Relationships (fun x -> x.Id)) id n
        | ConclusionRevised c -> known "conclusion" (ids inv.Conclusions (fun x -> x.Id)) c.Id n
        | _ -> Ok()

    /// Apply one recorded event. An event that does not fit the current state is an error, never ignored.
    let evolve (current: ContentInvestigation option) (record: EventRecord) : Result<ContentInvestigation, string> =
        let keep (inv: ContentInvestigation) = { inv with History = inv.History @ [ record ] }

        match admissible record.Event |> Result.mapError (fun m -> $"event {record.Sequence}: {m}"), current with
        | Error m, _ -> Error m
        | Ok(), Some inv when (referencesExist inv record |> Result.isError) -> referencesExist inv record |> Result.map (fun () -> inv)
        | Ok(), _ ->

        match current, record.Event with
        | None, Started(id, createdAt, details) ->
            Ok
                { Id = id
                  CreatedAt = createdAt
                  OriginalStatement = details.ProblemStatement
                  Details = details
                  State = Open
                  Observations = []
                  Hypotheses = []
                  Evidence = []
                  Relationships = []
                  Conclusions = []
                  History = [ record ] }
        | None, _ -> Error $"event {record.Sequence} precedes the investigation's start"
        | Some _, Started _ -> Error $"event {record.Sequence} starts an investigation that already started"
        | Some inv, DetailsEdited d -> Ok(keep { inv with Details = d })
        | Some inv, ObservationRecorded o -> Ok(keep { inv with Observations = inv.Observations @ [ o ] })
        | Some inv, ObservationEdited o -> Ok(keep { inv with Observations = replaceBy (fun x -> x.Id) o inv.Observations })
        | Some inv, HypothesisRecorded h -> Ok(keep { inv with Hypotheses = inv.Hypotheses @ [ h ] })
        | Some inv, HypothesisEdited h -> Ok(keep { inv with Hypotheses = replaceBy (fun x -> x.Id) h inv.Hypotheses })
        | Some inv, EvidenceAdmitted e -> Ok(keep { inv with Evidence = inv.Evidence @ [ e ] })
        | Some inv, EvidenceEdited e -> Ok(keep { inv with Evidence = replaceBy (fun x -> x.Id) e inv.Evidence })
        | Some inv, RelationshipRecorded r -> Ok(keep { inv with Relationships = inv.Relationships @ [ r ] })
        | Some inv, RelationshipReassessed(id, kind, rationale) ->
            Ok(keep { inv with Relationships = inv.Relationships |> List.map (fun r -> if r.Id = id then { r with Kind = kind; Rationale = rationale } else r) })
        | Some inv, RelationshipWithdrawn(id, _) ->
            Ok(keep { inv with Relationships = inv.Relationships |> List.filter (fun r -> r.Id <> id) })
        | Some inv, ConclusionRecorded c -> Ok(keep { inv with Conclusions = inv.Conclusions @ [ c ] })
        | Some inv, ConclusionRevised c -> Ok(keep { inv with Conclusions = replaceBy (fun x -> x.Id) c inv.Conclusions })

    /// Rebuild an investigation from its history; sequences must be 1..n.
    let replay (records: EventRecord list) : Result<ContentInvestigation, string> =
        match records with
        | [] -> Error "the history is empty"
        | _ ->
            records
            |> List.mapi (fun index record -> index + 1, record)
            |> List.fold
                (fun state (expected, record) ->
                    state
                    |> Result.bind (fun inv ->
                        if record.Sequence <> expected then Error $"event sequence {record.Sequence} where {expected} was expected"
                        else evolve inv record |> Result.map Some))
                (Ok None)
            |> Result.bind (fun inv -> match inv with Some i -> Ok i | None -> Error "the history is empty")

    /// Decide and record: the events a command produces, appended with provenance.
    let execute (context: DecisionContext) (current: ContentInvestigation option) (command: ContentCommand) =
        Decide.decide context current command
        |> Result.bind (fun events ->
            let start = current |> Option.map (fun i -> i.History.Length) |> Option.defaultValue 0

            events
            |> List.mapi (fun i e -> { Sequence = start + i + 1; Provenance = { Actor = context.Actor; At = context.At }; Event = e })
            |> List.fold (fun state record -> state |> Result.bind (fun inv -> evolve inv record |> Result.map Some)) (Ok current)
            |> Result.mapError (fun m -> [ { Field = "investigation"; Message = m } ])
            |> Result.bind (fun inv -> match inv with Some i -> Ok i | None -> Error [ { Field = "investigation"; Message = "nothing recorded" } ]))
