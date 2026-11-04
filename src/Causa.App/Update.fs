// Owner: ui. The engine's state machine: Model, messages, and a pure update that returns effects as data.
namespace Causa.App.Content

open System
open Causa.Domain.Content
open Causa.Persistence.Content

type Editor =
    | NoEditor
    | DetailsEditor
    | ObservationEditor of id: string option
    | HypothesisEditor of id: string option
    | EvidenceEditor of id: string option
    | LinkEditor
    | ReassessEditor of relationshipId: string
    | WithdrawEditor of relationshipId: string
    | ConclusionEditor of id: string option

type SaveState =
    | NeverSaved
    | Saving
    | Saved
    | SaveFailed of string
    | SaveAuthFailed of string
    | SaveConflict of string

type Session =
    { Investigation: ContentInvestigation
      /// The blob GitHub holds for this investigation, as last read or written (None: never saved).
      Sha: string option
      /// How many history records the saved copy holds.
      SavedLength: int
      Save: SaveState }

type IndexState =
    | IndexNotLoaded
    | IndexLoading
    | IndexLoaded of string list
    | IndexFailed of string

type LoadState =
    | NotLoading
    | Loading of string
    | LoadFailed of id: string * message: string

type Purpose =
    | CheckRepository of Destination * Credential
    | ListIndex
    | LoadInvestigation of string
    | SaveInvestigation of id: string * length: int

type Effect =
    | Http of correlation: string * GitHubRequest * authorization: string
    | PushUrl of correlation: string * url: string

type Model =
    { Screen: Screen
      Connection: ConnectionStatus
      Index: IndexState
      Load: LoadState
      Session: Session option
      Drafts: Map<string, string>
      Errors: Map<string, string>
      Editor: Editor
      StartIdentifier: string
      Counter: int
      Pending: Map<string, Purpose>
      Notice: string }

type Msg =
    | Initialized of hash: string
    | LocationChanged of hash: string
    | UiEvent of name: string * key: string option * value: string option
    | HttpDone of correlation: string * HttpObservation
    | Ignored

/// What the shell supplies with every message: time and a fresh identifier (keeps update pure).
type Env = { Now: DateTimeOffset; NewId: string }

