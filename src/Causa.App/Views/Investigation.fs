// Owner: ui. Projection: Model -> the complete view state the page binds. Views never decide legality.
namespace Causa.App.Content

open System
open System.Globalization
open Causa.Domain.Content
open Causa.Persistence.Content

[<RequireQualifiedAccess>]
module Projection =
    let private time (t: DateTimeOffset) = t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)

    let private short (text: string) = if text.Length > 80 then text.Substring(0, 77) + "…" else text

    let private eventText (inv: ContentInvestigation) (r: EventRecord) =
        let evidenceLabel id = inv.Evidence |> List.tryFind (fun e -> e.Id = id) |> Option.map (fun e -> e.Label) |> Option.defaultValue id

        match r.Event with
        | Started(_, _, d) -> $"Investigation started: \"{short d.ProblemStatement}\""
        | DetailsEdited d -> $"Details edited: title \"{short d.Title}\", problem statement \"{short d.ProblemStatement}\", question \"{short d.Question}\""
        | ObservationRecorded o -> $"Observation {o.Id} recorded: \"{short o.Text}\""
        | ObservationEdited o -> $"Observation {o.Id} edited to: \"{short o.Text}\""
        | HypothesisRecorded h -> $"Hypothesis {h.Id} recorded: \"{short h.Statement}\""
        | HypothesisEdited h -> $"Hypothesis {h.Id} edited to: \"{short h.Statement}\""
        | EvidenceAdmitted e -> $"Evidence {e.Id} recorded: \"{short e.Label}\""
        | EvidenceEdited e -> $"Evidence {e.Id} edited to: \"{short e.Label}\""
        | RelationshipRecorded rel -> $"Link {rel.Id}: \"{short (evidenceLabel rel.EvidenceId)}\" {RelationshipKind.code rel.Kind} hypothesis {rel.HypothesisId}. Rationale: \"{short rel.Rationale}\""
        | RelationshipReassessed(id, kind, rationale) -> $"Link {id} reassessed as {RelationshipKind.code kind}. Rationale: \"{short rationale}\""
        | RelationshipWithdrawn(id, reason) -> $"Link {id} withdrawn. Reason: \"{short reason}\""
        | ConclusionRecorded c -> $"Conclusion {c.Id} recorded: \"{short c.Statement}\""
        | ConclusionRevised c -> $"Conclusion {c.Id} revised to: \"{short c.Statement}\""

    let private saveLabel (session: Session) =
        let unsaved = session.Investigation.History.Length <> session.SavedLength || session.Sha.IsNone

        match session.Save with
        | Saving -> "Saving…"
        | Saved when not unsaved -> "Saved to GitHub"
        | SaveFailed m -> $"Save failed. {m}"
        | SaveAuthFailed m -> $"Authentication failed. {m}"
        | SaveConflict m -> $"Conflict. {m}"
        | _ when session.Sha.IsNone -> "Not saved"
        | _ -> "Unsaved changes"

    let private saveCode (session: Session) =
        match session.Save with
        | Saving -> "saving"
        | Saved when session.Investigation.History.Length = session.SavedLength -> "saved"
        | SaveFailed _ -> "failed"
        | SaveAuthFailed _ -> "auth-failed"
        | SaveConflict _ -> "conflict"
        | _ -> "unsaved"

    let private fieldErrors (model: Model) =
        [ "identifier"; "title"; "problemStatement"; "question"; "observationText"; "observationSource"; "hypothesisStatement"
          "hypothesisExplanation"; "evidenceLabel"; "evidenceContent"; "evidenceSource"; "evidenceUrl"; "linkEvidence"
          "linkHypothesis"; "relationshipKind"; "rationale"; "conclusionStatement"; "connection"; "investigation" ]
        |> List.collect (fun f ->
            let message = model.Errors |> Map.tryFind f |> Option.defaultValue ""
            [ View.text $"error_{f}" message; View.flag $"hasError_{f}" (message <> ""); View.text $"invalid_{f}" (if message <> "" then "true" else "false") ])

    let private drafts (model: Model) =
        ("identifier" :: Update.editorFields) @ [ "owner"; "repository"; "branch" ]
        |> List.map (fun f -> View.text $"draft_{f}" (Update.draft f model))

    let private editorFlags (model: Model) =
        let instance =
            match model.Editor with
            | ObservationEditor(Some id)
            | HypothesisEditor(Some id)
            | EvidenceEditor(Some id)
            | ConclusionEditor(Some id)
            | ReassessEditor id
            | WithdrawEditor id -> id
            | _ -> "new"

        let heading =
            match model.Editor with
            | NoEditor -> ""
            | DetailsEditor -> "Edit investigation details"
            | ObservationEditor None -> "Record an observation"
            | ObservationEditor _ -> "Edit observation"
            | HypothesisEditor None -> "Record a hypothesis"
            | HypothesisEditor _ -> "Edit hypothesis"
            | EvidenceEditor None -> "Record evidence"
            | EvidenceEditor _ -> "Edit evidence"
            | LinkEditor -> "Link evidence to a hypothesis"
            | ReassessEditor _ -> "Reassess link"
            | WithdrawEditor _ -> "Withdraw link"
            | ConclusionEditor None -> "Record a conclusion"
            | ConclusionEditor _ -> "Revise conclusion"

        let kind = Update.draft "relationshipKind" model

        [ View.flag "editingDetails" (model.Editor = DetailsEditor)
          View.flag "editingObservation" (match model.Editor with ObservationEditor _ -> true | _ -> false)
          View.flag "editingHypothesis" (match model.Editor with HypothesisEditor _ -> true | _ -> false)
          View.flag "editingEvidence" (match model.Editor with EvidenceEditor _ -> true | _ -> false)
          View.flag "editingLink" (match model.Editor with LinkEditor | ReassessEditor _ -> true | _ -> false)
          View.flag "editingNewLink" (model.Editor = LinkEditor)
          View.flag "editingWithdraw" (match model.Editor with WithdrawEditor _ -> true | _ -> false)
          View.flag "editingConclusion" (match model.Editor with ConclusionEditor _ -> true | _ -> false)
          View.flag "noEditor" (model.Editor = NoEditor)
          View.text "editorInstance" instance
          View.text "editorHeading" heading
          View.text "editorName"
              (match model.Editor with
               | NoEditor -> "none"
               | DetailsEditor -> "details"
               | ObservationEditor _ -> "observation"
               | HypothesisEditor _ -> "hypothesis"
               | EvidenceEditor _ -> "evidence"
               | LinkEditor -> "link"
               | ReassessEditor _ -> "reassess"
               | WithdrawEditor _ -> "withdraw"
               | ConclusionEditor _ -> "conclusion")
          View.flag "kindSupports" (kind = "supports")
          View.flag "kindContradicts" (kind = "contradicts")
          View.flag "kindConsistentWith" (kind = "consistent-with")
          View.flag "kindInconclusive" (kind = "inconclusive") ]

    let private connection (model: Model) =
        let status, connected, failed =
            match model.Connection with
            | Disconnected -> "Not connected to GitHub.", false, ""
            | Connecting -> "Connecting to GitHub…", false, ""
            | Connected(d, _) -> $"Connected: {Destination.describe d}", true, ""
            | ConnectFailed m -> "Not connected to GitHub.", false, m

        let destination =
            match model.Connection with
            | Connected(d, _) -> $"Saves to {Destination.describe d}"
            | _ -> "Not connected: connect to GitHub to save. Investigations are saved to the repository you connect."

        [ View.text "connectionStatus" status
          View.flag "connected" connected
          View.flag "showConnect" (not connected)
          View.text "connectError" failed
          View.flag "hasConnectError" (failed <> "")
          View.flag "connecting" (model.Connection = Connecting)
          View.text "destination" destination ]

    let private workspace (model: Model) =
        let session = model.Session |> Option.filter (fun s -> match model.Screen with Workspace id -> s.Investigation.Id = id | _ -> false)

        match session with
        | None ->
            let loadMessage =
                match model.Load, model.Screen with
                | Loading _, _ -> "Loading the investigation from GitHub…"
                | LoadFailed(_, m), _ -> m
                | _, Workspace _ when model.Connection = Disconnected -> "Connect to GitHub to open this investigation."
                | _ -> ""

            [ View.flag "hasInvestigation" false
              View.text "loadMessage" loadMessage
              View.flag "hasLoadMessage" (loadMessage <> "")
              View.flag "loadFailed" (match model.Load with LoadFailed _ -> true | _ -> false) ]
            @ ([ "invId"; "invTitle"; "invStatement"; "invOriginalStatement"; "invQuestion"; "invCreated"; "invState"; "saveState"; "saveCode" ]
               |> List.map (fun k -> View.text k ""))
            @ [ View.flag "saveDisabled" true; View.flag "canRetry" false; View.flag "isConflict" false; View.flag "hasQuestion" false ]
            @ ([ "observations"; "hypotheses"; "evidence"; "relationships"; "conclusions"; "history"; "evidenceOptions"; "hypothesisOptions" ]
               |> List.map (fun k -> View.items k []))
            @ [ View.flag "hasObservations" false; View.flag "hasHypotheses" false; View.flag "hasEvidence" false
                View.flag "hasRelationships" false; View.flag "hasConclusions" false; View.flag "canLink" false ]
        | Some s ->
            let inv = s.Investigation
            let evidenceLabel id = inv.Evidence |> List.tryFind (fun e -> e.Id = id) |> Option.map (fun e -> e.Label) |> Option.defaultValue "(missing)"
            let hypothesisStatement id = inv.Hypotheses |> List.tryFind (fun h -> h.Id = id) |> Option.map (fun h -> h.Statement) |> Option.defaultValue "(missing)"

            let linksOf (select: Relationship -> bool) (describe: Relationship -> string) =
                inv.Relationships |> List.filter select |> List.map describe |> fun xs -> String.Join(" · ", xs)

            let code = saveCode s

            [ View.flag "hasInvestigation" true
              View.text "loadMessage" ""
              View.flag "hasLoadMessage" false
              View.flag "loadFailed" false
              View.text "invId" inv.Id
              View.text "invTitle" (if inv.Details.Title = "" then "Untitled investigation" else inv.Details.Title)
              View.text "invStatement" inv.Details.ProblemStatement
              View.text "invOriginalStatement" inv.OriginalStatement
              View.text "invQuestion" inv.Details.Question
              View.flag "hasQuestion" (inv.Details.Question <> "")
              View.text "invCreated" (time inv.CreatedAt)
              View.text "invState" (InvestigationState.label inv.State)
              View.text "saveState" (saveLabel s)
              View.text "saveCode" code
              View.flag "saveDisabled" (code = "saving" || code = "saved")
              View.flag "canRetry" (code = "failed" || code = "auth-failed")
              View.flag "isConflict" (code = "conflict")
              View.items "observations" (inv.Observations |> List.map (fun o -> [ "id", Text o.Id; "text", Text o.Text; "source", Text o.Source; "hasSource", Flag(o.Source <> "") ]))
              View.flag "hasObservations" (not inv.Observations.IsEmpty)
              View.items
                  "hypotheses"
                  (inv.Hypotheses
                   |> List.map (fun h ->
                       let links = linksOf (fun r -> r.HypothesisId = h.Id) (fun r -> $"{RelationshipKind.label r.Kind}: {evidenceLabel r.EvidenceId}")
                       [ "id", Text h.Id; "statement", Text h.Statement; "explanation", Text h.Explanation
                         "hasExplanation", Flag(h.Explanation <> ""); "links", Text links; "hasLinks", Flag(links <> "") ]))
              View.flag "hasHypotheses" (not inv.Hypotheses.IsEmpty)
              View.items
                  "evidence"
                  (inv.Evidence
                   |> List.map (fun e ->
                       let links = linksOf (fun r -> r.EvidenceId = e.Id) (fun r -> $"{RelationshipKind.label r.Kind}: {hypothesisStatement r.HypothesisId}")
                       [ "id", Text e.Id; "label", Text e.Label; "content", Text e.Content; "hasContent", Flag(e.Content <> "")
                         "source", Text e.Source; "hasSource", Flag(e.Source <> ""); "url", Text e.Url; "hasUrl", Flag(e.Url <> "")
                         "links", Text links; "hasLinks", Flag(links <> "") ]))
              View.flag "hasEvidence" (not inv.Evidence.IsEmpty)
              View.items
                  "relationships"
                  (inv.Relationships
                   |> List.map (fun r ->
                       [ "id", Text r.Id; "evidenceId", Text r.EvidenceId; "hypothesisId", Text r.HypothesisId
                         "evidence", Text(evidenceLabel r.EvidenceId); "hypothesis", Text(hypothesisStatement r.HypothesisId)
                         "kind", Text(RelationshipKind.code r.Kind); "kindLabel", Text(RelationshipKind.label r.Kind); "rationale", Text r.Rationale ]))
              View.flag "hasRelationships" (not inv.Relationships.IsEmpty)
              View.items "conclusions" (inv.Conclusions |> List.map (fun c -> [ "id", Text c.Id; "statement", Text c.Statement ]))
              View.flag "hasConclusions" (not inv.Conclusions.IsEmpty)
              View.items
                  "history"
                  (inv.History
                   |> List.rev
                   |> List.map (fun r -> [ "seq", Number r.Sequence; "at", Text(time r.Provenance.At); "actor", Text r.Provenance.Actor; "text", Text(eventText inv r) ]))
              View.items "evidenceOptions" (inv.Evidence |> List.map (fun e -> [ "id", Text e.Id; "label", Text e.Label ]))
              View.items "hypothesisOptions" (inv.Hypotheses |> List.map (fun h -> [ "id", Text h.Id; "label", Text(short h.Statement) ]))
              View.flag "canLink" (not inv.Evidence.IsEmpty && not inv.Hypotheses.IsEmpty) ]

    let private home (model: Model) =
        let ids, message =
            match model.Index with
            | IndexNotLoaded -> [], "Connect to GitHub to list saved investigations."
            | IndexLoading -> [], "Loading saved investigations…"
            | IndexLoaded [] -> [], "No saved investigations yet."
            | IndexLoaded ids -> ids, ""
            | IndexFailed m -> [], m

        [ View.items "investigations" (ids |> List.map (fun id -> [ "id", Text id; "href", Text(Screen.format (Workspace id)) ]))
          View.text "indexMessage" message
          View.flag "hasIndexMessage" (message <> "") ]

    let project (model: Model) : (string * ViewValue) list =
        let screenName =
            match model.Screen with
            | Home -> "home"
            | StartScreen -> "start"
            | Workspace _ -> "workspace"
            | Missing _ -> "missing"

        [ View.text "screen" screenName
          View.flag "onHome" (model.Screen = Home)
          View.flag "onStart" (model.Screen = StartScreen)
          View.flag "onWorkspace" (match model.Screen with Workspace _ -> true | _ -> false)
          View.flag "onMissing" (match model.Screen with Missing _ -> true | _ -> false)
          View.text "notice" model.Notice
          View.text "startIdentifier" (model.Drafts |> Map.tryFind "identifier" |> Option.defaultValue model.StartIdentifier) ]
        @ connection model
        @ home model
        @ workspace model
        @ editorFlags model
        @ drafts model
        @ fieldErrors model
