#load "NativeGateCore.fsx"
open System
open System.IO
open System.Text
open System.Threading.Tasks
open NativeGateCore

let work = Path.Combine(Path.GetTempPath(), "barewire native gate " + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory work |> ignore
printfn "Native gate test evidence: %s" work
let host = (prepareHost defaultHostProject work).GetAwaiter().GetResult()
let fixtureOutput = Path.Combine(work, "fixture")
let fixtureProject = Path.Combine(__SOURCE_DIRECTORY__, "NativeGateFixture/NativeGateFixture.fsproj")
(runProcess (Some host) work "fixture-build" __SOURCE_DIRECTORY__ (TimeSpan.FromSeconds 120.)
    { Executable = "dotnet"
      Arguments = [ "build"; fixtureProject; "--disable-build-servers"; "--artifacts-path";
                    Path.Combine(work, "fixture-build-artifacts"); "-o"; fixtureOutput; "--nologo" ] }).GetAwaiter().GetResult()
let fixture = Path.Combine(fixtureOutput, "NativeGateFixture.dll")
let mutable passed = 0
let scenario name mode (expected: byte array) timeout =
    let sample = Path.Combine(work, name, "sample")
    let evidence = Path.Combine(work, name, "evidence")
    Directory.CreateDirectory sample |> ignore
    File.WriteAllBytes(Path.Combine(sample, "expected.txt"), expected)
    let command = { Executable = "dotnet"; Arguments = [fixture; "--compile-mode"; mode] }
    sample, evidence, fun () -> (runGate host command sample evidence timeout).GetAwaiter().GetResult()
let standard = Encoding.UTF8.GetBytes "agrees\n"
let ample = TimeSpan.FromSeconds 10.
let expectFailure label predicate action =
    let mutable failure = None
    try action () with error -> failure <- Some error
    match failure with
    | Some error when predicate error -> passed <- passed + 1; printfn "PASS %s" label
    | Some error -> failwithf "%s: unexpected failure: %O" label error
    | None -> failwithf "%s: expected rejection" label
let rejects (text: string) (error: exn) = error.Message.Contains(text, StringComparison.Ordinal)

let _, _, success = scenario "fresh exact" "success" standard ample
success (); passed <- passed + 1; printfn "PASS fresh exact artifact"
let _, _, raw = scenario "raw bytes" "raw-bytes" [| 0uy; 255uy; 128uy; 13uy; 10uy |] ample
raw (); passed <- passed + 1; printfn "PASS unmodified binary output bytes"
for name, mode, message in
    [ "nonzero native", "native-fail", "run exited 7"
      "different bytes", "mismatch", "exact bytes"
      "trailing whitespace", "whitespace", "exact bytes"
      "failed compiler", "compile-fail", "compile exited 3"
      "missing artifact", "missing", "no fresh native artifact" ] do
    let _, _, run = scenario name mode standard ample
    expectFailure name (rejects message) run

let missingSample, missingEvidence, missingOracle = scenario "missing oracle" "success" standard ample
File.Delete(Path.Combine(missingSample, "expected.txt"))
expectFailure "missing independent oracle" (fun error -> error :? FileNotFoundException) missingOracle
if File.Exists(Path.Combine(missingSample, "compiler-invoked")) || Directory.Exists missingEvidence then
    failwith "Missing oracle must fail before compilation/artifact creation"

let staleSample, staleEvidence, staleFirst = scenario "stale artifact" "success" standard ample
staleFirst ()
let noOutput = { Executable = "dotnet"; Arguments = [fixture; "--compile-mode"; "missing"] }
expectFailure "stale artifact cannot pass" (rejects "no fresh native artifact")
    (fun () -> (runGate host noOutput staleSample staleEvidence ample).GetAwaiter().GetResult())

for name, mode, stage in
    [ "compiler timeout", "compile-timeout", "compile"
      "native timeout", "native-timeout", "run" ] do
    let sample, _, run = scenario name mode standard (TimeSpan.FromMilliseconds 700.)
    expectFailure name (fun error -> error :? TimeoutException && rejects (stage + " exceeded") error) run
    let marker = if stage = "compile" then "compiler-invoked" else "native-invoked"
    if not (File.Exists(Path.Combine(sample, marker))) then failwithf "%s did not reach its fixture" name

if OperatingSystem.IsLinux() then
    let sample, _, orphan = scenario "orphan held pipe" "orphan-pipe" standard (TimeSpan.FromMilliseconds 700.)
    expectFailure "exited parent with orphan-held pipe"
        (fun error -> error :? TimeoutException && rejects "run exceeded" error) orphan
    if not (File.Exists(Path.Combine(sample, "orphan-started"))) then failwith "Orphan fixture did not start its child"
    Task.Delay(1700).GetAwaiter().GetResult()
    if File.Exists(Path.Combine(sample, "orphan-survived")) then failwith "Timed-out process-group child survived"
printfn "%d NativeGate checks passed" passed