[<RequireQualifiedAccess>]
module Update =
    let actor = "causa-user"

    let init (env: Env) (hash: string) : Model =
        { Screen = Screen.parse hash
          Connection = Disconnected
          Index = IndexNotLoaded
          Load = NotLoading
          Session = None
          Drafts = Map.ofList [ "owner", "kemiller2002"; "repository", "indy-init-rehearsal-009"; "branch", "causa-data" ]
          Errors = Map.empty
          Editor = NoEditor
          StartIdentifier = env.NewId
          Counter = 0
          Pending = Map.empty
          Notice = "" }

    let draft key (model: Model) = model.Drafts |> Map.tryFind key |> Option.defaultValue ""

    let private request purpose (req: GitHubRequest) (model: Model) =
        match model.Connection, purpose with
        | Connected(_, credential), _
        | _, CheckRepository(_, credential) ->
            let correlation = $"http-{model.Counter + 1}"
            // A distinct query per request keeps the browser's HTTP cache from answering a read with a stale copy.
            let separator = if req.Url.Contains '?' then "&" else "?"
            let url = if req.Method = "GET" then $"{req.Url}{separator}causa-request={correlation}" else req.Url

            { model with Counter = model.Counter + 1; Pending = model.Pending |> Map.add correlation purpose },
            [ Http(correlation, { req with Url = url }, Credential.header credential) ]
        | _ -> { model with Notice = "Connect to GitHub first." }, []

    let private destination (model: Model) =
        match model.Connection with
        | Connected(d, _) -> Some d
        | _ -> None

    let private navigate screen (model: Model) =
        let correlation = $"nav-{model.Counter + 1}"
        { model with Screen = screen; Counter = model.Counter + 1; Editor = NoEditor; Errors = Map.empty }, [ PushUrl(correlation, Screen.format screen) ]

    /// Make sure the workspace's investigation is in memory: load it from GitHub when connected.
    let private ensureLoaded (model: Model, effects: Effect list) =
        match model.Screen, model.Session, destination model with
        | Workspace id, Some s, _ when s.Investigation.Id = id -> model, effects
        | Workspace id, _, Some d when model.Load <> Loading id ->
            let m, e = request (LoadInvestigation id) (GitHub.readInvestigation d id) { model with Load = Loading id }
            m, effects @ e
        | _ -> model, effects

    let private refreshIndex (model: Model, effects: Effect list) =
        match destination model with
        | Some d ->
            let m, e = request ListIndex (GitHub.listInvestigations d) { model with Index = IndexLoading }
            m, effects @ e
        | None -> model, effects

    let private refusalsToErrors (refusals: Refusal list) =
        refusals |> List.fold (fun acc r -> if Map.containsKey r.Field acc then acc else Map.add r.Field r.Message acc) Map.empty

    let private clearDrafts keys (model: Model) =
        { model with Drafts = keys |> List.fold (fun acc k -> Map.remove k acc) model.Drafts }

    let editorFields =
        [ "title"; "problemStatement"; "question"; "observationText"; "observationSource"; "hypothesisStatement"
          "hypothesisExplanation"; "evidenceLabel"; "evidenceContent"; "evidenceSource"; "evidenceUrl"; "linkEvidence"
          "linkHypothesis"; "relationshipKind"; "rationale"; "conclusionStatement" ]

    let private openEditor editor (prefill: (string * string) list) (model: Model) =
        let cleared = clearDrafts editorFields model
        { cleared with
            Editor = editor
            Errors = Map.empty
            Drafts = prefill |> List.fold (fun acc (k, v) -> Map.add k v acc) cleared.Drafts }, []

    let private commandFor (model: Model) : ContentCommand option =
        let d k = draft k model

        match model.Editor with
        | NoEditor -> None
        | DetailsEditor -> Some(EditDetails { Title = d "title"; ProblemStatement = d "problemStatement"; Question = d "question" })
        | ObservationEditor None -> Some(RecordObservation { Text = d "observationText"; Source = d "observationSource" })
        | ObservationEditor(Some id) -> Some(EditObservation(id, { Text = d "observationText"; Source = d "observationSource" }))
        | HypothesisEditor None -> Some(RecordHypothesis { Statement = d "hypothesisStatement"; Explanation = d "hypothesisExplanation" })
        | HypothesisEditor(Some id) -> Some(EditHypothesis(id, { Statement = d "hypothesisStatement"; Explanation = d "hypothesisExplanation" }))
        | EvidenceEditor idOpt ->
            let input = { Label = d "evidenceLabel"; Content = d "evidenceContent"; Source = d "evidenceSource"; Url = d "evidenceUrl" }
            Some(match idOpt with None -> AdmitEvidence input | Some id -> EditEvidence(id, input))
        | LinkEditor ->
            Some(LinkEvidence { EvidenceId = d "linkEvidence"; HypothesisId = d "linkHypothesis"; Kind = d "relationshipKind"; Rationale = d "rationale" })
        | ReassessEditor id -> Some(Reassess(id, d "relationshipKind", d "rationale"))
        | WithdrawEditor id -> Some(Withdraw(id, d "rationale"))
        | ConclusionEditor None -> Some(RecordConclusion(d "conclusionStatement"))
        | ConclusionEditor(Some id) -> Some(ReviseConclusion(id, d "conclusionStatement"))

    let private markEdited (session: Session) (inv: ContentInvestigation) =
        { session with
            Investigation = inv
            Save =
                match session.Save with
                | Saving -> Saving
                | SaveConflict m -> SaveConflict m
                | SaveAuthFailed m -> SaveAuthFailed m
                | _ -> if inv.History.Length = session.SavedLength && session.Sha.IsSome then Saved else NeverSaved }

    let private submitEditor (env: Env) (model: Model) =
        match model.Session with
        | None -> model, []
        | Some session ->
            match commandFor model with
            | None -> model, []
            | Some command ->
                let context = { At = env.Now; Actor = actor; NewId = env.NewId }

                match Evolve.execute context (Some session.Investigation) command with
                | Error refusals -> { model with Errors = refusalsToErrors refusals; Notice = "Not recorded: correct the highlighted field." }, []
                | Ok inv ->
                    let notice =
                        match model.Editor with
                        | ObservationEditor None -> "Observation recorded."
                        | HypothesisEditor None -> "Hypothesis recorded."
                        | EvidenceEditor None -> "Evidence recorded."
                        | LinkEditor -> "Evidence linked to the hypothesis."
                        | ConclusionEditor None -> "Conclusion recorded."
                        | WithdrawEditor _ -> "Link withdrawn."
                        | _ -> "Change recorded."

                    { (clearDrafts editorFields model) with
                        Session = Some(markEdited session inv)
                        Editor = NoEditor
                        Errors = Map.empty
                        Notice = $"{notice} Unsaved changes: select Save to write them to GitHub." }, []

    let private startInvestigation (env: Env) (model: Model) =
        let input = { DetailsInput.Title = draft "title" model; ProblemStatement = draft "problemStatement" model; Question = draft "question" model }
        let identifier = model.Drafts |> Map.tryFind "identifier" |> Option.defaultValue model.StartIdentifier
        let context = { At = env.Now; Actor = actor; NewId = env.NewId }

        match Evolve.execute context None (Start(identifier, input)) with
        | Error refusals -> { model with Errors = refusalsToErrors refusals; Notice = "The investigation was not started: correct the highlighted field." }, []
        | Ok inv ->
            let m = { (clearDrafts ("identifier" :: editorFields) model) with
                        Session = Some { Investigation = inv; Sha = None; SavedLength = 0; Save = NeverSaved }
                        StartIdentifier = env.NewId
                        Notice = "Investigation started. Unsaved: select Save to write it to GitHub." }
            navigate (Workspace inv.Id) m

    let private save (model: Model) =
        match model.Session, destination model with
        | Some { Save = Saving }, _ -> model, [] // a duplicate submission while a save is in flight is ignored
        | Some session, Some d ->
            let inv = session.Investigation
            let document = Document.encode inv
            let req = GitHub.writeInvestigation d inv.Id session.Sha $"Causa: save investigation {inv.Id} ({inv.History.Length} records)" document
            let m, e = request (SaveInvestigation(inv.Id, inv.History.Length)) req model
            { m with Session = Some { session with Save = Saving }; Notice = "Saving to GitHub…" }, e
        | Some _, None -> { model with Notice = "Connect to GitHub to save." }, []
        | None, _ -> model, []

    let private relationship (model: Model) id =
        model.Session |> Option.bind (fun s -> s.Investigation.Relationships |> List.tryFind (fun r -> r.Id = id))

    let private onEvent (env: Env) (name: string) (key: string option) (value: string option) (model: Model) =
        let inv = model.Session |> Option.map (fun s -> s.Investigation)
        let k = key |> Option.defaultValue ""

        match name with
        | n when n.StartsWith("draft:", StringComparison.Ordinal) ->
            let field = n.Substring 6
            { model with Drafts = model.Drafts |> Map.add field (value |> Option.defaultValue ""); Errors = model.Errors |> Map.remove field }, []
        | "connect" ->
            let d = { Owner = (draft "owner" model).Trim(); Repository = (draft "repository" model).Trim(); Branch = (draft "branch" model).Trim() }

            match Credential.create (draft "token" model) with
            | _ when d.Owner = "" || d.Repository = "" || d.Branch = "" ->
                { model with Errors = Map.ofList [ "connection", "Owner, repository and branch are required." ] }, []
            | None -> { model with Errors = Map.ofList [ "connection", "A GitHub token is required." ] }, []
            | Some credential ->
                let m, e = request (CheckRepository(d, credential)) (GitHub.repository d) { model with Connection = Connecting; Errors = Map.empty }
                { m with Drafts = m.Drafts |> Map.remove "token" }, e
        | "disconnect" -> { model with Connection = Disconnected; Index = IndexNotLoaded; Notice = "Disconnected." }, []
        | "newInvestigation" ->
            let m, e = navigate StartScreen (clearDrafts ("identifier" :: editorFields) model)
            { m with Drafts = m.Drafts |> Map.add "identifier" model.StartIdentifier }, e
        | "cancelStart" ->
            let m, e = navigate Home (clearDrafts ("identifier" :: editorFields) model)
            { m with Notice = "Start cancelled. Nothing was created." }, e
        | "startInvestigation" -> startInvestigation env model
        | "home" -> refreshIndex (navigate Home model)
        | "open" when k <> "" -> ensureLoaded (navigate (Workspace k) { model with Load = NotLoading })
        | "refreshIndex" -> refreshIndex (model, [])
        | "save"
        | "retrySave" -> save model
        | "reloadSaved" ->
            match model.Screen with
            | Workspace id -> ensureLoaded ({ model with Session = None; Load = NotLoading; Editor = NoEditor }, [])
            | _ -> model, []
        | "editDetails" ->
            match inv with
            | Some i -> openEditor DetailsEditor [ "title", i.Details.Title; "problemStatement", i.Details.ProblemStatement; "question", i.Details.Question ] model
            | None -> model, []
        | "newObservation" -> openEditor (ObservationEditor None) [] model
        | "editObservation" ->
            match inv |> Option.bind (fun i -> i.Observations |> List.tryFind (fun o -> o.Id = k)) with
            | Some o -> openEditor (ObservationEditor(Some o.Id)) [ "observationText", o.Text; "observationSource", o.Source ] model
            | None -> model, []
        | "newHypothesis" -> openEditor (HypothesisEditor None) [] model
        | "editHypothesis" ->
            match inv |> Option.bind (fun i -> i.Hypotheses |> List.tryFind (fun h -> h.Id = k)) with
            | Some h -> openEditor (HypothesisEditor(Some h.Id)) [ "hypothesisStatement", h.Statement; "hypothesisExplanation", h.Explanation ] model
            | None -> model, []
        | "newEvidence" -> openEditor (EvidenceEditor None) [] model
        | "editEvidence" ->
            match inv |> Option.bind (fun i -> i.Evidence |> List.tryFind (fun e -> e.Id = k)) with
            | Some e -> openEditor (EvidenceEditor(Some e.Id)) [ "evidenceLabel", e.Label; "evidenceContent", e.Content; "evidenceSource", e.Source; "evidenceUrl", e.Url ] model
            | None -> model, []
        | "newLink" -> openEditor LinkEditor [] model
        | "reassess" ->
            match relationship model k with
            | Some r -> openEditor (ReassessEditor r.Id) [] model
            | None -> model, []
        | "withdraw" ->
            match relationship model k with
            | Some r -> openEditor (WithdrawEditor r.Id) [] model
            | None -> model, []
        | "newConclusion" -> openEditor (ConclusionEditor None) [] model
        | "reviseConclusion" ->
            match inv |> Option.bind (fun i -> i.Conclusions |> List.tryFind (fun c -> c.Id = k)) with
            | Some c -> openEditor (ConclusionEditor(Some c.Id)) [ "conclusionStatement", c.Statement ] model
            | None -> model, []
        | "submitEditor" -> submitEditor env model
        | "cancelEditor" -> { (clearDrafts editorFields model) with Editor = NoEditor; Errors = Map.empty; Notice = "Cancelled. Nothing was recorded." }, []
        | _ -> model, []

    let private onHttp (correlation: string) (observation: HttpObservation) (model: Model) =
        match model.Pending |> Map.tryFind correlation with
        | None -> model, []
        | Some purpose ->
            let model = { model with Pending = model.Pending |> Map.remove correlation }
            let outcome = GitHub.classify observation

            match purpose, outcome with
            | CheckRepository(d, credential), Succeeded _ ->
                ensureLoaded (refreshIndex ({ model with Connection = Connected(d, credential); Notice = $"Connected to {Destination.describe d}." }, []))
            | CheckRepository _, NotFound -> { model with Connection = ConnectFailed "The repository was not found, or the token cannot see it." }, []
            | CheckRepository _, (AuthenticationFailed m | ConflictDetected m | Unavailable m) -> { model with Connection = ConnectFailed m }, []
            | ListIndex, Succeeded body -> { model with Index = IndexLoaded(GitHub.listedIds body) }, []
            | ListIndex, NotFound -> { model with Index = IndexLoaded [] }, []
            | ListIndex, (AuthenticationFailed m | ConflictDetected m | Unavailable m) -> { model with Index = IndexFailed m }, []
            | LoadInvestigation id, Succeeded body ->
                match GitHub.fileContent body |> Result.bind (fun (text, sha) -> Document.decode id text |> Result.map (fun inv -> inv, sha)) with
                | Ok(inv, sha) ->
                    { model with
                        Load = NotLoading
                        Session = Some { Investigation = inv; Sha = Some sha; SavedLength = inv.History.Length; Save = Saved }
                        Notice = "Investigation loaded from GitHub." }, []
                | Error reason -> { model with Load = LoadFailed(id, $"The saved investigation could not be read: {reason}. Nothing was loaded.") }, []
            | LoadInvestigation id, NotFound -> { model with Load = LoadFailed(id, "No saved investigation has this identifier.") }, []
            | LoadInvestigation id, AuthenticationFailed m -> { model with Load = LoadFailed(id, m); Connection = ConnectFailed $"{m} Reconnect to load the investigation." }, []
            | LoadInvestigation id, (ConflictDetected m | Unavailable m) -> { model with Load = LoadFailed(id, m) }, []
            | SaveInvestigation(id, length), result ->
                match model.Session with
                | Some session when session.Investigation.Id = id ->
                    let state, sha, savedLength, notice =
                        match result with
                        | Succeeded body ->
                            let current = session.Investigation.History.Length
                            (if current = length then Saved else NeverSaved), GitHub.writtenSha body, length, "Saved to GitHub."
                        | AuthenticationFailed m -> SaveAuthFailed m, session.Sha, session.SavedLength, $"Not saved. {m}"
                        | ConflictDetected m -> SaveConflict m, session.Sha, session.SavedLength, $"Not saved. {m}"
                        | Unavailable m -> SaveFailed m, session.Sha, session.SavedLength, $"Not saved. {m}"
                        | NotFound -> SaveFailed "GitHub could not find the repository or branch (404).", session.Sha, session.SavedLength, "Not saved."

                    let indexed =
                        match model.Index, result with
                        | IndexLoaded ids, Succeeded _ when not (List.contains id ids) -> IndexLoaded(ids @ [ id ])
                        | other, _ -> other

                    let connection =
                        match result with
                        | AuthenticationFailed m -> ConnectFailed $"{m} Reconnect, then select Save again."
                        | _ -> model.Connection

                    { model with
                        Session = Some { session with Save = state; Sha = (match sha with Some s -> Some s | None -> session.Sha); SavedLength = savedLength }
                        Connection = connection
                        Index = indexed
                        Notice = notice }, []
                | _ -> model, []

    let update (env: Env) (msg: Msg) (model: Model) : Model * Effect list =
        match msg with
        | Initialized hash -> ensureLoaded ({ model with Screen = Screen.parse hash }, [])
        | LocationChanged hash -> ensureLoaded ({ model with Screen = Screen.parse hash; Editor = NoEditor; Errors = Map.empty }, [])
        | UiEvent(name, key, value) -> onEvent env name key value model
        | HttpDone(correlation, observation) -> onHttp correlation observation model
        | Ignored -> model, []
