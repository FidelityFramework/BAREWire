module BAREWire.Tests.MemoryTests

open BAREWire.Hardware
open BAREWire.Memory
open BAREWire.Tests.Harness

/// Region bounds and a typed View over a three-field layout (docs/04).
let run () =
    let region = Region.ofArray (Array.zeroCreate 64)
    equal "region length" 64 region.Length
    equal "slice inside" true (Region.slice region 8 16).IsSome
    equal "slice outside" true (Region.slice region 60 8).IsNone
    equal "contains" true (Region.contains region 0 64)
    equal "not contains" false (Region.contains region 1 64)
    let layout = Layout.create 24 8 [| Field.simple "a" 0 Repr.U32 AccessKind.ReadWrite; Field.simple "b" 4 Repr.I32 AccessKind.ReadWrite; Field.simple "c" 8 Repr.F64 AccessKind.ReadWrite; Field.simple "d" 16 Repr.I8 AccessKind.ReadWrite; Field.simple "e" 17 Repr.Bool AccessKind.ReadWrite; Field.simple "f" 18 Repr.I16 AccessKind.ReadWrite |]
    match View.create region layout with
    | None -> check "view created" false "none"
    | Some view ->
        equal "write a" true (View.writeU32 view "a" 0xDEADBEEFu)
        equal "write b" true (View.writeI32 view "b" -7)
        equal "write c" true (View.writeF64 view "c" 2.5)
        equal "read a" (Some 0xDEADBEEFu) (View.readU32 view "a")
        equal "read b" (Some -7) (View.readI32 view "b")
        equal "read c" (Some 2.5) (View.readF64 view "c")
        equal "write d" true (View.writeI8 view "d" -3y)
        equal "write e" true (View.writeBool view "e" true)
        equal "write f" true (View.writeI16 view "f" -300s)
        equal "read d" (Some -3y) (View.readI8 view "d")
        equal "read e" (Some true) (View.readBool view "e")
        equal "read f" (Some -300s) (View.readI16 view "f")
        equal "contains refuses a wrapping length" false (Region.contains region 4 System.Int32.MaxValue)
        equal "wrong repr refused" None (View.readU8 view "a")
        equal "unknown field refused" None (View.readU32 view "zz")
        // the bytes are little-endian at the declared offsets
        equal "a byte 0" 0xEFuy region.Data.[0]
        equal "b byte 4" 0xF9uy region.Data.[4]
    let small = Region.ofArray (Array.zeroCreate 8)
    equal "view over short region refused" true (View.create small layout).IsNone

    // A view is restricted by its declared layout and access rights, even if
    // the backing region is larger. Refusal must leave every byte untouched.
    for access, canRead, canWrite in [ AccessKind.ReadOnly, true, false; AccessKind.WriteOnly, false, true; AccessKind.ReadWrite, true, true; "unknown", false, false ] do
        let bytes = Array.zeroCreate<byte> 8
        let field = Field.simple "value" 0 Repr.U8 access
        let view = { Region = Region.ofArray bytes; Layout = Layout.create 1 1 [| field |] }
        equal (sprintf "view read respects %s" access) canRead (View.readU8 view "value").IsSome
        equal (sprintf "view write respects %s" access) canWrite (View.writeU8 view "value" 42uy)
        equal (sprintf "refused %s write leaves bytes unchanged" access) (if canWrite then 42uy else 0uy) bytes.[0]
    for offset, count, size in [ 1, 1, 1; 0, 0, 1; 0, -1, 1; 0, 3, 2; 0, System.Int32.MaxValue, 1 ] do
        let bytes = Array.zeroCreate<byte> 8
        let view = { Region = Region.ofArray bytes; Layout = Layout.create size 1 [| Field.array "value" offset Repr.U8 count AccessKind.ReadWrite |] }
        equal "view refuses a field outside its declared extent" None (View.readU8 view "value")
        equal "view cannot write through an invalid field declaration" false (View.writeU8 view "value" 42uy)
        bytesEqual "invalid field write changes no backing bytes" (Array.zeroCreate 8) bytes
    for offset, length in [ -1, 4; 1, 8; 0, System.Int32.MinValue; System.Int32.MaxValue, 4 ] do
        let invalid : Region = { Data = Array.zeroCreate 8; Offset = offset; Length = length }
        equal "view refuses malformed backing region" true (View.create invalid (Layout.create 0 1 [||])).IsNone
        equal "malformed region contains no valid access" false (Region.contains invalid 1 1)
        equal "slice cannot legitimize a malformed region" true (Region.slice invalid 1 1).IsNone

        equal "malformed region has no absolute position" -1 (Region.absolute invalid 1)
        equal "copy cannot turn invalid metadata into empty data" None (Region.tryToArray invalid)
    let copied = Region.ofArray [| 1uy; 2uy; 3uy; 4uy |]
    match Region.slice copied 1 2 with
    | None -> check "valid region slice is available" false "refused"
    | Some middle ->
        equal "region copy preserves the declared slice" (Some [| 2uy; 3uy |]) (Region.tryToArray middle)
        equal "absolute address preserves the slice origin" 2 (Region.absolute middle 1)
        equal "absolute address rejects position beyond slice" -1 (Region.absolute middle 3)
    let empty = Region.ofArray [||]
    equal "valid empty region is distinguished from invalid data" (Some [||]) (Region.tryToArray empty)
    let arrayView = { Region = Region.ofArray [| 1uy; 2uy |]; Layout = Layout.create 2 1 [| Field.array "values" 0 Repr.U8 2 AccessKind.ReadWrite |] }
    equal "valid inline array retains scalar first-element access" (Some 1uy) (View.readU8 arrayView "values")
