module NativeGateFixture

open System
open System.IO
open System.Diagnostics
open System.Threading

let writeBytes bytes =
    use output = Console.OpenStandardOutput()
    output.Write(bytes: byte array)
    output.Flush()

[<EntryPoint>]
let main arguments =
    match Array.toList arguments with
    | "--compile-mode" :: mode :: "compile" :: rest ->
        let output = rest |> List.pairwise |> List.find (fun (key, _) -> key = "-o") |> snd
        File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "compiler-invoked"), "yes")
        if not (List.contains "-k" rest && List.contains "--artifacts-dir" rest) then 23
        elif mode = "compile-fail" then 3
        elif mode = "compile-timeout" then Thread.Sleep 60000; 0
        elif mode = "missing" then 0
        else
            let target = Path.GetDirectoryName output
            Directory.CreateDirectory target |> ignore
            for source in Directory.GetFiles AppContext.BaseDirectory do
                File.Copy(source, Path.Combine(target, Path.GetFileName source), true)
            let apphost = Path.Combine(AppContext.BaseDirectory,
                              if OperatingSystem.IsWindows() then "NativeGateFixture.exe" else "NativeGateFixture")
            File.Copy(apphost, output, true)
            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(output, File.GetUnixFileMode apphost)
            File.WriteAllText(Path.Combine(target, "run-mode"), mode)
            0
    | ["--orphan"; marker] ->
        Thread.Sleep 1500
        File.WriteAllText(marker, "survived")
        0
    | [] ->
        let mode = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "run-mode"))
        File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "native-invoked"), mode)
        if mode = "native-timeout" then Thread.Sleep 60000
        if mode = "orphan-pipe" then
            let start = ProcessStartInfo("dotnet", UseShellExecute = false)
            for argument in [ Path.Combine(AppContext.BaseDirectory, "NativeGateFixture.dll");
                              "--orphan"; Path.Combine(Environment.CurrentDirectory, "orphan-survived") ] do
                start.ArgumentList.Add argument
            use orphan = Process.Start start
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "orphan-started"), string orphan.Id)
        let bytes =
            match mode with
            | "mismatch" -> [| 100uy; 105uy; 102uy; 102uy; 10uy |]
            | "whitespace" -> [| 97uy; 103uy; 114uy; 101uy; 101uy; 115uy; 32uy; 10uy |]
            | "raw-bytes" -> [| 0uy; 255uy; 128uy; 13uy; 10uy |]
            | _ -> Text.Encoding.UTF8.GetBytes "agrees\n"
        writeBytes bytes
        if mode = "native-fail" then 7 else 0
    | _ -> 24
