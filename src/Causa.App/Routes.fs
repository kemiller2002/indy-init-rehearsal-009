// Owner: ui. Hash routes: the URL is the source of truth for the screen.
namespace Causa.App.Content

open System

type Screen =
    | Home
    | StartScreen
    | Workspace of investigationId: string
    | Missing of path: string

[<RequireQualifiedAccess>]
module Screen =
    let parse (hash: string) : Screen =
        let path = if hash.StartsWith("#", StringComparison.Ordinal) then hash.Substring 1 else hash

        match path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray with
        | [] -> Home
        | [ "start" ] -> StartScreen
        | [ "investigation"; id ] -> Workspace(id.ToLowerInvariant())
        | _ -> Missing path

    let format (screen: Screen) : string =
        match screen with
        | Home -> "#/"
        | StartScreen -> "#/start"
        | Workspace id -> $"#/investigation/{id}"
        | Missing path -> "#" + path
