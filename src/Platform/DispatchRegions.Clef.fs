namespace BAREWire.Platform

/// Clef source projection of the hosted DispatchRegions scalar guards.
/// The equations are identical; int uses Clef's range/platform settlement,
/// while the hosted implementation explicitly selects System.Int64/JS BigInt.
/// tests/dispatch_projection.py verifies exact correspondence. This projection
/// does not implement the graph validator or establish lifecycle authority.
module DispatchRegions =

    [<Literal>]
    let private MaxExtent = 9223372036854775807

    /// Subtract before adding so even a malicious offset/length cannot wrap.
    /// A zero-length slice at the allocation endpoint is valid.
    let contains (byteLength: int) (offset: int) (length: int) : bool =
        byteLength >= 0 && offset >= 0 && length >= 0
        && offset <= byteLength && length <= byteLength - offset

    /// Same-allocation half-open extents are disjoint. Malformed or unrepresentable
    /// extents fail; empty extents do not overlap any valid extent.
    let disjoint (firstOffset: int) (firstLength: int) (secondOffset: int) (secondLength: int) : bool =
        if not (contains MaxExtent firstOffset firstLength) || not (contains MaxExtent secondOffset secondLength) then false
        elif firstLength = 0 || secondLength = 0 then true
        elif firstOffset <= secondOffset then firstLength <= secondOffset - firstOffset
        else secondLength <= firstOffset - secondOffset

    /// Guard a count-to-byte multiplication without evaluating the product.
    /// maxByteLength is a settled target/allocation limit, not the host word size.
    let fitsByteLength (maxByteLength: int) (count: int) (elementSize: int) : bool =
        maxByteLength >= 0 && count >= 0 && elementSize > 0
        && count <= maxByteLength / elementSize

