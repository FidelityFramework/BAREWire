namespace BAREWire.Framing

open BAREWire.Encoding

/// The frame kind, as its wire byte. A byte alias with a constant module
/// rather than a union: the tag is the wire byte, and a record carrying a
/// nullary union field does not lower in the current Composer snapshot.
/// SUBSET(record-union-field): preferred spelling is a five-case FrameKind union.
type FrameKind = byte

[<RequireQualifiedAccess>]
module FrameKind =
    [<Literal>]
    let Tell: FrameKind = 0uy
    [<Literal>]
    let Ask: FrameKind = 1uy
    [<Literal>]
    let Reply: FrameKind = 2uy
    [<Literal>]
    let Event: FrameKind = 3uy
    [<Literal>]
    let Control: FrameKind = 4uy

    /// True when the byte names a kind.
    let isValid (k: FrameKind) : bool =
        k <= Control

/// One frame: a kind, a correlation id, and a BARE-encoded payload.
type Frame = {
    Kind: FrameKind
    Correlation: uint32
    Payload: byte array
}

/// A frame decoded from the front of a stream buffer, with the bytes
/// consumed. `Consumed` is `Envelope.Incomplete` when the buffer holds no
/// whole frame yet (read more and retry), and `Cursor.Fault` when a whole
/// frame is present but malformed (the stream is corrupt; do not retry).
type StreamDecode = {
    Frame: Frame
    Consumed: int
}

/// The schema-epoch handshake carried as the first Control frame.
type Hello = {
    Epoch: int32
    Build: string
}

