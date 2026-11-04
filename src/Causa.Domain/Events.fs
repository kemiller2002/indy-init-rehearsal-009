// Owner: domain. Content events: the persisted, append-only history (INV-035).
namespace Causa.Domain.Content

open System

type ContentEvent =
    | Started of id: string * createdAt: DateTimeOffset * Details
    | DetailsEdited of Details
    | ObservationRecorded of Observation
    | ObservationEdited of Observation
    | HypothesisRecorded of Hypothesis
    | HypothesisEdited of Hypothesis
    | EvidenceAdmitted of Evidence
    | EvidenceEdited of Evidence
    | RelationshipRecorded of Relationship
    | RelationshipReassessed of id: string * RelationshipKind * rationale: string
    | RelationshipWithdrawn of id: string * reason: string
    | ConclusionRecorded of Conclusion
    | ConclusionRevised of Conclusion

type EventRecord =
    { Sequence: int
      Provenance: Provenance
      Event: ContentEvent }
