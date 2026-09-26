#load "NativeGateCore.fsx"
open System
open System.IO
open System.Globalization
open NativeGateCore

let usage = "dotnet fsi tests/NativeGate.fsx -- /path/to/Composer [--timeout seconds] [--results directory] [--process-host-project path]"
let mutable compiler = None
let mutable timeout = TimeSpan.FromSeconds 180.
let mutable results = Path.Combine(Path.GetTempPath(), "barewire-native")
let mutable hostProject = defaultHostProject
let rec parse = function
    | [] -> ()
    | "--" :: rest -> parse rest
    | "--timeout" :: value :: rest ->
        match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, seconds when Double.IsFinite seconds && seconds > 0. ->
            timeout <- TimeSpan.FromSeconds seconds
            parse rest
        | _ -> invalidArg "timeout" "Expected a finite positive number of seconds"
    | "--results" :: value :: rest -> results <- Path.GetFullPath value; parse rest
    | "--process-host-project" :: value :: rest -> hostProject <- Path.GetFullPath value; parse rest
    | value :: rest when compiler.IsNone && not (value.StartsWith("-")) ->
        compiler <- Some(Path.GetFullPath value)
        parse rest
    | _ -> invalidArg "arguments" usage

try
    parse (fsi.CommandLineArgs |> Array.skip 1 |> Array.toList)
    let executable = compiler |> Option.defaultWith (fun () -> invalidArg "composer" usage)
    let sample = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../samples/RoundTrip"))
    // Fail before even building the process host if the oracle is absent.
    File.ReadAllBytes(Path.Combine(sample, "expected.txt")) |> ignore
    let evidence = Path.Combine(results, DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"))
    printfn "Native gate evidence: %s" evidence
    let host = (prepareHost hostProject evidence).GetAwaiter().GetResult()
    (runGate host { Executable = executable; Arguments = [] } sample evidence timeout).GetAwaiter().GetResult()
    printfn "native differential: agrees"
with error ->
    eprintfn "native gate failed: %s" error.Message
    exit 1
