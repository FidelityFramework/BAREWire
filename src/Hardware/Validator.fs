namespace BAREWire.Hardware

open BAREWire.Encoding

/// The kind of a layout finding.
type FindingKind = string

[<RequireQualifiedAccess>]
module FindingKind =
    /// A field is declared before the field preceding it.
    [<Literal>]
    let OffsetMismatch: FindingKind = "offset-mismatch"
    /// A field's offset is not a multiple of its natural alignment.
    [<Literal>]
    let Misaligned: FindingKind = "misaligned"
    /// A field begins inside the field before it.
    [<Literal>]
    let Overlap: FindingKind = "overlap"
    /// Bytes between two fields that natural alignment does not require and no padding field covers.
    [<Literal>]
    let Gap: FindingKind = "gap"
    /// The declared size differs from the derived size.
    [<Literal>]
    let SizeMismatch: FindingKind = "size-mismatch"
    /// The declared alignment differs from the derived alignment.
    [<Literal>]
    let AlignmentMismatch: FindingKind = "alignment-mismatch"
    /// A bit field does not fit inside its register's width.
    [<Literal>]
    let BitFieldOutOfRange: FindingKind = "bitfield-out-of-range"
    /// A field's representation is not one the ABI profile knows.
    [<Literal>]
    let UnknownRepr: FindingKind = "unknown-repr"
    /// A field's inline count is zero or negative.
    [<Literal>]
    let ZeroCount: FindingKind = "zero-count"
    /// The descriptor declares more fields than `Layout.MaxFields`: it is not a descriptor.
    [<Literal>]
    let TooManyFields: FindingKind = "too-many-fields"
    /// Byte arithmetic cannot be represented by this descriptor implementation.
    [<Literal>]
    let ExtentOverflow: FindingKind = "extent-overflow"
    [<Literal>]
    let InvalidAbi: FindingKind = "invalid-abi"

/// One disagreement between a descriptor and the ABI's natural layout.
type LayoutFinding = {
    Field: string
    Kind: FindingKind
    Expected: int64
    Actual: int64
    Message: string
}

/// The validator's answer: agreement, the derived size and alignment, and
/// every finding. Zero findings means the descriptor agrees with the ABI.
type LayoutVerdict = {
    Agrees: bool
    ExpectedSize: int option
    ExpectedAlignment: int
    Findings: LayoutFinding array
}

/// A field as a C declaration states it: a name, a representation, and an
/// inline element count, with no offset. The validator derives the offsets.
type NamedRepr = {
    Name: string
    Repr: Repr
    Count: int
}

