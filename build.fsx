// Owner: integration. Builds the deployable site into dist/: the .NET WebAssembly engine, the page, the
// Limen kernel and Forma, served as static files (GitHub Pages or any static server). F# only.
open System
open System.Diagnostics
open System.IO

let root = __SOURCE_DIRECTORY__
let dist = Path.Combine(root, "dist")
let publish = Path.Combine(root, "artifacts", "host")

let run (file: string) (args: string) =
    let info = ProcessStartInfo(file, args, WorkingDirectory = root, UseShellExecute = false)
    use p = Process.Start info
    p.WaitForExit()
    if p.ExitCode <> 0 then failwithf "%s %s exited %d" file args p.ExitCode

let rec copyDir (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore
    Directory.GetFiles source |> Array.iter (fun f -> File.Copy(f, Path.Combine(target, Path.GetFileName f), true))
    Directory.GetDirectories source |> Array.iter (fun d -> copyDir d (Path.Combine(target, Path.GetFileName d)))

run "dotnet" $"publish src/Causa.Host/Causa.Host.csproj -c Release -o \"{publish}\" --nologo"
if Directory.Exists dist then Directory.Delete(dist, true)
Directory.CreateDirectory dist |> ignore
copyDir (Path.Combine(root, "web")) dist
copyDir (Path.Combine(publish, "wwwroot", "_framework")) (Path.Combine(dist, "_framework"))
copyDir (Path.Combine(root, "node_modules", "@echelon-foundry", "limen", "dist")) (Path.Combine(dist, "vendor", "limen"))
copyDir (Path.Combine(root, "node_modules", "@echelon-foundry", "design-system", "dist")) (Path.Combine(dist, "vendor", "forma"))
File.WriteAllText(Path.Combine(dist, ".nojekyll"), "")
printfn "site written to %s" dist
