// Owner: domain. Content commands: what a user asks for, as entered (unvalidated text).
namespace Causa.Domain.Content

open System

type DetailsInput =
    { Title: string
      ProblemStatement: string
      Question: string }

type ObservationInput = { Text: string; Source: string }

type HypothesisInput = { Statement: string; Explanation: string }

type EvidenceInput =
    { Label: string
      Content: string
      Source: string
      Url: string }

type RelationshipInput =
    { EvidenceId: string
      HypothesisId: string
      Kind: string
      Rationale: string }

type ContentCommand =
    | Start of identifier: string * DetailsInput
    | EditDetails of DetailsInput
    | RecordObservation of ObservationInput
    | EditObservation of id: string * ObservationInput
    | RecordHypothesis of HypothesisInput
    | EditHypothesis of id: string * HypothesisInput
    | AdmitEvidence of EvidenceInput
    | EditEvidence of id: string * EvidenceInput
    | LinkEvidence of RelationshipInput
    | Reassess of relationshipId: string * kind: string * rationale: string
    | Withdraw of relationshipId: string * reason: string
    | RecordConclusion of statement: string
    | ReviseConclusion of id: string * statement: string

/// What the shell supplies to a decision: the clock, the actor and a fresh identifier. Keeps decide pure.
type DecisionContext =
    { At: DateTimeOffset
      Actor: string
      NewId: string }
