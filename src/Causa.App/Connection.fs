// Owner: ui. The GitHub connection: destination and an opaque credential that is never rendered or logged.
namespace Causa.App.Content

open Causa.Persistence.Content

/// The credential is held only here and only sent as an Authorization header. ToString never reveals it.
[<CustomEquality; NoComparison>]
type Credential =
    private
    | Credential of string

    override this.ToString() = "<credential>"
    override this.Equals other =
        match other with
        | :? Credential as c -> (let (Credential a) = this in let (Credential b) = c in a = b)
        | _ -> false
    override this.GetHashCode() = 0

[<RequireQualifiedAccess>]
module Credential =
    let create (text: string) = if System.String.IsNullOrWhiteSpace text then None else Some(Credential(text.Trim()))
    let header (Credential value) = $"Bearer {value}"

type ConnectionStatus =
    | Disconnected
    | Connecting
    | Connected of Destination * Credential
    | ConnectFailed of message: string
