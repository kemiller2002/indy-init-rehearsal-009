// Owner: persistence. GitHub Contents API requests and the classification of their answers. Pure: the shell
// performs the HTTP effect; this module only describes it and reads the outcome.
namespace Causa.Persistence.Content

open System
open System.Text

/// Where investigations are saved: one file per investigation, causa/investigations/<id>.json on a branch.
type Destination =
    { Owner: string
      Repository: string
      Branch: string }

[<RequireQualifiedAccess>]
module Destination =
    let describe (d: Destination) = $"github.com/{d.Owner}/{d.Repository} (branch {d.Branch}, folder causa/investigations)"
    let folder = "causa/investigations"
    let path (id: string) = $"{folder}/{id}.json"

/// What an HTTP exchange came to, as the shell observed it.
type HttpObservation =
    | Answered of status: int * body: string
    | NoAnswer of reason: string

/// The classified outcome of a GitHub request.
type ProviderOutcome =
    | Succeeded of body: string
    | NotFound
    | AuthenticationFailed of string
    | ConflictDetected of string
    | Unavailable of string

type GitHubRequest =
    { Method: string
      Url: string
      Body: string option }

[<RequireQualifiedAccess>]
module GitHub =
    let api = "https://api.github.com"

    let private escape (s: string) = Uri.EscapeDataString s

    let repository (d: Destination) =
        { Method = "GET"; Url = $"{api}/repos/{escape d.Owner}/{escape d.Repository}"; Body = None }

    let listInvestigations (d: Destination) =
        { Method = "GET"
          Url = $"{api}/repos/{escape d.Owner}/{escape d.Repository}/contents/{Destination.folder}?ref={escape d.Branch}"
          Body = None }

    let readInvestigation (d: Destination) (id: string) =
        { Method = "GET"
          Url = $"{api}/repos/{escape d.Owner}/{escape d.Repository}/contents/{Destination.path id}?ref={escape d.Branch}"
          Body = None }

    let private quote (s: string) = Text.Json.JsonSerializer.Serialize s

    /// PUT the document. `sha` is the blob the client last read; GitHub refuses the write when it changed (conflict).
    let writeInvestigation (d: Destination) (id: string) (sha: string option) (message: string) (document: string) =
        let content = Convert.ToBase64String(Encoding.UTF8.GetBytes document)
        let shaField = sha |> Option.map (fun s -> $",\"sha\":{quote s}") |> Option.defaultValue ""

        { Method = "PUT"
          Url = $"{api}/repos/{escape d.Owner}/{escape d.Repository}/contents/{Destination.path id}"
          Body = Some $"{{\"message\":{quote message},\"content\":{quote content},\"branch\":{quote d.Branch}{shaField}}}" }

    /// Classify by the status and, where one status has several meanings, by the body.
    let classify (observation: HttpObservation) : ProviderOutcome =
        match observation with
        | NoAnswer reason -> Unavailable $"GitHub could not be reached ({reason})."
        | Answered(status, body) when status >= 200 && status < 300 -> Succeeded body
        | Answered(401, _) -> AuthenticationFailed "GitHub refused the credential (401). Reconnect with a valid token."
        | Answered(403, body) when body.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ->
            Unavailable "GitHub's rate limit was reached (403). Try again later."
        | Answered(403, _) -> AuthenticationFailed "The credential may not write to this repository (403)."
        | Answered(404, _) -> NotFound
        | Answered(409, _) -> ConflictDetected "The saved copy changed on GitHub since it was loaded (409)."
        | Answered(422, body) when body.Contains("sha", StringComparison.OrdinalIgnoreCase) ->
            ConflictDetected "The saved copy changed on GitHub since it was loaded (422)."
        | Answered(status, _) -> Unavailable $"GitHub could not save or load ({status})."

    let private property (name: string) (body: string) =
        try
            use doc = Text.Json.JsonDocument.Parse body
            match doc.RootElement.TryGetProperty name with
            | true, v when v.ValueKind = Text.Json.JsonValueKind.String -> v.GetString() |> Option.ofObj
            | _ -> None
        with _ ->
            None

    /// A Contents API file answer: its decoded text and blob sha.
    let fileContent (body: string) : Result<string * string, string> =
        match property "content" body, property "sha" body with
        | Some content, Some sha ->
            try
                Ok(Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", ""))), sha)
            with :? FormatException ->
                Error "the saved file is not valid base64"
        | _ -> Error "GitHub's answer is not a file"

    /// After a PUT: the new blob sha (content.sha).
    let writtenSha (body: string) : string option =
        try
            use doc = Text.Json.JsonDocument.Parse body
            match doc.RootElement.TryGetProperty "content" with
            | true, c when c.ValueKind = Text.Json.JsonValueKind.Object ->
                match c.TryGetProperty "sha" with
                | true, s when s.ValueKind = Text.Json.JsonValueKind.String -> s.GetString() |> Option.ofObj
                | _ -> None
            | _ -> None
        with _ ->
            None

    /// The investigation ids in a directory listing.
    let listedIds (body: string) : string list =
        try
            use doc = Text.Json.JsonDocument.Parse body
            if doc.RootElement.ValueKind <> Text.Json.JsonValueKind.Array then []
            else
                doc.RootElement.EnumerateArray()
                |> Seq.choose (fun e ->
                    match e.TryGetProperty "name" with
                    | true, n ->
                        let name = n.GetString() |> Option.ofObj |> Option.defaultValue ""
                        if name.EndsWith(".json", StringComparison.Ordinal) then Some(name.Substring(0, name.Length - 5)) else None
                    | _ -> None)
                |> List.ofSeq
        with _ ->
            []
