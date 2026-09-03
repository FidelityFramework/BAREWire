namespace BAREWire.Encoding

/// Offset threading over a bounded byte extent.
///
/// Every encoder takes `(data, offset, value)` and returns the next offset.
/// Every decoder takes `(data, offset)` and returns `(value, nextOffset)`.
/// A read or write that would leave the extent, or a malformed input,
/// returns the fault offset instead of raising. Faults propagate: any
/// operation given a fault offset returns a fault offset, so a codec can be
/// composed straight through and the result checked once at the end.
///
/// This is the intersection-subset form of the design in
/// `docs/02 Encoding and Decoding Engine.md`: no exceptions, no mutable
/// buffer records, no interfaces. One source compiles under Fable, .NET and
/// Composer.
module Cursor =

    /// The fault offset. Any negative offset is a fault; this is the canonical one.
    [<Literal>]
    let Fault = -1

    /// True when the offset is a fault.
    let isFault (offset: int) : bool =
        offset < 0

    /// True when the offset is not a fault.
    let isOk (offset: int) : bool =
        offset >= 0

    /// True when `count` bytes starting at `offset` lie inside `data`.
    /// A fault offset never fits.
    let fits (data: byte array) (offset: int) (count: int) : bool =
        // Subtraction form: `offset + count` could wrap for a wire-supplied
        // count near the int maximum and pass the check it should fail.
        offset >= 0 && count >= 0 && count <= Array.length data - offset

    /// The number of bytes remaining from `offset` to the end of `data`,
    /// or zero on a fault.
    let remaining (data: byte array) (offset: int) : int =
        if offset < 0 || offset > Array.length data then 0 else Array.length data - offset
