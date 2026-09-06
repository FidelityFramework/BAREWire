module BAREWire.Tests.TranscriptTests

open System.IO
open BAREWire.Encoding
open BAREWire.Framing
open BAREWire.Hardware
open BAREWire.Memory
open BAREWire.Platform
open BAREWire.Tests.Harness

/// The RoundTrip sample's transcript, produced through the .NET build with the
/// same operations and labels as samples/RoundTrip/Main.clef, and compared
/// line by line with samples/RoundTrip/expected.txt. When the native gate
/// runs, its stdout is diffed against the same file: that is the
/// cross-substrate differential of Readiness Audit §4 step 4.
let transcript () : string array =
    let lines = System.Collections.Generic.List<string>()
    let show (label: string) (n: int) = lines.Add(sprintf "%s=%d" label n)
    let showText (label: string) (s: string) = lines.Add(sprintf "%s=%s" label s)
    let named (n: string) (r: Repr) : NamedRepr = { Name = n; Repr = r; Count = 1 }
    let data : byte array = Array.zeroCreate 64
    let o1 = Encoder.writeUInt data 0 300UL
    let o2 = Encoder.writeInt data o1 (0L - 2L)
    let o3 = Encoder.writeBool data o2 true
    let o4 = Encoder.writeString data o3 "héllo"
    let o5 = Encoder.writeU32 data o4 (uint32 305419896)
    let o6 = Encoder.writeTag data o5 3
    show "written" o6
    show "byte0" (int (Array.get data 0))
    show "byte1" (int (Array.get data 1))
    let v1, r1 = Decoder.readUInt data 0
    let v2, r2 = Decoder.readInt data r1
    let v3, r3 = Decoder.readBool data r2
    let v4, r4 = Decoder.readString data r3
    let v5, r5 = Decoder.readU32 data r4
    let v6, r6 = Decoder.readTag data r5
    show "uint" (int v1)
    show "int" (int v2)
    show "bool" (if v3 then 1 else 0)
    showText "str" v4
    show "u32lo" (int (v5 &&& uint32 65535))
    show "tag" v6
    show "read" r6
    let _, rbad = Decoder.readU32 data 62
    show "faultread" rbad
    let frame = Envelope.ask (uint32 9) (Array.sub data 0 o6)
    let bytes = Envelope.encodeMessage frame
    show "framelen" (Array.length bytes)
    let f, at = Envelope.decodeMessage bytes
    show "at" at
    show "corr" (int f.Correlation)
    let sd = Envelope.tryDecodeStream (Envelope.encodeStream frame)
    show "consumed" sd.Consumed
    let hello = Envelope.hello 1 "abc123"
    let hv, hc = Envelope.tryReadHello hello
    show "helloconsumed" hc
    showText "hellobuild" hv.Build
    let abi = Abi.sysvAmd64
    let sockaddr = derived abi "sockaddr_in" [| named "sin_family" Repr.U16; named "sin_port" Repr.U16; named "sin_addr" Repr.U32; { Name = "sin_zero"; Repr = Repr.U8; Count = 8 } |]
    show "sockaddrsize" sockaddr.Layout.Size
    let verdict = Validator.validate abi sockaddr
    show "sockaddragrees" (if verdict.Agrees then 1 else 0)
    show "pointerfree" (if Layout.isPointerFree sockaddr.Layout then 1 else 0)
    // ---- Memory: permitted writes and refusal without byte mutation ----
    let memory = Array.zeroCreate<byte> 8
    let region = BAREWire.Memory.Region.ofArray memory
    let layout = Layout.create 2 1 [|
        Field.simple "value" 0 Repr.U8 AccessKind.ReadWrite
        Field.simple "status" 1 Repr.U8 AccessKind.ReadOnly |]
    let view : View = { Region = region; Layout = layout }
    show "memorywrite" (if BAREWire.Memory.View.writeU8 view "value" (byte 42) then 1 else 0)
    show "memoryread" (match BAREWire.Memory.View.readU8 view "value" with Some value -> int value | None -> -1)
    show "readonlywrite" (if BAREWire.Memory.View.writeU8 view "status" (byte 99) then 1 else 0)
    show "readonlybyte" (int (Array.get memory 1))
    let outside : View =
        { Region = region; Layout = Layout.create 1 1 [| Field.simple "value" 1 Repr.U8 AccessKind.ReadWrite |] }
    show "outsidewrite" (if BAREWire.Memory.View.writeU8 outside "value" (byte 99) then 1 else 0)
    show "outsidebyte" (int (Array.get memory 1))

    // ---- Schema: packed wire extents and unresolved contract references ----
    let header = BAREWire.Schema.SchemaDSL.struct' [|
        BAREWire.Schema.SchemaDSL.field "tag" BAREWire.Schema.SchemaDSL.u8
        BAREWire.Schema.SchemaDSL.field "value" BAREWire.Schema.SchemaDSL.u32 |]
    let schema = BAREWire.Schema.SchemaDSL.withType "Header" header (BAREWire.Schema.SchemaDSL.schema "Header")
    show "schemafindings" (Array.length (BAREWire.Schema.Validation.validate schema))
    match BAREWire.Schema.Analysis.wireSize schema header with
    | Error findings -> show "wiresizefindings" (Array.length findings)
    | Ok wireSize ->
        show "wiremin" wireSize.Min
        show "wiremax" wireSize.Max
        show "wirefixed" (if wireSize.IsFixed then 1 else 0)
    let unresolved = BAREWire.Schema.SchemaDSL.schema "Missing"
    show "unresolvedschema" (Array.length (BAREWire.Schema.Validation.validate unresolved))

    let readContract = Contract.assumed "read-bound" "writes at most count bytes into buf; returns n <= count; n = 0 is end of input" [| "CWE-120" |]
    let spaces = [| MemorySpace.withGrowth (MemorySpace.create "stack" MemoryKind.Stack 8388608L 16) Growth.Down; MemorySpace.withGrowth (MemorySpace.create "arena" MemoryKind.Arena 4096L 16) Growth.Up |]
    let surfaces = [| BoundarySurface.create "syscalls" SurfaceKind.Syscall [| Endpoint.syscall "read" 0 [| readContract |] |] |]
    let buffers = [| BufferSchema.delimited "consoleReadln" "str" 1024L 10 true "arena" |]
    let desc : PlatformDescription =
        { Id = "cpu-linux-x86_64"; DisplayName = "Linux x86-64 (libc)"; Substrate = "CPU"; Core = None
          Spaces = spaces; Surfaces = surfaces; Buffers = buffers; Transports = [||]
          Lifecycle = Lifecycle.process' "_start" "exit_group" Persistence.Volatile; Notes = [||]; Limits = [||] }
    show "findings" (Array.length (Check.run desc))
    let obs = Obligations.ofDescription desc
    show "obligations" (Array.length obs)
    for o in obs do lines.Add(Obligations.ledgerLine o)
    show "readlncapacity" (int (PlatformDescription.capacityOf desc "consoleReadln"))
    lines.ToArray()

let run () =
    let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let expectedPath = Path.Combine(root, "samples", "RoundTrip", "expected.txt")
    let actual = transcript ()
    // A missing oracle is a failed gate, never permission to bless this output.
    let expected = File.ReadAllLines expectedPath
    equal "transcript line count" expected.Length actual.Length
    for i in 0 .. (min expected.Length actual.Length) - 1 do
        equal (sprintf "transcript line %d" (i + 1)) expected.[i] actual.[i]
