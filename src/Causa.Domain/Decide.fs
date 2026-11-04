// Owner: domain. decide: command -> events or refusals. Pure; validation is the catalog's rules.
namespace Causa.Domain.Content

[<RequireQualifiedAccess>]
module Decide =
    let private refuse field message : Result<ContentEvent list, Refusal list> = Error [ { Field = field; Message = message } ]

    /// Validate each (key, rule, value); all failures are reported together.
    let private validate (checks: (string * FieldRule * string) list) =
        let results = checks |> List.map (fun (key, rule, value) -> key, Rules.check rule value)

        match results |> List.choose (fun (key, r) -> match r with Error m -> Some { Field = key; Message = m } | Ok _ -> None) with
        | [] -> Ok(results |> List.map (fun (key, r) -> key, (match r with Ok v -> v | Error _ -> "")) |> Map.ofList)
        | refusals -> Error refusals

    let private details (input: DetailsInput) =
        validate
            [ "title", Rules.title, input.Title
              "problemStatement", Rules.problemStatement, input.ProblemStatement
              "question", Rules.question, input.Question ]
        |> Result.map (fun v -> { Details.Title = v["title"]; ProblemStatement = v["problemStatement"]; Question = v["question"] })

    let private observation id (input: ObservationInput) =
        validate [ "observationText", Rules.observationText, input.Text; "observationSource", Rules.observationSource, input.Source ]
        |> Result.map (fun v -> { Observation.Id = id; Text = v["observationText"]; Source = v["observationSource"] })

    let private hypothesis id (input: HypothesisInput) =
        validate
            [ "hypothesisStatement", Rules.hypothesisStatement, input.Statement
              "hypothesisExplanation", Rules.hypothesisExplanation, input.Explanation ]
        |> Result.map (fun v -> { Hypothesis.Id = id; Statement = v["hypothesisStatement"]; Explanation = v["hypothesisExplanation"] })

    let private evidence id (input: EvidenceInput) =
        validate
            [ "evidenceLabel", Rules.evidenceLabel, input.Label
              "evidenceContent", Rules.evidenceContent, input.Content
              "evidenceSource", Rules.evidenceSource, input.Source
              "evidenceUrl", Rules.evidenceUrl, input.Url ]
        |> Result.map (fun v ->
            { Evidence.Id = id
              Label = v["evidenceLabel"]
              Content = v["evidenceContent"]
              Source = v["evidenceSource"]
              Url = v["evidenceUrl"] })

    let private assessment kind rationale =
        validate [ "relationshipKind", Rules.relationshipKind, kind; "rationale", Rules.rationale, rationale ]
        |> Result.bind (fun v ->
            match RelationshipKind.parse v["relationshipKind"] with
            | Some k -> Ok(k, v["rationale"])
            | None -> Error [ { Field = "relationshipKind"; Message = "Relationship must be chosen." } ])

    let private exists (items: 'a list) (idOf: 'a -> string) id = items |> List.exists (fun x -> idOf x = id)

    let private unchanged field = refuse field "Nothing changed."

    let decide (context: DecisionContext) (current: ContentInvestigation option) (command: ContentCommand) : Result<ContentEvent list, Refusal list> =
        match current, command with
        | None, Start(identifier, input) ->
            validate [ "identifier", Rules.identifier, identifier ]
            |> Result.bind (fun v -> details input |> Result.map (fun d -> [ Started(v["identifier"].ToLowerInvariant(), context.At, d) ]))
        | Some _, Start _ -> refuse "identifier" "This investigation has already started."
        | None, _ -> refuse "investigation" "No investigation is open."
        | Some inv, EditDetails input ->
            details input
            |> Result.bind (fun d -> if d = inv.Details then unchanged "problemStatement" else Ok [ DetailsEdited d ])
        | Some _, RecordObservation input -> observation context.NewId input |> Result.map (fun o -> [ ObservationRecorded o ])
        | Some inv, EditObservation(id, input) ->
            match inv.Observations |> List.tryFind (fun o -> o.Id = id) with
            | None -> refuse "observationText" "That observation does not exist."
            | Some before ->
                observation id input
                |> Result.bind (fun o -> if o = before then unchanged "observationText" else Ok [ ObservationEdited o ])
        | Some _, RecordHypothesis input -> hypothesis context.NewId input |> Result.map (fun h -> [ HypothesisRecorded h ])
        | Some inv, EditHypothesis(id, input) ->
            match inv.Hypotheses |> List.tryFind (fun h -> h.Id = id) with
            | None -> refuse "hypothesisStatement" "That hypothesis does not exist."
            | Some before ->
                hypothesis id input
                |> Result.bind (fun h -> if h = before then unchanged "hypothesisStatement" else Ok [ HypothesisEdited h ])
        | Some _, AdmitEvidence input -> evidence context.NewId input |> Result.map (fun e -> [ EvidenceAdmitted e ])
        | Some inv, EditEvidence(id, input) ->
            match inv.Evidence |> List.tryFind (fun e -> e.Id = id) with
            | None -> refuse "evidenceLabel" "That evidence item does not exist."
            | Some before ->
                evidence id input
                |> Result.bind (fun e -> if e = before then unchanged "evidenceLabel" else Ok [ EvidenceEdited e ])
        | Some inv, LinkEvidence input ->
            if not (exists inv.Evidence (fun e -> e.Id) input.EvidenceId) then
                refuse "linkEvidence" "Choose an evidence item that exists."
            elif not (exists inv.Hypotheses (fun h -> h.Id) input.HypothesisId) then
                refuse "linkHypothesis" "Choose a hypothesis that exists."
            else
                match inv.Relationships |> List.tryFind (fun r -> r.EvidenceId = input.EvidenceId && r.HypothesisId = input.HypothesisId) with
                | Some existing ->
                    refuse "linkHypothesis" $"This evidence is already assessed against this hypothesis ({RelationshipKind.label existing.Kind}). Reassess the existing link instead."
                | None ->
                    assessment input.Kind input.Rationale
                    |> Result.map (fun (kind, rationale) ->
                        [ RelationshipRecorded
                              { Id = context.NewId
                                EvidenceId = input.EvidenceId
                                HypothesisId = input.HypothesisId
                                Kind = kind
                                Rationale = rationale } ])
        | Some inv, Reassess(id, kind, rationale) ->
            if not (exists inv.Relationships (fun r -> r.Id) id) then refuse "relationshipKind" "That link does not exist."
            else
                assessment kind rationale
                |> Result.bind (fun (k, r) ->
                    match inv.Relationships |> List.tryFind (fun x -> x.Id = id) with
                    | Some existing when existing.Kind = k && existing.Rationale = r -> unchanged "relationshipKind"
                    | _ -> Ok [ RelationshipReassessed(id, k, r) ])
        | Some inv, Withdraw(id, reason) ->
            if not (exists inv.Relationships (fun r -> r.Id) id) then refuse "rationale" "That link does not exist."
            else
                validate [ "rationale", Rules.rationale, reason ]
                |> Result.map (fun v -> [ RelationshipWithdrawn(id, v["rationale"]) ])
        | Some _, RecordConclusion statement ->
            validate [ "conclusionStatement", Rules.conclusionStatement, statement ]
            |> Result.map (fun v -> [ ConclusionRecorded { Id = context.NewId; Statement = v["conclusionStatement"] } ])
        | Some inv, ReviseConclusion(id, statement) ->
            match inv.Conclusions |> List.tryFind (fun c -> c.Id = id) with
            | None -> refuse "conclusionStatement" "That conclusion does not exist."
            | Some before ->
                validate [ "conclusionStatement", Rules.conclusionStatement, statement ]
                |> Result.bind (fun v ->
                    if v["conclusionStatement"] = before.Statement then unchanged "conclusionStatement"
                    else Ok [ ConclusionRevised { Id = id; Statement = v["conclusionStatement"] } ])
