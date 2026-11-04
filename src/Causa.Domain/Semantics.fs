// Owner: domain. The value rules of CAT-CAUSA-INVESTIGATION (INV-006, INV-019..INV-033, INV-038), pure.
namespace Causa.Domain.Content

open System
open System.Text.RegularExpressions

/// A field's value rules exactly as the ratified catalog states them.
type FieldRule =
    { Field: string
      MinLength: int option
      MaxLength: int option
      Pattern: string option
      Options: string list }

[<RequireQualifiedAccess>]
module Rules =
    let private rule field minLength maxLength pattern options =
        { Field = field; MinLength = minLength; MaxLength = maxLength; Pattern = pattern; Options = options }

    let uuidPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
    let urlPattern = "^https?://[^\\s]+$"

    /// INV-006: the investigation identifier.
    let identifier = rule "Investigation identifier" (Some 1) None (Some uuidPattern) []
    /// INV-001, INV-019, INV-021.
    let problemStatement = rule "Problem statement" (Some 1) (Some 10000) None []
    /// INV-002, INV-020.
    let title = rule "Title" None (Some 200) None []
    /// INV-023.
    let question = rule "Primary investigative question" None (Some 10000) None []
    /// INV-024.
    let observationText = rule "Observation text" (Some 1) (Some 10000) None []
    /// INV-025.
    let observationSource = rule "Observation source reference" None (Some 10000) None []
    /// INV-026.
    let hypothesisStatement = rule "Hypothesis statement" (Some 1) (Some 10000) None []
    /// INV-011 (no length rule in the catalog).
    let hypothesisExplanation = rule "Hypothesis explanation" None None None []
    /// INV-027.
    let evidenceLabel = rule "Evidence label" (Some 1) (Some 200) None []
    /// INV-012 (no length rule in the catalog).
    let evidenceContent = rule "Evidence original content" None None None []
    /// INV-028.
    let evidenceSource = rule "Evidence source reference" None (Some 10000) None []
    /// INV-029.
    let evidenceUrl = rule "Evidence source URL" None (Some 2000) (Some urlPattern) []
    /// INV-031: exactly the ratified vocabulary (OD-INDY-23); no default.
    let relationshipKind = rule "Relationship" (Some 1) None None [ "supports"; "contradicts"; "consistent-with"; "inconclusive" ]
    /// INV-038.
    let rationale = rule "Rationale" (Some 1) (Some 10000) None []
    /// INV-033.
    let conclusionStatement = rule "Conclusion statement" (Some 1) (Some 10000) None []

    /// Apply a rule. Emptiness is decided by minLength alone; an empty optional value is valid.
    let check (r: FieldRule) (value: string) : Result<string, string> =
        let trimmedLength = value.Trim().Length

        if trimmedLength = 0 then
            match r.MinLength with
            | Some m when m >= 1 -> Error $"{r.Field} is required."
            | _ -> Ok ""
        else
            match r.MaxLength, r.Pattern, r.Options with
            | Some m, _, _ when value.Length > m -> Error $"{r.Field} must be at most {m} characters ({value.Length} entered)."
            | _, Some p, _ when not (Regex.IsMatch(value, p, RegexOptions.CultureInvariant)) ->
                if p = urlPattern then Error $"{r.Field} must be an absolute http:// or https:// address."
                else Error $"{r.Field} is not in the required format."
            | _, _, options when not options.IsEmpty && not (List.contains value options) ->
                let allowed = String.Join(", ", options)
                Error $"{r.Field} must be one of: {allowed}."
            | _ -> Ok value
