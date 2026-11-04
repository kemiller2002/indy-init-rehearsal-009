// Owner: domain. The investigation aggregate: the fold of its content history.
namespace Causa.Domain.Content

open System

type ContentInvestigation =
    { Id: string
      CreatedAt: DateTimeOffset
      /// INV-008: the problem statement as first entered; editing never changes it.
      OriginalStatement: string
      Details: Details
      State: InvestigationState
      Observations: Observation list
      Hypotheses: Hypothesis list
      Evidence: Evidence list
      /// Current assessments only; withdrawn ones remain in History.
      Relationships: Relationship list
      Conclusions: Conclusion list
      History: EventRecord list }

/// A refused command: the field it concerns (for the message beside it) and the reason.
type Refusal = { Field: string; Message: string }
