module BAREWire.Tests.SchemaTests

open BAREWire.Schema
open BAREWire.Tests.Harness

/// The docs/03 messaging schema built with the DSL, validated, sized, emitted,
/// and compared across versions.
let run () =
    let messaging =
        SchemaDSL.schema "Message"
        |> SchemaDSL.withType "UserId" SchemaDSL.string
        |> SchemaDSL.withType "MessageId" SchemaDSL.string
        |> SchemaDSL.withType "Timestamp" SchemaDSL.int
        |> SchemaDSL.withType "Attachment" (SchemaDSL.optional SchemaDSL.data)
        |> SchemaDSL.withType "MessageType" (SchemaDSL.enum [| Bare.enumValue "TEXT" 0UL; Bare.enumValue "IMAGE" 1UL |])
        |> SchemaDSL.withType "Message" (SchemaDSL.struct' [|
            SchemaDSL.field "id" (SchemaDSL.typeRef "MessageId")
            SchemaDSL.field "sender" (SchemaDSL.typeRef "UserId")
            SchemaDSL.field "timestamp" (SchemaDSL.typeRef "Timestamp")
            SchemaDSL.field "attachment" (SchemaDSL.typeRef "Attachment")
            SchemaDSL.field "type" (SchemaDSL.typeRef "MessageType") |])
    equal "messaging schema valid" 0 (Array.length (Validation.validate messaging))

    let broken =
        SchemaDSL.schema "Root"
        |> SchemaDSL.withType "Loop" (SchemaDSL.struct' [| SchemaDSL.field "self" (SchemaDSL.typeRef "Loop") |])
        |> SchemaDSL.withType "Dangling" (SchemaDSL.struct' [| SchemaDSL.field "a" (SchemaDSL.typeRef "Nowhere") |])
    let errors = Validation.validate broken
    check "broken schema reports errors" (Array.length errors >= 3) (sprintf "%d errors" (Array.length errors))
    let kinds = errors |> Array.map (fun e -> e.Kind)
    check "undefined root reported" (Array.contains ValidationErrorKind.UndefinedRoot kinds) (String.concat "," kinds)
    check "cyclic struct reported" (Array.contains ValidationErrorKind.CyclicTypeReference kinds) (String.concat "," kinds)
    check "undefined type reported" (Array.contains ValidationErrorKind.UndefinedType kinds) (String.concat "," kinds)

    // legal indirection: a list of self is fine in BARE
    let tree =
        SchemaDSL.schema "Node"
        |> SchemaDSL.withType "Node" (SchemaDSL.struct' [| SchemaDSL.field "children" (SchemaDSL.list (SchemaDSL.typeRef "Node")) |])
    equal "recursion through list is legal" 0 (Array.length (Validation.validate tree))

    // wire sizes
    let header = SchemaDSL.struct' [| SchemaDSL.field "a" SchemaDSL.u8; SchemaDSL.field "b" SchemaDSL.u16; SchemaDSL.field "c" SchemaDSL.u32 |]
    let hs = Analysis.wireSize messaging header
    equal "fixed struct min" 7 hs.Min
    equal "fixed struct max" 7 hs.Max
    equal "fixed struct is fixed" true hs.IsFixed
    let ms = Analysis.wireSize messaging (Bare.typeRef "Message")
    equal "string-bearing struct unbounded" false ms.IsBounded
    let es = Analysis.wireSize messaging (Bare.typeRef "MessageType")
    equal "enum min" 1 es.Min
    // packed offsets: BARE structs have no alignment padding
    let extents = Analysis.packedOffsets messaging [| SchemaDSL.field "a" SchemaDSL.u8; SchemaDSL.field "b" SchemaDSL.u16; SchemaDSL.field "c" SchemaDSL.u32 |]
    equal "packed extents count" 3 (Array.length extents)
    equal "packed offset of c" 3 extents.[2].Offset
    equal "packed size of c" 4 extents.[2].Size

    // compatibility across versions
    let v1 = SchemaDSL.schema "Event" |> SchemaDSL.withType "Event" (SchemaDSL.union [| SchemaDSL.case 0 SchemaDSL.u8; SchemaDSL.case 1 SchemaDSL.string |])
    let v2 = SchemaDSL.schema "Event" |> SchemaDSL.withType "Event" (SchemaDSL.union [| SchemaDSL.case 0 SchemaDSL.u8; SchemaDSL.case 1 SchemaDSL.string; SchemaDSL.case 2 SchemaDSL.u32 |])
    equal "same schema is fully compatible" Compatibility.Full (Analysis.compatibility v1 v1)
    equal "added case is backward compatible" Compatibility.Backward (Analysis.compatibility v1 v2)
    equal "removed case is forward compatible" Compatibility.Forward (Analysis.compatibility v2 v1)

    // the interchange text
    let text = Emit.schema messaging
    check "emit names the root struct" (text.Contains "type Message struct {") text
    check "emit writes the optional" (text.Contains "type Attachment optional<data>") text
    check "emit writes the enum" (text.Contains "type MessageType enum { TEXT = 0 IMAGE = 1 }") text
    // stable: emitting twice gives the same text
    equal "emit is deterministic" text (Emit.schema messaging)
