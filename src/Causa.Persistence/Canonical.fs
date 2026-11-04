// Owner: persistence. The saved investigation document (causa.investigation/1): the full content history,
// encoded and decoded strictly. A document that does not decode is refused, never partially loaded.
namespace Causa.Persistence.Content

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes
open Causa.Domain.Content

[<RequireQualifiedAccess>]
module Document =
    [<Literal>]
    let Schema = "causa.investigation/1"

    let private str (value: string) : JsonNode = JsonValue.Create(value: string) |> nonNull :> JsonNode
    let private obj (fields: (string * JsonNode) list) : JsonNode =
        let o = JsonObject()
        fields |> List.iter (fun (k, v) -> o[k] <- v)
        o :> JsonNode

    let private time (t: DateTimeOffset) = t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)

    let private details (d: Details) =
        [ "title", str d.Title; "problemStatement", str d.ProblemStatement; "question", str d.Question ]

    let private eventFields (event: ContentEvent) : (string * JsonNode) list =
        match event with
        | Started(id, createdAt, d) -> [ "type", str "started"; "id", str id; "createdAt", str (time createdAt) ] @ details d
        | DetailsEdited d -> ("type", str "details-edited") :: details d
        | ObservationRecorded o
        | ObservationEdited o ->
            let kind = match event with ObservationRecorded _ -> "observation-recorded" | _ -> "observation-edited"
            [ "type", str kind; "id", str o.Id; "text", str o.Text; "source", str o.Source ]
        | HypothesisRecorded h
        | HypothesisEdited h ->
            let kind = match event with HypothesisRecorded _ -> "hypothesis-recorded" | _ -> "hypothesis-edited"
            [ "type", str kind; "id", str h.Id; "statement", str h.Statement; "explanation", str h.Explanation ]
        | EvidenceAdmitted e
        | EvidenceEdited e ->
            let kind = match event with EvidenceAdmitted _ -> "evidence-admitted" | _ -> "evidence-edited"
            [ "type", str kind; "id", str e.Id; "label", str e.Label; "content", str e.Content; "source", str e.Source; "url", str e.Url ]
        | RelationshipRecorded r ->
            [ "type", str "relationship-recorded"
              "id", str r.Id
              "evidenceId", str r.EvidenceId
              "hypothesisId", str r.HypothesisId
              "kind", str (RelationshipKind.code r.Kind)
              "rationale", str r.Rationale ]
        | RelationshipReassessed(id, kind, rationale) ->
            [ "type", str "relationship-reassessed"; "id", str id; "kind", str (RelationshipKind.code kind); "rationale", str rationale ]
        | RelationshipWithdrawn(id, reason) -> [ "type", str "relationship-withdrawn"; "id", str id; "reason", str reason ]
        | ConclusionRecorded c -> [ "type", str "conclusion-recorded"; "id", str c.Id; "statement", str c.Statement ]
        | ConclusionRevised c -> [ "type", str "conclusion-revised"; "id", str c.Id; "statement", str c.Statement ]

    let private record (r: EventRecord) =
        obj ([ "sequence", (JsonValue.Create(r.Sequence) :> JsonNode); "actor", str r.Provenance.Actor; "at", str (time r.Provenance.At) ] @ eventFields r.Event)

    /// Encode an investigation's history as the canonical document text.
    let encode (inv: ContentInvestigation) : string =
        let events = JsonArray()
        inv.History |> List.iter (fun r -> events.Add(record r))
        let root = obj [ "schema", str Schema; "id", str inv.Id; "events", (events :> JsonNode) ]
        root.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    // ------------------------------------------------------------------ decoding

    let private field (name: string) (o: JsonObject) : Result<string, string> =
        match o[name] with
        | :? JsonValue as v ->
            match v.GetValueKind() with
            | JsonValueKind.String ->
                Ok(v.GetValue<string>())
            | _ -> Error $"field '{name}' is not text"
        | _ -> Error $"field '{name}' is missing"

    let private timeField name o =
        field name o
        |> Result.bind (fun s ->
            match DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) with
            | true, t -> Ok t
            | _ -> Error $"field '{name}' is not a timestamp")

    let private kindField o =
        field "kind" o
        |> Result.bind (fun k -> match RelationshipKind.parse k with Some kind -> Ok kind | None -> Error $"relationship kind '{k}' is not in the vocabulary")

    type private ResultBuilder() =
        member _.Bind(r, f) = Result.bind f r
        member _.Return v = Ok v
        member _.ReturnFrom r = r

    let private result = ResultBuilder()

    let private detailsOf o =
        result {
            let! t = field "title" o
            let! p = field "problemStatement" o
            let! q = field "question" o
            return { Details.Title = t; ProblemStatement = p; Question = q }
        }

    let private eventOf (o: JsonObject) : Result<ContentEvent, string> =
        result {
            let! kind = field "type" o

            match kind with
            | "started" ->
                let! id = field "id" o
                let! createdAt = timeField "createdAt" o
                let! d = detailsOf o
                return Started(id, createdAt, d)
            | "details-edited" ->
                let! d = detailsOf o
                return DetailsEdited d
            | "observation-recorded"
            | "observation-edited" ->
                let! id = field "id" o
                let! text = field "text" o
                let! source = field "source" o
                let value = { Observation.Id = id; Text = text; Source = source }
                return (if kind = "observation-recorded" then ObservationRecorded value else ObservationEdited value)
            | "hypothesis-recorded"
            | "hypothesis-edited" ->
                let! id = field "id" o
                let! statement = field "statement" o
                let! explanation = field "explanation" o
                let value = { Hypothesis.Id = id; Statement = statement; Explanation = explanation }
                return (if kind = "hypothesis-recorded" then HypothesisRecorded value else HypothesisEdited value)
            | "evidence-admitted"
            | "evidence-edited" ->
                let! id = field "id" o
                let! label = field "label" o
                let! content = field "content" o
                let! source = field "source" o
                let! url = field "url" o
                let value = { Evidence.Id = id; Label = label; Content = content; Source = source; Url = url }
                return (if kind = "evidence-admitted" then EvidenceAdmitted value else EvidenceEdited value)
            | "relationship-recorded" ->
                let! id = field "id" o
                let! e = field "evidenceId" o
                let! h = field "hypothesisId" o
                let! k = kindField o
                let! rationale = field "rationale" o
                return RelationshipRecorded { Id = id; EvidenceId = e; HypothesisId = h; Kind = k; Rationale = rationale }
            | "relationship-reassessed" ->
                let! id = field "id" o
                let! k = kindField o
                let! rationale = field "rationale" o
                return RelationshipReassessed(id, k, rationale)
            | "relationship-withdrawn" ->
                let! id = field "id" o
                let! reason = field "reason" o
                return RelationshipWithdrawn(id, reason)
            | "conclusion-recorded"
            | "conclusion-revised" ->
                let! id = field "id" o
                let! statement = field "statement" o
                let value = { Conclusion.Id = id; Statement = statement }
                return (if kind = "conclusion-recorded" then ConclusionRecorded value else ConclusionRevised value)
            | other -> return! Error $"unknown event type '{other}'"
        }

    let private recordOf (node: JsonNode | null) : Result<EventRecord, string> =
        match node with
        | :? JsonObject as o ->
            result {
                let! sequence =
                    match o["sequence"] with
                    | :? JsonValue as v ->
                        if v.GetValueKind() = JsonValueKind.Number then
                            try Ok(v.GetValue<int>()) with _ -> Error "event sequence is not a whole number"
                        else Error "event sequence is not a number"
                    | _ -> Error "event sequence is missing"
                let! actor = field "actor" o
                let! at = timeField "at" o
                let! event = eventOf o
                return { Sequence = sequence; Provenance = { Actor = actor; At = at }; Event = event }
            }
        | _ -> Error "an event is not an object"

    /// Decode and replay a saved document. Any defect is an error naming it; nothing is partially loaded.
    let decode (expectedId: string) (text: string) : Result<ContentInvestigation, string> =
        try
            match JsonNode.Parse text |> Option.ofObj with
            | None -> Error "the document is empty"
            | Some node ->
            match node with
            | :? JsonObject as root ->
                result {
                    let! schema = field "schema" root
                    do! (if schema = Schema then Ok() else Error $"schema '{schema}' is not {Schema}")
                    let! id = field "id" root
                    do! (if id = expectedId then Ok() else Error $"the document is for investigation {id}, not {expectedId}")
                    let! events =
                        match root["events"] with
                        | :? JsonArray as a ->
                            a
                            |> Seq.fold (fun acc n -> acc |> Result.bind (fun xs -> recordOf n |> Result.map (fun r -> r :: xs))) (Ok [])
                            |> Result.map List.rev
                        | _ -> Error "the document has no event list"
                    let! inv = Evolve.replay events
                    do! (if inv.Id = id then Ok() else Error "the history starts a different investigation")
                    return inv
                }
            | _ -> Error "the document is not a JSON object"
        with :? JsonException as ex ->
            Error $"the document is not valid JSON ({ex.Message})"
