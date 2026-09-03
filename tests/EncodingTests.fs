module BAREWire.Tests.EncodingTests

open BAREWire.Encoding
open BAREWire.Tests.Harness

/// Golden vectors: byte-exact encodings every implementation must produce
/// (Readiness Audit §4 step 4). Values chosen at the boundaries of each
/// encoding. The same values are printed by samples/RoundTrip natively.
let private encodeWith (write: byte array -> int -> 'a -> int) (v: 'a) : byte array =
    let data : byte array = Array.zeroCreate 64
    let next = write data 0 v
    if Cursor.isOk next then Array.sub data 0 next else [| 0xEEuy |]

let run () =
    // ULEB128
    bytesEqual "uint 0" [| 0x00uy |] (encodeWith Encoder.writeUInt 0UL)
    bytesEqual "uint 1" [| 0x01uy |] (encodeWith Encoder.writeUInt 1UL)
    bytesEqual "uint 127" [| 0x7Fuy |] (encodeWith Encoder.writeUInt 127UL)
    bytesEqual "uint 128" [| 0x80uy; 0x01uy |] (encodeWith Encoder.writeUInt 128UL)
    bytesEqual "uint 300" [| 0xACuy; 0x02uy |] (encodeWith Encoder.writeUInt 300UL)
    bytesEqual "uint 16383" [| 0xFFuy; 0x7Fuy |] (encodeWith Encoder.writeUInt 16383UL)
    bytesEqual "uint 16384" [| 0x80uy; 0x80uy; 0x01uy |] (encodeWith Encoder.writeUInt 16384UL)
    bytesEqual "uint max" (Array.append (Array.create 9 0xFFuy) [| 0x01uy |]) (encodeWith Encoder.writeUInt System.UInt64.MaxValue)
    // zigzag
    bytesEqual "int 0" [| 0x00uy |] (encodeWith Encoder.writeInt 0L)
    bytesEqual "int -1" [| 0x01uy |] (encodeWith Encoder.writeInt -1L)
    bytesEqual "int 1" [| 0x02uy |] (encodeWith Encoder.writeInt 1L)
    bytesEqual "int -2" [| 0x03uy |] (encodeWith Encoder.writeInt -2L)
    bytesEqual "int 63" [| 0x7Euy |] (encodeWith Encoder.writeInt 63L)
    bytesEqual "int -64" [| 0x7Fuy |] (encodeWith Encoder.writeInt -64L)
    bytesEqual "int 64" [| 0x80uy; 0x01uy |] (encodeWith Encoder.writeInt 64L)
    bytesEqual "int min" (Array.append (Array.create 9 0xFFuy) [| 0x01uy |]) (encodeWith Encoder.writeInt System.Int64.MinValue)
    // fixed width, little-endian
    bytesEqual "u16" [| 0x34uy; 0x12uy |] (encodeWith Encoder.writeU16 0x1234us)
    bytesEqual "u32" [| 0x78uy; 0x56uy; 0x34uy; 0x12uy |] (encodeWith Encoder.writeU32 0x12345678u)
    bytesEqual "u64" [| 0xEFuy; 0xCDuy; 0xABuy; 0x89uy; 0x67uy; 0x45uy; 0x23uy; 0x01uy |] (encodeWith Encoder.writeU64 0x0123456789ABCDEFUL)
    bytesEqual "i8 -1" [| 0xFFuy |] (encodeWith Encoder.writeI8 -1y)
    bytesEqual "i16 -2" [| 0xFEuy; 0xFFuy |] (encodeWith Encoder.writeI16 -2s)
    bytesEqual "i32 -3" [| 0xFDuy; 0xFFuy; 0xFFuy; 0xFFuy |] (encodeWith Encoder.writeI32 -3)
    bytesEqual "i64 -4" [| 0xFCuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy |] (encodeWith Encoder.writeI64 -4L)
    bytesEqual "f32 1.0" [| 0x00uy; 0x00uy; 0x80uy; 0x3Fuy |] (encodeWith Encoder.writeF32 1.0f)
    bytesEqual "f64 1.0" [| 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0xF0uy; 0x3Fuy |] (encodeWith Encoder.writeF64 1.0)
    bytesEqual "f64 -2.5" [| 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x04uy; 0xC0uy |] (encodeWith Encoder.writeF64 -2.5)
    // bool, string, data
    bytesEqual "bool true" [| 0x01uy |] (encodeWith Encoder.writeBool true)
    bytesEqual "bool false" [| 0x00uy |] (encodeWith Encoder.writeBool false)
    bytesEqual "string empty" [| 0x00uy |] (encodeWith Encoder.writeString "")
    bytesEqual "string hello" [| 0x05uy; 0x68uy; 0x65uy; 0x6Cuy; 0x6Cuy; 0x6Fuy |] (encodeWith Encoder.writeString "hello")
    bytesEqual "string héllo" [| 0x06uy; 0x68uy; 0xC3uy; 0xA9uy; 0x6Cuy; 0x6Cuy; 0x6Fuy |] (encodeWith Encoder.writeString "héllo")
    bytesEqual "string emoji" [| 0x04uy; 0xF0uy; 0x9Fuy; 0x98uy; 0x80uy |] (encodeWith Encoder.writeString "\U0001F600")
    bytesEqual "data" [| 0x03uy; 0x01uy; 0x02uy; 0x03uy |] (encodeWith Encoder.writeData [| 1uy; 2uy; 3uy |])
    bytesEqual "fixed data" [| 0x01uy; 0x02uy |] (encodeWith (fun d o v -> Encoder.writeFixedData d o v 2) [| 1uy; 2uy |])
    equal "fixed data wrong length faults" Cursor.Fault (Encoder.writeFixedData (Array.zeroCreate 8) 0 [| 1uy |] 2)
    // aggregates
    bytesEqual "option none" [| 0x00uy |] (encodeWith (fun d o v -> Encoder.writeOption d o Encoder.writeU8 v) None)
    bytesEqual "option some" [| 0x01uy; 0x2Auy |] (encodeWith (fun d o v -> Encoder.writeOption d o Encoder.writeU8 v) (Some 42uy))
    bytesEqual "list" [| 0x02uy; 0x01uy; 0x80uy; 0x01uy |] (encodeWith (fun d o v -> Encoder.writeList d o Encoder.writeUInt v) [| 1UL; 128UL |])
    bytesEqual "fixed list" [| 0x07uy; 0x08uy |] (encodeWith (fun d o v -> Encoder.writeFixedList d o Encoder.writeU8 v 2) [| 7uy; 8uy |])
    bytesEqual "tag 3" [| 0x03uy |] (encodeWith Encoder.writeTag 3)
    equal "negative tag faults" Cursor.Fault (Encoder.writeTag (Array.zeroCreate 4) 0 -1)
    // overflow is a fault, not a throw
    equal "u32 into 3 bytes faults" Cursor.Fault (Encoder.writeU32 (Array.zeroCreate 3) 0 1u)
    equal "fault propagates" Cursor.Fault (Encoder.writeU8 (Array.zeroCreate 3) Cursor.Fault 1uy)
    equal "fits refuses a count near int max" false (Cursor.fits (Array.zeroCreate 8) 4 System.Int32.MaxValue)
    let _, hugeFixed = Decoder.readFixedData (Array.zeroCreate 8) 4 System.Int32.MaxValue
    equal "huge fixed read faults" Cursor.Fault hugeFixed

    // round trips through the decoder
    let roundUInt (v: uint64) =
        let bytes = encodeWith Encoder.writeUInt v
        let back, next = Decoder.readUInt bytes 0
        equal (sprintf "uint round %d" v) v back
        equal (sprintf "uint consumed %d" v) bytes.Length next
    for v in [ 0UL; 1UL; 127UL; 128UL; 300UL; 16383UL; 16384UL; 4294967295UL; System.UInt64.MaxValue ] do roundUInt v
    let roundInt (v: int64) =
        let bytes = encodeWith Encoder.writeInt v
        let back, _ = Decoder.readInt bytes 0
        equal (sprintf "int round %d" v) v back
    for v in [ 0L; -1L; 1L; -64L; 63L; 64L; -65L; System.Int64.MinValue; System.Int64.MaxValue ] do roundInt v
    let s, sn = Decoder.readString (encodeWith Encoder.writeString "héllo \U0001F600") 0
    equal "string round" "héllo \U0001F600" s
    equal "string consumed" 12 sn
    let f, _ = Decoder.readF64 (encodeWith Encoder.writeF64 3.141592653589793) 0
    equal "f64 round" 3.141592653589793 f
    let g, _ = Decoder.readF32 (encodeWith Encoder.writeF32 2.5f) 0
    equal "f32 round" 2.5f g
    let items, ln = Decoder.readList (encodeWith (fun d o v -> Encoder.writeList d o Encoder.writeUInt v) [| 1UL; 128UL; 300UL |]) 0 Decoder.readUInt
    equal "list round" [| 1UL; 128UL; 300UL |] items
    equal "list consumed" 6 ln
    let opt, _ = Decoder.readOption (encodeWith (fun d o v -> Encoder.writeOption d o Encoder.writeU8 v) (Some 9uy)) 0 Decoder.readU8
    equal "option round" (Some 9uy) opt

    // malformed input faults and never reads out of bounds
    let _, badBool = Decoder.readBool [| 2uy |] 0
    equal "bad bool faults" Cursor.Fault badBool
    let _, badOpt = Decoder.readOption [| 7uy |] 0 Decoder.readU8
    equal "bad option tag faults" Cursor.Fault badOpt
    let _, truncated = Decoder.readU32 [| 1uy; 2uy |] 0
    equal "truncated u32 faults" Cursor.Fault truncated
    let _, longVarint = Decoder.readUInt (Array.create 11 0x80uy) 0
    equal "11-byte varint faults" Cursor.Fault longVarint
    let _, tenthOverflow = Decoder.readUInt (Array.append (Array.create 9 0xFFuy) [| 0x7Fuy |]) 0
    equal "tenth byte beyond 64 bits faults" Cursor.Fault tenthOverflow
    let _, tenthBit1 = Decoder.readUInt (Array.append (Array.create 9 0x80uy) [| 0x02uy |]) 0
    equal "tenth byte bit 1 faults" Cursor.Fault tenthBit1
    let maxBack, maxNext = Decoder.readUInt (Array.append (Array.create 9 0xFFuy) [| 0x01uy |]) 0
    equal "uint max still decodes" System.UInt64.MaxValue maxBack
    equal "uint max consumed" 10 maxNext
    equal "remaining past end is zero" 0 (Cursor.remaining (Array.zeroCreate 4) 9)
    let _, hugeData = Decoder.readData [| 0xFFuy; 0xFFuy; 0x7Fuy; 1uy |] 0
    equal "data longer than buffer faults" Cursor.Fault hugeData
    let _, hugeList = Decoder.readList [| 0xFFuy; 0x7Fuy |] 0 Decoder.readU8
    equal "list count beyond buffer faults" Cursor.Fault hugeList
    let _, faultIn = Decoder.readU8 [| 1uy |] Cursor.Fault
    equal "decoder fault propagates" Cursor.Fault faultIn

    // codec combinators
    let enc, encLen = Codec.encode 8 Encoder.writeUInt 300UL
    bytesEqual "codec encode" [| 0xACuy; 0x02uy |] enc
    equal "codec encode length" 2 encLen
    let _, tooSmall = Codec.encode 1 Encoder.writeUInt 300UL
    equal "codec encode overflow faults" Cursor.Fault tooSmall
    let dec, decLen = Codec.decode Decoder.readUInt [| 0xACuy; 0x02uy |]
    equal "codec decode" 300UL dec
    equal "codec decode consumed" 2 decLen
    let _, trailing = Codec.decode Decoder.readUInt [| 0xACuy; 0x02uy; 0x00uy |]
    equal "codec decode trailing bytes fault" Cursor.Fault trailing

    // formatting
    equal "fmt int" "-1234" (Fmt.ofInt64 -1234L)
    equal "fmt uint64 max" "18446744073709551615" (Fmt.ofUInt64 System.UInt64.MaxValue)
    equal "fmt hex" "0xbeef" (Fmt.hex64 48879UL)
    equal "fmt hex zero" "0x0" (Fmt.hex64 0UL)
    equal "fmt join" "a, b, c" (Fmt.join ", " [| "a"; "b"; "c" |])
    equal "fmt join empty" "" (Fmt.join ", " [||])
    // text shim
    equal "utf8 of unpaired surrogate is U+FFFD" [| 0xEFuy; 0xBFuy; 0xBDuy |] (Text.toUtf8 "\uD800")
    equal "utf8 decode of malformed is U+FFFD" "�" (Text.ofUtf8 [| 0xC0uy |])
    equal "overlong three-byte NUL is U+FFFD" "�" (Text.ofUtf8 [| 0xE0uy; 0x80uy; 0x80uy |])
    equal "well-formed three-byte decodes" "€" (Text.ofUtf8 [| 0xE2uy; 0x82uy; 0xACuy |])
