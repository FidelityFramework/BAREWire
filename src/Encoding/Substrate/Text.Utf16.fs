namespace BAREWire.Encoding

/// UTF-8 text on UTF-16 substrates (.NET and JavaScript via Fable). Selected
/// by `BAREWire.fsproj` and `BAREWire.Fable.fsproj`.
///
/// Hand-written transcoding, no `System.Text.Encoding`: the string is a
/// UTF-16 code-unit sequence on both substrates, so one implementation serves
/// both. Surrogate pairs become four-byte sequences; an unpaired surrogate is
/// encoded as U+FFFD, the replacement character, so the output is always
/// well-formed UTF-8. Decoding of malformed input likewise substitutes U+FFFD
/// and never reads past the extent.
module Text =

    let private utf8Length (s: string) : int =
        let mutable n = 0
        let mutable i = 0
        let len = s.Length
        while i < len do
            let c = int s.[i]
            if c < 0x80 then
                n <- n + 1
                i <- i + 1
            elif c < 0x800 then
                n <- n + 2
                i <- i + 1
            elif c >= 0xD800 && c <= 0xDBFF && i + 1 < len && int s.[i + 1] >= 0xDC00 && int s.[i + 1] <= 0xDFFF then
                n <- n + 4
                i <- i + 2
            else
                n <- n + 3
                i <- i + 1
        n

    /// The UTF-8 bytes of a string.
    let toUtf8 (s: string) : byte array =
        let out = Array.zeroCreate (utf8Length s)
        let mutable o = 0
        let mutable i = 0
        let len = s.Length
        while i < len do
            let c = int s.[i]
            if c < 0x80 then
                out.[o] <- byte c
                o <- o + 1
                i <- i + 1
            elif c < 0x800 then
                out.[o] <- byte (0xC0 ||| (c >>> 6))
                out.[o + 1] <- byte (0x80 ||| (c &&& 0x3F))
                o <- o + 2
                i <- i + 1
            elif c >= 0xD800 && c <= 0xDBFF && i + 1 < len && int s.[i + 1] >= 0xDC00 && int s.[i + 1] <= 0xDFFF then
                let lo = int s.[i + 1]
                let cp = 0x10000 + (((c - 0xD800) <<< 10) ||| (lo - 0xDC00))
                out.[o] <- byte (0xF0 ||| (cp >>> 18))
                out.[o + 1] <- byte (0x80 ||| ((cp >>> 12) &&& 0x3F))
                out.[o + 2] <- byte (0x80 ||| ((cp >>> 6) &&& 0x3F))
                out.[o + 3] <- byte (0x80 ||| (cp &&& 0x3F))
                o <- o + 4
                i <- i + 2
            else
                // BMP character, or an unpaired surrogate replaced by U+FFFD.
                let cp = if c >= 0xD800 && c <= 0xDFFF then 0xFFFD else c
                out.[o] <- byte (0xE0 ||| (cp >>> 12))
                out.[o + 1] <- byte (0x80 ||| ((cp >>> 6) &&& 0x3F))
                out.[o + 2] <- byte (0x80 ||| (cp &&& 0x3F))
                o <- o + 3
                i <- i + 1
        out

    /// A string over UTF-8 bytes.
    let ofUtf8 (bytes: byte array) : string =
        let len = bytes.Length
        // Upper bound: one UTF-16 unit per byte (four-byte sequences yield two units).
        let chars : char array = Array.zeroCreate len
        let mutable o = 0
        let mutable i = 0
        let cont (k: int) = if k < len then (int bytes.[k] &&& 0xC0) = 0x80 else false
        while i < len do
            let b0 = int bytes.[i]
            if b0 < 0x80 then
                chars.[o] <- char b0
                o <- o + 1
                i <- i + 1
            elif b0 >= 0xC2 && b0 < 0xE0 && cont (i + 1) then
                chars.[o] <- char (((b0 &&& 0x1F) <<< 6) ||| (int bytes.[i + 1] &&& 0x3F))
                o <- o + 1
                i <- i + 2
            elif b0 >= 0xE0 && b0 < 0xF0 && cont (i + 1) && cont (i + 2) then
                let cp = ((b0 &&& 0x0F) <<< 12) ||| ((int bytes.[i + 1] &&& 0x3F) <<< 6) ||| (int bytes.[i + 2] &&& 0x3F)
                // overlong (below U+0800) and surrogate code points are ill-formed
                chars.[o] <- (if cp < 0x800 || (cp >= 0xD800 && cp <= 0xDFFF) then char 0xFFFD else char cp)
                o <- o + 1
                i <- i + 3
            elif b0 >= 0xF0 && b0 < 0xF5 && cont (i + 1) && cont (i + 2) && cont (i + 3) then
                let cp =
                    ((b0 &&& 0x07) <<< 18) ||| ((int bytes.[i + 1] &&& 0x3F) <<< 12)
                    ||| ((int bytes.[i + 2] &&& 0x3F) <<< 6) ||| (int bytes.[i + 3] &&& 0x3F)
                if cp >= 0x10000 && cp <= 0x10FFFF then
                    let v = cp - 0x10000
                    chars.[o] <- char (0xD800 + (v >>> 10))
                    chars.[o + 1] <- char (0xDC00 + (v &&& 0x3FF))
                    o <- o + 2
                else
                    chars.[o] <- char 0xFFFD
                    o <- o + 1
                i <- i + 4
            else
                chars.[o] <- char 0xFFFD
                o <- o + 1
                i <- i + 1
        System.String(chars, 0, o)

    /// Concatenate two strings.
    let append (a: string) (b: string) : string =
        a + b

    /// The character at an index.
    let charAt (s: string) (i: int) : char =
        s.[i]
