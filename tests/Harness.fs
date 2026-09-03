module BAREWire.Tests.Harness

/// The smallest harness that can fail loudly: named checks, a running count,
/// and a hex dump for byte comparisons. No test framework, so the gate has
/// no dependency the library does not.
let mutable private passed = 0
let mutable private failed = 0

let hex (bytes: byte array) : string =
    bytes |> Array.map (fun b -> b.ToString("x2")) |> String.concat " "

let check (name: string) (ok: bool) (detail: string) : unit =
    if ok then
        passed <- passed + 1
    else
        failed <- failed + 1
        printfn "FAIL %s: %s" name detail

let equal (name: string) (expected: 'a) (actual: 'a) : unit =
    check name (expected = actual) (sprintf "expected %A, got %A" expected actual)

let bytesEqual (name: string) (expected: byte array) (actual: byte array) : unit =
    check name (expected = actual) (sprintf "expected [%s], got [%s]" (hex expected) (hex actual))

let summary () : int =
    printfn "%d passed, %d failed" passed failed
    if failed = 0 then 0 else 1
