// Owner: domain. Content kernel tests: the catalog's value rules, decide/evolve, identity and history (pure).
namespace Causa.Domain.Tests

open System
open Xunit
open Causa.Domain.Content

module ContentKernel =
    let at = DateTimeOffset(2026, 11, 4, 1, 0, 0, TimeSpan.Zero)
    let ctx id = { At = at; Actor = "tester"; NewId = id }
    let start = Start("6b1a8f0e-1111-4222-8333-944445555666", { Title = "T"; ProblemStatement = "Pump trips"; Question = "Why?" })

    let run commands =
        commands
        |> List.fold (fun (state: Result<ContentInvestigation option, Refusal list>, n) command ->
            match state with
            | Error e -> Error e, n
            | Ok current -> (Evolve.execute (ctx $"id-{n}") current command |> Result.map Some), n + 1) (Ok None, 1)
        |> fst
        |> Result.map Option.get

    let refused field result =
        match result with
        | Error refusals -> Assert.True(refusals |> List.exists (fun (r: Refusal) -> r.Field = field), $"%A{refusals}")
        | Ok _ -> failwith $"expected a refusal of {field}"

    [<Fact>]
    let ``the ratified relationship vocabulary is exactly four kinds with no default`` () =
        Assert.Equal<string list>([ "supports"; "contradicts"; "consistent-with"; "inconclusive" ], RelationshipKind.all |> List.map RelationshipKind.code)
        Assert.Equal<string list>(RelationshipKind.all |> List.map RelationshipKind.code, Rules.relationshipKind.Options)
        Assert.True((RelationshipKind.parse "not-assessed").IsNone)
        Assert.True((RelationshipKind.parse "").IsNone)

    [<Fact>]
    let ``value rules follow the catalog boundaries`` () =
        Assert.True(Rules.check Rules.problemStatement "   " |> Result.isError)
        Assert.True(Rules.check Rules.problemStatement (String('x', 10000)) |> Result.isOk)
        Assert.True(Rules.check Rules.problemStatement (String('x', 10001)) |> Result.isError)
        Assert.True(Rules.check Rules.title "" |> Result.isOk)
        Assert.True(Rules.check Rules.title (String('x', 201)) |> Result.isError)
        Assert.True(Rules.check Rules.evidenceLabel (String('x', 200)) |> Result.isOk)
        Assert.True(Rules.check Rules.evidenceUrl "javascript:alert(1)" |> Result.isError)
        Assert.True(Rules.check Rules.evidenceUrl "https://example.com/a" |> Result.isOk)
        Assert.True(Rules.check Rules.evidenceUrl "" |> Result.isOk)
        Assert.True(Rules.check Rules.identifier "not-a-uuid" |> Result.isError)
        Assert.True(Rules.check Rules.relationshipKind "maybe" |> Result.isError)

    [<Fact>]
    let ``starting keeps the original statement and the identifier`` () =
        let inv = run [ start ] |> Result.defaultWith (fun e -> failwithf "%A" e)
        Assert.Equal("6b1a8f0e-1111-4222-8333-944445555666", inv.Id)
        Assert.Equal("Pump trips", inv.OriginalStatement)
        Assert.Equal(at, inv.CreatedAt)

    [<Fact>]
    let ``empty content is refused and nothing is recorded`` () =
        refused "problemStatement" (run [ Start("6b1a8f0e-1111-4222-8333-944445555666", { Title = ""; ProblemStatement = " "; Question = "" }) ])
        refused "identifier" (run [ Start("nope", { Title = ""; ProblemStatement = "x"; Question = "" }) ])
        refused "observationText" (run [ start; RecordObservation { Text = ""; Source = "" } ])
        refused "hypothesisStatement" (run [ start; RecordHypothesis { Statement = "  "; Explanation = "" } ])
        refused "evidenceUrl" (run [ start; AdmitEvidence { Label = "L"; Content = ""; Source = ""; Url = "ftp://x" } ])
        refused "conclusionStatement" (run [ start; RecordConclusion "" ])

    [<Fact>]
    let ``links need existing targets, a kind, a rationale and are one per pair`` () =
        let setup = [ start; RecordHypothesis { Statement = "H"; Explanation = "" }; AdmitEvidence { Label = "E"; Content = ""; Source = ""; Url = "" } ]
        let link kind rationale = LinkEvidence { EvidenceId = "id-3"; HypothesisId = "id-2"; Kind = kind; Rationale = rationale }
        refused "linkEvidence" (run (setup @ [ LinkEvidence { EvidenceId = "missing"; HypothesisId = "id-2"; Kind = "supports"; Rationale = "r" } ]))
        refused "linkHypothesis" (run (setup @ [ LinkEvidence { EvidenceId = "id-3"; HypothesisId = "missing"; Kind = "supports"; Rationale = "r" } ]))
        refused "relationshipKind" (run (setup @ [ link "" "r" ]))
        refused "relationshipKind" (run (setup @ [ link "maybe" "r" ]))
        refused "rationale" (run (setup @ [ link "supports" " " ]))
        refused "linkHypothesis" (run (setup @ [ link "supports" "r"; link "contradicts" "again" ]))
        let inv = run (setup @ [ link "consistent-with" "fits" ]) |> Result.defaultWith (fun e -> failwithf "%A" e)
        Assert.Equal(ConsistentWith, inv.Relationships.Head.Kind)

    [<Fact>]
    let ``edits keep identity and history; reassess and withdraw keep the earlier assessment`` () =
        let commands =
            [ start
              RecordObservation { Text = "first"; Source = "" }
              EditObservation("id-2", { Text = "second"; Source = "s" })
              RecordHypothesis { Statement = "H"; Explanation = "" }
              AdmitEvidence { Label = "E"; Content = ""; Source = ""; Url = "" }
              LinkEvidence { EvidenceId = "id-5"; HypothesisId = "id-4"; Kind = "supports"; Rationale = "r1" }
              Reassess("id-6", "inconclusive", "r2")
              Withdraw("id-6", "no longer relevant") ]
        let inv = run commands |> Result.defaultWith (fun e -> failwithf "%A" e)
        Assert.Equal<Observation list>([ { Id = "id-2"; Text = "second"; Source = "s" } ], inv.Observations)
        Assert.Empty(inv.Relationships)
        Assert.Equal(8, inv.History.Length)
        Assert.True(inv.History |> List.exists (fun (r: EventRecord) -> r.Event = ObservationRecorded { Id = "id-2"; Text = "first"; Source = "" }))
        Assert.True(inv.History |> List.exists (fun (r: EventRecord) -> match r.Event with RelationshipRecorded rel -> rel.Kind = Supports | _ -> false))
        Assert.True(inv.History |> List.exists (fun (r: EventRecord) -> r.Event = RelationshipReassessed("id-6", Inconclusive, "r2")))

    [<Fact>]
    let ``replay refuses a broken history`` () =
        let inv = run [ start; RecordConclusion "c" ] |> Result.defaultWith (fun e -> failwithf "%A" e)
        Assert.True(Evolve.replay inv.History |> Result.isOk)
        Assert.True(Evolve.replay (List.rev inv.History) |> Result.isError)
        Assert.True(Evolve.replay [] |> Result.isError)
