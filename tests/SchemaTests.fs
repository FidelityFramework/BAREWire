module BAREWire.Tests.SchemaTests

open BAREWire.Schema
open BAREWire.Tests.Harness

let sized schema t =
    match Analysis.wireSize schema t with
    | Ok size -> size
    | Error errors -> failwithf "Valid size fixture failed: %A" errors

let fixedOffsets schema fields =
    match Analysis.packedOffsets schema fields with
    | Ok (Some offsets) -> offsets
    | other -> failwithf "Valid fixed layout fixture failed: %A" other

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

    // BARE forbids a type defined in terms of itself by any route, a list included
    let tree =
        SchemaDSL.schema "Node"
        |> SchemaDSL.withType "Node" (SchemaDSL.struct' [| SchemaDSL.field "children" (SchemaDSL.list (SchemaDSL.typeRef "Node")) |])
    check "recursion through list is refused" (Validation.validate tree |> Array.exists (fun e -> e.Kind = ValidationErrorKind.CyclicTypeReference)) "no cycle reported"
    // a bare void declaration used as a union case is legal; a negative tag and a repeated case type are not
    let voidCase =
        SchemaDSL.schema "E"
        |> SchemaDSL.withType "Nothing" SchemaDSL.void'
        |> SchemaDSL.withType "E" (SchemaDSL.union [| SchemaDSL.case 0 (SchemaDSL.typeRef "Nothing"); SchemaDSL.case 1 SchemaDSL.u8 |])
    equal "void via a named type in a union is legal" 0 (Array.length (Validation.validate voidCase))
    let badUnion = SchemaDSL.schema "U" |> SchemaDSL.withType "U" (SchemaDSL.union [| SchemaDSL.case -1 SchemaDSL.u8; SchemaDSL.case 1 SchemaDSL.u8 |])
    let badKinds = Validation.validate badUnion |> Array.map (fun e -> e.Kind)
    check "negative tag refused" (Array.contains ValidationErrorKind.InvalidTag badKinds) (String.concat "," badKinds)
    check "duplicate case type refused" (Array.contains ValidationErrorKind.DuplicateCaseType badKinds) (String.concat "," badKinds)
    // enums encode as uint whatever the base
    let es8 = sized messaging (SchemaDSL.enumWith PrimKind.U8 [| Bare.enumValue "A" 0UL |])
    equal "enum over u8 still sizes as a varint" false es8.IsFixed

    // wire sizes
    let header = SchemaDSL.struct' [| SchemaDSL.field "a" SchemaDSL.u8; SchemaDSL.field "b" SchemaDSL.u16; SchemaDSL.field "c" SchemaDSL.u32 |]
    let hs = sized messaging header
    equal "fixed struct min" 7 hs.Min
    equal "fixed struct max" 7 hs.Max
    equal "fixed struct is fixed" true hs.IsFixed
    let ms = sized messaging (Bare.typeRef "Message")
    equal "string-bearing struct unbounded" false ms.IsBounded
    let es = sized messaging (Bare.typeRef "MessageType")
    equal "enum min" 1 es.Min
    // packed offsets: BARE structs have no alignment padding
    let extents = fixedOffsets messaging [| SchemaDSL.field "a" SchemaDSL.u8; SchemaDSL.field "b" SchemaDSL.u16; SchemaDSL.field "c" SchemaDSL.u32 |]
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
    let fixedText = Emit.typeText messaging (SchemaDSL.fixedData 16)
    equal "fixed data uses BARE's bracket form" "data[16]" fixedText
    let text = Emit.schema messaging
    check "emit names the root struct" (text.Contains "type Message struct {") text
    check "emit writes the optional" (text.Contains "type Attachment optional<data>") text
    check "emit writes the enum" (text.Contains "type MessageType enum { TEXT = 0 IMAGE = 1 }") text
    // stable: emitting twice gives the same text
    equal "emit is deterministic" text (Emit.schema messaging)

    // Wire extents are allocation and contract premises, never wrapped host ints.
    let sizeAttempt t = Analysis.wireSize messaging t
    let offsetsAttempt fields = Analysis.packedOffsets messaging fields
    let reject name kind result =
        match result with
        | Error (findings: ValidationError array) ->
            check name (Array.exists (fun finding -> finding.Kind = kind) findings) (sprintf "%A" findings)
        | Ok _ -> check name false "analysis supplied a value for an invalid extent or missing fact"
    for name, t in [
        "4 GiB fixed list", SchemaDSL.fixedList SchemaDSL.u64 536870912
        "fixed list product", SchemaDSL.fixedList SchemaDSL.u16 1073741824
        "optional tag overflow", SchemaDSL.optional (Bare.fixedData 2147483647)
        "union tag overflow", SchemaDSL.union [| SchemaDSL.case 0 (Bare.fixedData 2147483647) |]
        "struct sum overflow", SchemaDSL.struct' [| SchemaDSL.field "a" (Bare.fixedData 2147483647); SchemaDSL.field "b" SchemaDSL.u8 |]
        "unbounded minimum overflow", SchemaDSL.struct' [| SchemaDSL.field "a" (Bare.fixedData 2147483647); SchemaDSL.field "b" SchemaDSL.string |]
    ] do reject name "extent-overflow" (sizeAttempt t)
    for n in [0; -1] do
        reject "invalid fixed data length" ValidationErrorKind.InvalidFixedLength (sizeAttempt (Bare.fixedData n))
        reject "invalid fixed list length" ValidationErrorKind.InvalidFixedLength (sizeAttempt (SchemaDSL.fixedList SchemaDSL.u8 n))
    for t in [SchemaDSL.typeRef "Missing"; SchemaDSL.list (SchemaDSL.typeRef "Missing"); SchemaDSL.optional (SchemaDSL.typeRef "Missing")] do
        reject "missing fact is not an unbounded size" ValidationErrorKind.UndefinedType (sizeAttempt t)
    reject "unknown primitive is not zero bytes" ValidationErrorKind.InvalidPrimitive (sizeAttempt (Prim "missing"))
    reject "empty union is not zero bytes" ValidationErrorKind.EmptyUnion (sizeAttempt (SchemaDSL.union [||]))
    reject "empty struct is not zero bytes" ValidationErrorKind.EmptyStruct (sizeAttempt (SchemaDSL.struct' [||]))
    reject "packed endpoint cannot wrap" "extent-overflow"
        (offsetsAttempt [| SchemaDSL.field "a" (Bare.fixedData 2147483647); SchemaDSL.field "b" SchemaDSL.u8; SchemaDSL.field "c" SchemaDSL.u8 |])
    reject "packed missing type cannot become empty offsets" ValidationErrorKind.UndefinedType
        (offsetsAttempt [| SchemaDSL.field "a" (SchemaDSL.typeRef "Missing") |])
    let largest = sized messaging (Bare.fixedData 2147483647)
    equal "largest hosted fixed extent stays exact" 2147483647 largest.Min
    equal "largest hosted extent is fixed" true largest.IsFixed
    let largestList = sized messaging (SchemaDSL.fixedList SchemaDSL.u8 2147483647)
    equal "largest fixed list stays exact" 2147483647 largestList.Max
    let largestOptional = sized messaging (SchemaDSL.optional (Bare.fixedData 2147483646))
    equal "optional minimum retains its tag" 1 largestOptional.Min
    equal "optional maximum at hosted limit" 2147483647 largestOptional.Max
    let largestUnion = sized messaging (SchemaDSL.union [| SchemaDSL.case 0 (Bare.fixedData 2147483646) |])
    equal "union tag at hosted limit" 2147483647 largestUnion.Min
    let endFields = [| SchemaDSL.field "a" (Bare.fixedData 2147483646); SchemaDSL.field "b" SchemaDSL.u8 |]
    let endOffsets = fixedOffsets messaging endFields
    equal "last valid packed offset" 2147483646 endOffsets.[1].Offset
    equal "last valid packed field size" 1 endOffsets.[1].Size
    match Analysis.packedOffsets messaging [| SchemaDSL.field "value" SchemaDSL.string |] with
    | Ok None -> check "dynamic offsets remain unknown" true ""
    | other -> check "dynamic offsets remain unknown" false (sprintf "%A" other)
    reject "cyclic types are errors, not unbounded facts" ValidationErrorKind.CyclicTypeReference (Analysis.wireSize tree (SchemaDSL.typeRef "Node"))
    reject "invalid length under dynamic list" ValidationErrorKind.InvalidFixedLength (sizeAttempt (SchemaDSL.list (Bare.fixedData -1)))
    reject "fixed-width query cannot hide missing facts" ValidationErrorKind.UndefinedType (Analysis.isFixedWidth messaging (SchemaDSL.typeRef "Missing"))
