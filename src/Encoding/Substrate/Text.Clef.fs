namespace BAREWire.Encoding

/// UTF-8 text on the Clef substrate. Selected by `BAREWire.fidproj`.
///
/// A Clef string IS a length-carried memref of UTF-8 bytes. `String.toBytes`
/// and `String.fromBytes` are identity views over the same memory; nothing is
/// transcoded. (docs/02, "Primitive Encoding".)
module Text =

    /// The UTF-8 bytes of a string.
    let toUtf8 (s: string) : byte array =
        String.toBytes s

    /// A string over UTF-8 bytes.
    let ofUtf8 (bytes: byte array) : string =
        String.fromBytes bytes

    /// Concatenate two strings.
    let append (a: string) (b: string) : string =
        String.concat2 a b
