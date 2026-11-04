// Owner: integration. The Limen protocol boundary: JSON messages in, a complete view and effects out.
// `Protocol` is pure; `Engine` holds the one mutable cell (the current model) for the host shim.
namespace Causa.App.Content

open System
open System.Text.Json
open System.Text.Json.Nodes
open Causa.Persistence.Content

[<RequireQualifiedAccess>]
module Protocol =
    let private prop (name: string) (e: JsonElement) =
        if e.ValueKind = JsonValueKind.Object then
            match e.TryGetProperty name with
            | true, v when v.ValueKind <> JsonValueKind.Null && v.ValueKind <> JsonValueKind.Undefined -> Some v
            | _ -> None
        else None

    let private text name e =
        prop name e |> Option.bind (fun v -> if v.ValueKind = JsonValueKind.String then v.GetString() |> Option.ofObj else None)

    let private hashOf (e: JsonElement) = prop "location" e |> Option.bind (text "hash") |> Option.defaultValue ""

    /// Decode one BrowserToEngineMessage. Anything not understood is Ignored (the view is re-sent unchanged).
    let decode (json: string) : Msg =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement

            match text "kind" root with
            | Some "Initialize" -> Initialized(hashOf root)
            | Some "LocationChanged" -> LocationChanged(hashOf root)
            | Some "Event" ->
                match prop "event" root with
                | Some ev -> UiEvent(text "name" ev |> Option.defaultValue "", text "key" ev, text "value" ev)
                | None -> Ignored
            | Some "EffectResult" ->
                match prop "result" root with
                | Some result when text "kind" result = Some "HttpResult" ->
                    let correlation = text "correlationId" result |> Option.defaultValue ""
                    let outcome = prop "outcome" result
                    let status = outcome |> Option.bind (prop "status") |> Option.bind (fun s -> match s.TryGetInt32() with | true, n -> Some n | _ -> None)

                    let observation =
                        match outcome |> Option.bind (text "kind"), status with
                        | Some "Success", Some s ->
                            let body =
                                outcome
                                |> Option.bind (prop "body")
                                |> Option.map (fun b -> if b.ValueKind = JsonValueKind.String then (b.GetString() |> Option.ofObj |> Option.defaultValue "") else b.GetRawText())
                                |> Option.defaultValue ""
                            Answered(s, body)
                        | Some "Failure", Some s -> Answered(s, "")
                        | Some kind, _ ->
                            let reason = outcome |> Option.bind (text "reason") |> Option.defaultValue kind
                            NoAnswer reason
                        | None, _ -> NoAnswer "no outcome"

                    HttpDone(correlation, observation)
                | _ -> Ignored
            | _ -> Ignored
        with :? JsonException ->
            Ignored

    let private primitive (p: ViewPrimitive) : JsonNode =
        match p with
        | Text s -> JsonValue.Create(s: string) |> nonNull :> JsonNode
        | Flag b -> JsonValue.Create(b: bool) :> JsonNode
        | Number n -> JsonValue.Create(n: int) :> JsonNode

    let private effect (e: Effect) : JsonNode =
        let o = JsonObject()

        match e with
        | Http(correlation, req, authorization) ->
            let headers = JsonObject()
            headers["authorization"] <- JsonValue.Create(authorization: string)
            headers["accept"] <- JsonValue.Create("application/vnd.github+json")
            headers["x-github-api-version"] <- JsonValue.Create("2022-11-28")
            req.Body |> Option.iter (fun _ -> headers["content-type"] <- JsonValue.Create("application/json"))
            o["kind"] <- JsonValue.Create("Http")
            o["correlationId"] <- JsonValue.Create(correlation: string)
            o["method"] <- JsonValue.Create(req.Method: string)
            o["url"] <- JsonValue.Create(req.Url: string)
            o["headers"] <- headers
            req.Body |> Option.iter (fun b -> o["body"] <- JsonValue.Create(b: string))
            o["timeoutMs"] <- JsonValue.Create(30000)
            o["response"] <- JsonValue.Create("text")
        | PushUrl(correlation, url) ->
            o["kind"] <- JsonValue.Create("Navigation")
            o["correlationId"] <- JsonValue.Create(correlation: string)
            o["operation"] <- JsonValue.Create("push")
            o["url"] <- JsonValue.Create(url: string)

        o :> JsonNode

    /// Encode the EngineToBrowserMessage: the complete view, the effects, no cancellations.
    let encode (view: (string * ViewValue) list) (effects: Effect list) : string =
        let viewNode = JsonObject()

        view
        |> List.iter (fun (key, value) ->
            viewNode[key] <-
                match value with
                | Scalar p -> primitive p
                | Items rows ->
                    let array = JsonArray()
                    rows
                    |> List.iter (fun row ->
                        let item = JsonObject()
                        row |> List.iter (fun (k, p) -> item[k] <- primitive p)
                        array.Add(item))
                    array :> JsonNode)

        let effectsNode = JsonArray()
        effects |> List.iter (fun e -> effectsNode.Add(effect e))
        let root = JsonObject()
        root["view"] <- viewNode
        root["effects"] <- effectsNode
        root["cancellations"] <- JsonArray()
        root.ToJsonString()

    /// One protocol step, pure: the current model (None before Initialize), a message, the shell's env.
    let step (env: Env) (current: Model option) (json: string) : Model * string =
        let msg = decode json
        let model = current |> Option.defaultWith (fun () -> Update.init env "")
        let next, effects = Update.update env msg model
        next, encode (Projection.project next) effects

[<RequireQualifiedAccess>]
module Engine =
    let mutable private current: Model option = None

    /// The host shim's entry point: the clock and identifier are supplied by the host.
    let dispatch (now: DateTimeOffset) (newId: Guid) (json: string) : string =
        let env = { Now = now; NewId = newId.ToString() }
        let next, response = Protocol.step env current json
        current <- Some next
        response
