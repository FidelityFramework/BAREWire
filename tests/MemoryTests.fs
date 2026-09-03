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
    let layout = Layout.create 16 8 [| Field.simple "a" 0 Repr.U32 AccessKind.ReadWrite; Field.simple "b" 4 Repr.I32 AccessKind.ReadWrite; Field.simple "c" 8 Repr.F64 AccessKind.ReadWrite |]
    match View.create region layout with
    | None -> check "view created" false "none"
    | Some view ->
        equal "write a" true (View.writeU32 view "a" 0xDEADBEEFu)
        equal "write b" true (View.writeI32 view "b" -7)
        equal "write c" true (View.writeF64 view "c" 2.5)
        equal "read a" (Some 0xDEADBEEFu) (View.readU32 view "a")
        equal "read b" (Some -7) (View.readI32 view "b")
        equal "read c" (Some 2.5) (View.readF64 view "c")
        equal "wrong repr refused" None (View.readU8 view "a")
        equal "unknown field refused" None (View.readU32 view "zz")
        // the bytes are little-endian at the declared offsets
        equal "a byte 0" 0xEFuy region.Data.[0]
        equal "b byte 4" 0xF9uy region.Data.[4]
    let small = Region.ofArray (Array.zeroCreate 8)
    equal "view over short region refused" true (View.create small layout).IsNone
