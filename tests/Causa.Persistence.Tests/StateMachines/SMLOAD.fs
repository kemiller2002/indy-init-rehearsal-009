// Owner: persistence. Loading a saved investigation: the canonical document round trip and its refusals.
namespace Causa.Persistence.Tests

open System
open Xunit
open Causa.Domain.Content
open Causa.Persistence.Content

module DocumentRoundTrip =
    let id = "6b1a8f0e-1111-4222-8333-944445555666"
    let at = DateTimeOffset(2026, 11, 4, 1, 0, 0, TimeSpan.Zero)

    let investigation () =
        [ Start(id, { Title = "Tïtle \"quoted\""; ProblemStatement = "Line 1\nLine 2 — ünïcode"; Question = "Q?" })
          RecordObservation { Text = "obs"; Source = "src" }
          RecordHypothesis { Statement = "H"; Explanation = "E" }
          AdmitEvidence { Label = "L"; Content = "C"; Source = "S"; Url = "https://example.com/x?y=1&z=2" }
          LinkEvidence { EvidenceId = "n4"; HypothesisId = "n3"; Kind = "contradicts"; Rationale = "R" }
          RecordConclusion "Conclusion" ]
        |> List.indexed
        |> List.fold (fun state (i, c) -> state |> Result.bind (fun cur -> Evolve.execute { At = at; Actor = "a"; NewId = $"n{i + 1}" } cur c |> Result.map Some)) (Ok None)
        |> function
            | Ok(Some inv) -> inv
            | other -> failwithf "%A" other

    [<Fact>]
    let ``a saved document reloads to the same investigation`` () =
        let inv = investigation ()
        match Document.decode id (Document.encode inv) with
        | Ok reloaded -> Assert.Equal(inv, reloaded)
        | Error e -> failwith e

    [<Theory>]
    [<InlineData("{ not json")>]
    [<InlineData("[]")>]
    [<InlineData("{\"schema\":\"other/1\",\"id\":\"x\",\"events\":[]}")>]
    let ``malformed documents are refused`` (text: string) =
        Assert.True(Document.decode id text |> Result.isError)

    [<Fact>]
    let ``a document for another investigation or with an unknown kind is refused`` () =
        let text = Document.encode (investigation ())
        Assert.True(Document.decode "11111111-1111-1111-1111-111111111111" text |> Result.isError)
        Assert.True(Document.decode id (text.Replace("\"contradicts\"", "\"proves\"")) |> Result.isError)

    [<Fact>]
    let ``GitHub answers are classified by status and body`` () =
        Assert.Equal(NotFound, GitHub.classify (Answered(404, "")))
        Assert.True(match GitHub.classify (Answered(401, "")) with AuthenticationFailed _ -> true | _ -> false)
        Assert.True(match GitHub.classify (Answered(409, "")) with ConflictDetected _ -> true | _ -> false)
        Assert.True(match GitHub.classify (Answered(422, "\"sha\" wasn't supplied")) with ConflictDetected _ -> true | _ -> false)
        Assert.True(match GitHub.classify (Answered(403, "API rate limit exceeded")) with Unavailable _ -> true | _ -> false)
        Assert.True(match GitHub.classify (Answered(503, "")) with Unavailable _ -> true | _ -> false)
        Assert.True(match GitHub.classify (NoAnswer "network") with Unavailable _ -> true | _ -> false)

    [<Fact>]
    let ``the write request carries the blob sha and the branch`` () =
        let d = { Owner = "o"; Repository = "r"; Branch = "b" }
        let req = GitHub.writeInvestigation d id (Some "abc") "m" "{}"
        Assert.Equal("PUT", req.Method)
        Assert.EndsWith($"/contents/causa/investigations/{id}.json", req.Url)
        Assert.Contains("\"sha\":\"abc\"", req.Body.Value)
        Assert.Contains("\"branch\":\"b\"", req.Body.Value)