/// The struct-layout validator (Readiness Audit §4 step 5; docs/10).
///
/// `derive` lays out a field sequence the way a C compiler does under an
/// ABI profile: each field at the next offset aligned to its natural
/// alignment, the struct aligned to its widest field, the size rounded up
/// to that alignment. `validate` checks a declared descriptor against the
/// same rules and reports every disagreement by name. This is what corrected
/// Farscape output looks like, and what replaces HelloWayland's hand padding:
/// the contract written down once and checked, instead of written in a
/// comment and trusted.
module Validator =

    let private finding (field: string) (kind: FindingKind) (expected: int) (actual: int) (message: string) : LayoutFinding =
        { Field = field; Kind = kind; Expected = int64 expected; Actual = int64 actual; Message = message }

    let private push (items: LayoutFinding array) (item: LayoutFinding) : LayoutFinding array =
        let n = Array.length items
        let out : LayoutFinding array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get items i)
            i <- i + 1
        Array.set out n item
        out

    let private overflow (name: string) : LayoutFinding =
        finding name FindingKind.ExtentOverflow 0 0 "exact byte extent exceeds the descriptor implementation's integer representation"

    let private prerequisites (abi: AbiProfile) (name: string) (n: int) : LayoutFinding array =
        let mutable issues : LayoutFinding array = [||]
        if not (Abi.isValid abi) then
            issues <- push issues (finding abi.Name FindingKind.InvalidAbi 0 0 "ABI sizes and alignments must be positive powers of two within MaxAlign")
        if n > Layout.MaxFields then
            issues <- push issues (finding name FindingKind.TooManyFields Layout.MaxFields n "descriptor exceeds the vocabulary's maximum field count")
        issues

    /// Derive a natural layout, or explicit findings. Failed derivation never
    /// supplies a descriptor: validation, BTF emission and proof must not be
    /// handed plausible offsets after arithmetic has lost their meaning.
    let derive (abi: AbiProfile) (name: string) (fields: NamedRepr array) : Result<StructDescriptor, LayoutFinding array> =
        let n = Array.length fields
        let mutable issues = prerequisites abi name n
        if Array.length issues > 0 then Error issues
        else
            let out : FieldDescriptor array = Array.zeroCreate n
            let mutable cursor = 0
            let mutable maxAlign = 1
            let mutable i = 0
            while i < n && Array.length issues = 0 do
                let f = Array.get fields i
                let size = Abi.reprSize abi f.Repr
                let align = Abi.reprAlign abi f.Repr
                if not (Repr.isValid f.Repr) then
                    issues <- push issues (finding f.Name FindingKind.UnknownRepr 0 0 (Text.append "unknown representation " f.Repr))
                elif f.Count <= 0 then
                    issues <- push issues (finding f.Name FindingKind.ZeroCount 1 f.Count "inline count must be positive")
                else
                    match Abi.tryAlignUp cursor align with
                    | None -> issues <- push issues (overflow f.Name)
                    | Some offset ->
                        match Abi.tryFieldEnd offset size f.Count with
                        | None -> issues <- push issues (overflow f.Name)
                        | Some endpoint ->
                            Array.set out i { Name = f.Name; Offset = offset; Repr = f.Repr; Count = f.Count; Access = AccessKind.ReadWrite; BitFields = [||]; Documentation = None }
                            cursor <- endpoint
                            if align > maxAlign then maxAlign <- align
                i <- i + 1
            if Array.length issues > 0 then Error issues
            else
                match Abi.tryAlignUp cursor maxAlign with
                | None -> Error [| overflow name |]
                | Some size -> Ok { Name = name; Layout = { Size = size; Alignment = maxAlign; Fields = out }; Documentation = None }

    /// Validate actual declared offsets and extents as well as natural ABI
    /// alignment. Unknown arithmetic stays unknown; it never becomes zero.
    let private findingsWithin (abi: AbiProfile) (descriptor: StructDescriptor) (fields: FieldDescriptor array) (n: int) : LayoutVerdict =
        let mutable issues : LayoutFinding array = [||]
        let mutable prevStart = 0
        let mutable prevEnd = Some 0
        let mutable maxAlign = 1
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            let known = Repr.isValid f.Repr
            let size = Abi.reprSize abi f.Repr
            let align = Abi.reprAlign abi f.Repr
            if not known then
                issues <- push issues (finding f.Name FindingKind.UnknownRepr 0 0 (Text.append "unknown representation " f.Repr))
            if f.Count <= 0 then
                issues <- push issues (finding f.Name FindingKind.ZeroCount 1 f.Count "inline count must be positive")
            match prevEnd with
            | None -> ()
            | Some endpoint ->
                match Abi.tryAlignUp endpoint align with
                | None ->
                    issues <- push issues (overflow f.Name)
                    prevEnd <- None
                | Some expected ->
                    if known && f.Offset % align <> 0 then
                        issues <- push issues (finding f.Name FindingKind.Misaligned align f.Offset "offset is not a multiple of the natural alignment")
                    elif i > 0 && f.Offset < prevStart then
                        issues <- push issues (finding f.Name FindingKind.OffsetMismatch expected f.Offset "field is declared before the field preceding it")
                    elif f.Offset < endpoint then
                        issues <- push issues (finding f.Name FindingKind.Overlap endpoint f.Offset "field begins inside the preceding field")
                    elif f.Offset > expected then
                        issues <- push issues (finding f.Name FindingKind.Gap expected f.Offset "unexplained bytes before the field")
            let bits = int64 size * 8L
            let bitFields = f.BitFields
            let bn = Array.length bitFields
            let mutable b = 0
            while b < bn do
                let bf = Array.get bitFields b
                // Widen each operand first: Position + Width may overflow even
                // when the register itself is only eight bytes wide.
                let top = int64 bf.Position + int64 bf.Width
                if bf.Position < 0 || bf.Width < 1 || top > bits then
                    let where = Text.append (Text.append f.Name ".") bf.Name
                    issues <- push issues { Field = where; Kind = FindingKind.BitFieldOutOfRange; Expected = bits; Actual = top
                                            Message = Text.append "bit field does not fit a register of width " (Fmt.ofInt64 bits) }
                b <- b + 1
            if known && f.Count > 0 then
                match Abi.tryFieldEnd f.Offset size f.Count with
                | None ->
                    issues <- push issues (overflow f.Name)
                    prevEnd <- None
                | Some fieldEnd ->
                    match prevEnd with
                    | Some endpoint when fieldEnd > endpoint -> prevEnd <- Some fieldEnd
                    | _ -> ()
            else prevEnd <- None
            prevStart <- f.Offset
            if known && align > maxAlign then maxAlign <- align
            i <- i + 1
        let expectedSize =
            match prevEnd with
            | None -> None
            | Some endpoint ->
                let aligned = Abi.tryAlignUp endpoint maxAlign
                if aligned = None then issues <- push issues (overflow descriptor.Name)
                aligned
        match expectedSize with
        | Some size when descriptor.Layout.Size <> size ->
            issues <- push issues (finding descriptor.Name FindingKind.SizeMismatch size descriptor.Layout.Size "declared size differs from the natural size")
        | _ -> ()
        if descriptor.Layout.Alignment <> maxAlign then
            issues <- push issues (finding descriptor.Name FindingKind.AlignmentMismatch maxAlign descriptor.Layout.Alignment "declared alignment differs from the natural alignment")
        { Agrees = Array.length issues = 0; ExpectedSize = expectedSize; ExpectedAlignment = maxAlign; Findings = issues }

    /// Check the declaration; bounding field count alone does not bound byte
    /// extents or the number of bit-field findings. Each is checked on its own.
    let validate (abi: AbiProfile) (descriptor: StructDescriptor) : LayoutVerdict =
        let fields = descriptor.Layout.Fields
        let n = Array.length fields
        let issues = prerequisites abi descriptor.Name n
        if Array.length issues > 0 then
            { Agrees = false; ExpectedSize = None; ExpectedAlignment = 1; Findings = issues }
        else findingsWithin abi descriptor fields n

    /// One line per finding: `field: kind expected N actual M; message`.
    /// A verdict with no findings explains itself as agreement.
    let explain (verdict: LayoutVerdict) : string =
        let n = Array.length verdict.Findings
        if n = 0 then
            match verdict.ExpectedSize with
            | Some size -> Text.append (Text.append (Text.append "agrees: size " (Fmt.ofInt size)) " alignment ") (Fmt.ofInt verdict.ExpectedAlignment)
            | None -> "layout extent is unresolved"
        else
            let lines : string array = Array.zeroCreate n
            let mutable i = 0
            while i < n do
                let f = Array.get verdict.Findings i
                let head = Text.append (Text.append f.Field ": ") f.Kind
                let exp = Text.append " expected " (Fmt.ofInt64 f.Expected)
                let act = Text.append " actual " (Fmt.ofInt64 f.Actual)
                let line = Text.append (Text.append (Text.append head exp) act) (Text.append "; " f.Message)
                Array.set lines i line
                i <- i + 1
            Fmt.join "\n" lines