/// The envelope (Readiness Audit §4 step 3; adopted from Conclave.Wire).
///
///   kind        u8      0 Tell, 1 Ask, 2 Reply, 3 Event, 4 Control
///   correlation u32 LE  0 when the kind carries none
///   payload     bytes   BARE, to the end of the frame
///
/// On a self-framing transport (a WebSocket binary message, a script
/// message) the frame is exactly these five bytes plus the payload. On a
/// stream transport a u32 LE length prefix precedes the frame. There are no
/// magic or version bytes: the schema is fixed at compile time on both ends,
/// and the epoch travels once, in the Hello control frame.
module Envelope =

    /// Header bytes on a self-framing transport.
    [<Literal>]
    let HeaderSize = 5

    /// Length-prefix bytes on a stream transport.
    [<Literal>]
    let LengthPrefixSize = 4

    /// The `Consumed` value of a stream decode that needs more bytes. Distinct
    /// from `Cursor.Fault`, which means the frame present is malformed.
    [<Literal>]
    let Incomplete = -2

    /// The largest frame body a stream transport admits: the u32 prefix must
    /// fit the platform int, and a frame is never larger than this.
    [<Literal>]
    let MaxStreamBody = 2147483647

    /// Write a frame at an offset: kind, correlation, payload. Returns the next offset.
    let writeMessage (data: byte array) (offset: int) (frame: Frame) : int =
        let o1 = Encoder.writeU8 data offset frame.Kind
        let o2 = Encoder.writeU32 data o1 frame.Correlation
        Encoder.writeBytesRaw data o2 frame.Payload

    /// The exact size of a frame on a self-framing transport.
    let messageSize (frame: Frame) : int =
        HeaderSize + Array.length frame.Payload

    /// Encode for a transport that frames messages itself.
    let encodeMessage (frame: Frame) : byte array =
        let data : byte array = Array.zeroCreate (messageSize frame)
        let _ = writeMessage data 0 frame
        data

    /// The frame a faulted decode returns: an empty Tell.
    let empty : Frame =
        { Kind = FrameKind.Tell; Correlation = uint32 0; Payload = Array.zeroCreate 0 }

    /// Decode a self-framed envelope: the payload is everything after the
    /// header. Returns the frame and the payload offset, or `empty` and
    /// `Cursor.Fault` when the header is short or the kind byte is unknown.
    /// The cursor tuple rather than `Frame option` keeps every decoder in one
    /// shape; a concrete option would also lower (docs/12, `generic-option`).
    let decodeMessage (bytes: byte array) : Frame * int =
        let kb, o1 = Decoder.readU8 bytes 0
        let correlation, o2 = Decoder.readU32 bytes o1
        let ok = Cursor.isOk o2 && FrameKind.isValid kb
        let payloadLen = if ok then Array.length bytes - o2 else 0
        let payload = if ok then Array.sub bytes o2 payloadLen else Array.zeroCreate 0
        let frame = { Kind = (if ok then kb else FrameKind.Tell); Correlation = (if ok then correlation else uint32 0); Payload = payload }
        (frame, (if ok then o2 else Cursor.Fault))

    /// Encode for a stream transport: u32 LE length, then the self-framed envelope.
    let encodeStream (frame: Frame) : byte array =
        let body = messageSize frame
        let data : byte array = Array.zeroCreate (LengthPrefixSize + body)
        let o1 = Encoder.writeU32 data 0 (uint32 body)
        let _ = writeMessage data o1 frame
        data

    /// Decode one stream-framed envelope from the front of a buffer. Three
    /// outcomes: the frame and the bytes consumed; `Incomplete` when the
    /// buffer is shorter than the prefix plus the announced body (read more
    /// and retry); `Cursor.Fault` when a whole frame is present but malformed
    /// (a body shorter than the header, an unknown kind byte, or a prefix
    /// that cannot fit), which a caller treats as a corrupt stream.
    /// SUBSET(value-match): a record result rather than a three-case union,
    /// which the preferred spelling would be.
    let tryDecodeStream (bytes: byte array) : StreamDecode =
        let len32, o1 = Decoder.readU32 bytes 0
        let prefixOk = Cursor.isOk o1 && len32 <= uint32 MaxStreamBody
        let len = if prefixOk then int len32 else 0
        let present = prefixOk && Cursor.fits bytes o1 len
        let start = if present then o1 else Cursor.Fault
        let body, _ = Decoder.readBytesRaw bytes start len
        let frame, at = decodeMessage body
        let wellFormed = present && len >= HeaderSize && Cursor.isOk at
        let incomplete = Cursor.isOk o1 && (not prefixOk || not present) && len32 <= uint32 MaxStreamBody || (Cursor.isFault o1)
        let consumed =
            if wellFormed then LengthPrefixSize + len
            elif incomplete then Incomplete
            else Cursor.Fault
        { Frame = frame; Consumed = consumed }

    /// A Tell frame: no correlation.
    let tell (payload: byte array) : Frame =
        { Kind = FrameKind.Tell; Correlation = uint32 0; Payload = payload }

    /// An Ask frame with a correlation id the Reply echoes.
    let ask (correlation: uint32) (payload: byte array) : Frame =
        { Kind = FrameKind.Ask; Correlation = correlation; Payload = payload }

    /// A Reply frame echoing its Ask's correlation id.
    let reply (correlation: uint32) (payload: byte array) : Frame =
        { Kind = FrameKind.Reply; Correlation = correlation; Payload = payload }

    /// An Event frame: no correlation.
    let event (payload: byte array) : Frame =
        { Kind = FrameKind.Event; Correlation = uint32 0; Payload = payload }

    /// A Control frame: no correlation.
    let control (payload: byte array) : Frame =
        { Kind = FrameKind.Control; Correlation = uint32 0; Payload = payload }

    /// Write a Hello: epoch as int, build as string.
    let writeHello (data: byte array) (offset: int) (h: Hello) : int =
        let o1 = Encoder.writeInt data offset (int64 h.Epoch)
        Encoder.writeString data o1 h.Build

    /// Read a Hello. An epoch outside the int32 range is a fault, not a
    /// wrapped value: the handshake exists to detect a mismatch.
    let readHello (data: byte array) (offset: int) : Hello * int =
        let epoch, o1 = Decoder.readInt data offset
        let build, o2 = Decoder.readString data o1
        let inRange = epoch >= -2147483648L && epoch <= 2147483647L
        let e = if inRange then int32 epoch else int32 0
        let next = if inRange then o2 else Cursor.Fault
        ({ Epoch = e; Build = build }, next)

    /// The Hello control frame that opens a session. The payload is at most
    /// ten bytes of epoch plus the build string.
    let hello (epoch: int32) (build: string) : Frame =
        let h = { Epoch = epoch; Build = build }
        let capacity = 20 + Array.length (Text.toUtf8 build)
        let data : byte array = Array.zeroCreate capacity
        let next = writeHello data 0 h
        let len = if Cursor.isOk next then next else 0
        control (Array.sub data 0 len)

    /// The Hello carried by a Control frame, with the bytes consumed, or
    /// `Cursor.Fault` when the frame is not a well-formed Hello.
    /// The cursor tuple rather than `Hello option`, for the same reason as
    /// `decodeMessage`.
    let tryReadHello (frame: Frame) : Hello * int =
        let h, consumed = Codec.decode readHello frame.Payload
        (h, (if frame.Kind = FrameKind.Control then consumed else Cursor.Fault))
