open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

type Command = { Executable: string; Arguments: string list }

module private ProcessGroup =
    [<DllImport("libc", SetLastError = true)>]
    extern int kill(int pid, int signal)

/// Byte-preserving process boundary. The shared .NET host establishes a Linux
/// session before launching the command; no shell or compiler-host threads run it.
let runProcess hostAssembly evidence stage workingDirectory timeout (command: Command) = task {
    Directory.CreateDirectory evidence |> ignore
    let ready = Path.Combine(evidence, stage + ".ready")
    File.Delete ready
    File.Delete(ready + ".error")
    let executable, arguments =
        match hostAssembly with
        | Some host -> "dotnet", [host; "--ready"; ready; "--"; command.Executable] @ command.Arguments
        | None -> command.Executable, command.Arguments
    let start = ProcessStartInfo(executable, UseShellExecute = false,
                    WorkingDirectory = workingDirectory, RedirectStandardOutput = true,
                    RedirectStandardError = true, RedirectStandardInput = true)
    for argument in arguments do start.ArgumentList.Add argument
    use child = new Process(StartInfo = start)
    use cancellation = new CancellationTokenSource()
    use stdout = File.Create(Path.Combine(evidence, stage + ".stdout.log"))
    use stderr = File.Create(Path.Combine(evidence, stage + ".stderr.log"))
    if not (child.Start()) then failwithf "%s: could not start %s" stage executable
    child.StandardInput.Close()
    let terminate () =
        if OperatingSystem.IsLinux() && hostAssembly.IsSome && File.Exists ready then
            match Int32.TryParse(File.ReadAllText ready) with
            | true, identity when identity = child.Id -> ProcessGroup.kill(-identity, 9) |> ignore
            | _ -> ()
        if not child.HasExited then
            try child.Kill(true) with :? InvalidOperationException -> ()
    let completed =
        Task.WhenAll
            [| child.StandardOutput.BaseStream.CopyToAsync(stdout, cancellation.Token)
               child.StandardError.BaseStream.CopyToAsync(stderr, cancellation.Token)
               child.WaitForExitAsync(cancellation.Token) |]
    try
        try
            do! completed.WaitAsync(timeout: TimeSpan)
        with failure ->
            terminate ()
            cancellation.Cancel()
            try do! completed.WaitAsync(TimeSpan.FromSeconds 5.) with _ -> ()
            try do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 5.) with _ -> ()
            match failure with
            | :? TimeoutException -> raise (TimeoutException(sprintf "%s exceeded %.3f seconds" stage timeout.TotalSeconds))
            | _ -> raise failure
        if File.Exists(ready + ".error") then
            failwithf "%s: %s" stage (File.ReadAllText(ready + ".error"))
        if child.ExitCode <> 0 then
            failwithf "%s exited %d; see %s" stage child.ExitCode evidence
    finally
        terminate ()
}

let defaultHostProject =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../Composer/tests/Infrastructure/ProcessHost/ProcessHost.fsproj"))

/// Builds only the small reusable host into private outputs, never Composer/CCS.
let prepareHost hostProject evidence = task {
    if not (File.Exists hostProject) then
        failwithf "ProcessHost project not found: %s (use --process-host-project)" hostProject
    let output = Path.Combine(evidence, "process-host")
    let command =
        { Executable = "dotnet"
          Arguments = [ "build"; hostProject; "--disable-build-servers";
                        "--artifacts-path"; Path.Combine(evidence, "host-build-artifacts");
                        "-o"; output; "--nologo" ] }
    do! runProcess None evidence "host-build" (Path.GetDirectoryName hostProject)
            (TimeSpan.FromSeconds 120.) command
    let assembly = Path.Combine(output, "ProcessHost.dll")
    if not (File.Exists assembly) then failwith "ProcessHost build produced no assembly"
    return assembly
}

let runGate hostAssembly composer sample evidence timeout = task {
    // Read the independent oracle before creating artifacts or launching a job.
    // The run under test never creates or updates this file.
    let expected = File.ReadAllBytes(Path.Combine(sample, "expected.txt"))
    Directory.CreateDirectory evidence |> ignore
    let output = Path.Combine(evidence, "native", "roundtrip")
    Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore
    File.Delete output
    File.WriteAllBytes(Path.Combine(evidence, "expected.bytes"), expected)
    let compile =
        { composer with
            Arguments =
                composer.Arguments @
                    [ "compile"; "RoundTrip.fidproj"; "-o"; output; "-k";
                      "--artifacts-dir"; Path.Combine(evidence, "compiler-artifacts") ] }
    do! runProcess (Some hostAssembly) evidence "compile" sample timeout compile
    if not (File.Exists output) then
        raise (FileNotFoundException("Successful compiler produced no fresh native artifact", output))
    do! runProcess (Some hostAssembly) evidence "run" sample timeout
            { Executable = output; Arguments = [] }
    let actual = File.ReadAllBytes(Path.Combine(evidence, "run.stdout.log"))
    if actual <> expected then
        failwith "native transcript differs from samples/RoundTrip/expected.txt (exact bytes)"
}
