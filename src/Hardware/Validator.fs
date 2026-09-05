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

/// One disagreement between a descriptor and the ABI's natural layout.
type LayoutFinding = {
    Field: string
    Kind: FindingKind
    Expected: int
    Actual: int
    Message: string
}

/// The validator's answer: agreement, the derived size and alignment, and
/// every finding. Zero findings means the descriptor agrees with the ABI.
type LayoutVerdict = {
    Agrees: bool
    ExpectedSize: int
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
        { Field = field; Kind = kind; Expected = expected; Actual = actual; Message = message }

    /// The natural C layout of a field sequence under an ABI profile. The field-count
    /// invariant (`Layout.MaxFields`) is checked by `validate`, which every derived
    /// descriptor is meant to pass through; a sequence beyond it derives a layout that
    /// `validate` reports as `TooManyFields`.
    let derive (abi: AbiProfile) (name: string) (fields: NamedRepr array) : StructDescriptor =
        let n = Array.length fields
        let out : FieldDescriptor array = Array.zeroCreate n
        let none : BitFieldDescriptor array = Array.zeroCreate 0
        let mutable cursor = 0
        let mutable maxAlign = 1
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            let size = Abi.reprSize abi f.Repr
            let align = Abi.reprAlign abi f.Repr
            let count = if f.Count < 0 then 0 else f.Count
            let offset = Abi.alignUp cursor align
            let placed : FieldDescriptor =
                { Name = f.Name; Offset = offset; Repr = f.Repr; Count = count; Access = AccessKind.ReadWrite; BitFields = none; Documentation = None }
            Array.set out i placed
            cursor <- offset + size * count
            if align > maxAlign then
                maxAlign <- align
            i <- i + 1
        let layout : PeripheralLayout = { Size = Abi.alignUp cursor maxAlign; Alignment = maxAlign; Fields = out }
        { Name = name; Layout = layout; Documentation = None }

    /// The findings of a descriptor whose field count `n` is within `Layout.MaxFields` (the
    /// caller, `validate`, established that bound, so every count below is derived from a
    /// bounded one: the finding buffer is sized by it).
    let private findingsWithin (abi: AbiProfile) (descriptor: StructDescriptor) (fields: FieldDescriptor array) (n: int) : LayoutVerdict =
        // Upper bound on findings: three per field, one per bit field, two for the struct.
        let mutable bitCount = 0
        let mutable k = 0
        while k < n do
            let f = Array.get fields k
            bitCount <- bitCount + Array.length f.BitFields
            k <- k + 1
        let buf : LayoutFinding array = Array.zeroCreate (3 * n + bitCount + 2)
        let mutable count = 0
        let mutable prevStart = 0
        let mutable prevEnd = 0
        let mutable maxAlign = 1
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            let known = Repr.isValid f.Repr
            let size = Abi.reprSize abi f.Repr
            let align = Abi.reprAlign abi f.Repr
            if not known then
                Array.set buf count (finding f.Name FindingKind.UnknownRepr 0 0 (Text.append "unknown representation " f.Repr))
                count <- count + 1
            if f.Count <= 0 then
                Array.set buf count (finding f.Name FindingKind.ZeroCount 1 f.Count "inline count must be positive")
                count <- count + 1
            let expected = Abi.alignUp prevEnd align
            if known && f.Offset % align <> 0 then
                Array.set buf count (finding f.Name FindingKind.Misaligned align f.Offset (Text.append "offset is not a multiple of the natural alignment " (Fmt.ofInt align)))
                count <- count + 1
            elif i > 0 && f.Offset < prevStart then
                Array.set buf count (finding f.Name FindingKind.OffsetMismatch expected f.Offset "field is declared before the field preceding it")
                count <- count + 1
            elif f.Offset < prevEnd then
                Array.set buf count (finding f.Name FindingKind.Overlap prevEnd f.Offset (Text.append "field begins inside the preceding field, which ends at " (Fmt.ofInt prevEnd)))
                count <- count + 1
            elif f.Offset > expected then
                Array.set buf count (finding f.Name FindingKind.Gap expected f.Offset (Text.append (Text.append (Fmt.ofInt (f.Offset - expected)) " unexplained bytes before the field; natural offset is ") (Fmt.ofInt expected)))
                count <- count + 1
            let bits = size * 8
            let bitFields = f.BitFields
            let bn = Array.length bitFields
            let mutable b = 0
            while b < bn do
                let bf = Array.get bitFields b
                let top = bf.Position + bf.Width
                if bf.Position < 0 || bf.Width < 1 || top > bits then
                    let where = Text.append (Text.append f.Name ".") bf.Name
                    Array.set buf count (finding where FindingKind.BitFieldOutOfRange bits top (Text.append "bit field does not fit a register of width " (Fmt.ofInt bits)))
                    count <- count + 1
                b <- b + 1
            let elements = if f.Count < 0 then 0 else f.Count
            let fieldEnd = f.Offset + size * elements
            prevStart <- f.Offset
            if fieldEnd > prevEnd then
                prevEnd <- fieldEnd
            if known && align > maxAlign then
                maxAlign <- align
            i <- i + 1
        let expectedSize = Abi.alignUp prevEnd maxAlign
        if descriptor.Layout.Size <> expectedSize then
            Array.set buf count (finding descriptor.Name FindingKind.SizeMismatch expectedSize descriptor.Layout.Size (Text.append "declared size differs from the natural size " (Fmt.ofInt expectedSize)))
            count <- count + 1
        if descriptor.Layout.Alignment <> maxAlign then
            Array.set buf count (finding descriptor.Name FindingKind.AlignmentMismatch maxAlign descriptor.Layout.Alignment (Text.append "declared alignment differs from the natural alignment " (Fmt.ofInt maxAlign)))
            count <- count + 1
        { Agrees = count = 0; ExpectedSize = expectedSize; ExpectedAlignment = maxAlign; Findings = Array.sub buf 0 count }

    /// Check a declared descriptor against the ABI's natural layout rules. A descriptor with
    /// more fields than `Layout.MaxFields` is not a descriptor: the verdict is that one finding,
    /// and every other count is derived from a field count within the declared bound.
    let validate (abi: AbiProfile) (descriptor: StructDescriptor) : LayoutVerdict =
        let fields = descriptor.Layout.Fields
        let n = Array.length fields
        if n > Layout.MaxFields then
            let only = finding descriptor.Name FindingKind.TooManyFields Layout.MaxFields n (Text.append "the descriptor declares more fields than the vocabulary's maximum of " (Fmt.ofInt Layout.MaxFields))
            { Agrees = false; ExpectedSize = 0; ExpectedAlignment = 1; Findings = [| only |] }
        else
            findingsWithin abi descriptor fields n

    /// One line per finding: `field: kind expected N actual M; message`.
    /// A verdict with no findings explains itself as agreement.
    let explain (verdict: LayoutVerdict) : string =
        let n = Array.length verdict.Findings
        if n = 0 then
            Text.append (Text.append (Text.append "agrees: size " (Fmt.ofInt verdict.ExpectedSize)) " alignment ") (Fmt.ofInt verdict.ExpectedAlignment)
        else
            let lines : string array = Array.zeroCreate n
            let mutable i = 0
            while i < n do
                let f = Array.get verdict.Findings i
                let head = Text.append (Text.append f.Field ": ") f.Kind
                let exp = Text.append " expected " (Fmt.ofInt f.Expected)
                let act = Text.append " actual " (Fmt.ofInt f.Actual)
                let line = Text.append (Text.append (Text.append head exp) act) (Text.append "; " f.Message)
                Array.set lines i line
                i <- i + 1
            Fmt.join "\n" lines
