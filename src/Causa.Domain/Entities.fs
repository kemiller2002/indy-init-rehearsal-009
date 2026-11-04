// Owner: domain. The investigation content model (OD-INDY-19..21, OD-INDY-23): pure data.
namespace Causa.Domain.Content

open System

/// INV-031: the ratified relationship vocabulary. "Not assessed" is the absence of a relationship, never a case.
type RelationshipKind =
    | Supports
    | Contradicts
    | ConsistentWith
    | Inconclusive

[<RequireQualifiedAccess>]
module RelationshipKind =
    let all = [ Supports; Contradicts; ConsistentWith; Inconclusive ]

    let code kind =
        match kind with
        | Supports -> "supports"
        | Contradicts -> "contradicts"
        | ConsistentWith -> "consistent-with"
        | Inconclusive -> "inconclusive"

    let label kind =
        match kind with
        | Supports -> "Supports"
        | Contradicts -> "Contradicts"
        | ConsistentWith -> "Consistent with"
        | Inconclusive -> "Inconclusive"

    let parse (text: string) = all |> List.tryFind (fun k -> code k = text)

/// Who did something and when: the provenance kept in the history (INV-035).
type Provenance = { Actor: string; At: DateTimeOffset }

type Details =
    { Title: string
      ProblemStatement: string
      Question: string }

type Observation = { Id: string; Text: string; Source: string }

type Hypothesis = { Id: string; Statement: string; Explanation: string }

type Evidence =
    { Id: string
      Label: string
      Content: string
      Source: string
      Url: string }

/// One current assessment of an evidence-hypothesis pair (INV-030). Withdrawn assessments stay in the history.
type Relationship =
    { Id: string
      EvidenceId: string
      HypothesisId: string
      Kind: RelationshipKind
      Rationale: string }

type Conclusion = { Id: string; Statement: string }

type InvestigationState =
    | Open

[<RequireQualifiedAccess>]
module InvestigationState =
    let label state =
        match state with
        | Open -> "Open"
