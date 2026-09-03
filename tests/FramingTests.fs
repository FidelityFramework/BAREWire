module BAREWire.Tests.FramingTests

open BAREWire.Encoding
open BAREWire.Framing
open BAREWire.Tests.Harness

/// The envelope's golden bytes and round trips, including the stand-in's
/// pinned vector from Conclave.Wire: a Tell of a one-byte payload `0x00`
/// is exactly six bytes, `00 00000000 00`.
let run () =
    let tell0 = Envelope.encodeMessage (Envelope.tell [| 0uy |])
    bytesEqual "tell of tag 0 is six bytes" [| 0uy; 0uy; 0uy; 0uy; 0uy; 0uy |] tell0
    let ask = Envelope.ask 9u [| 0xACuy; 0x02uy |]
    bytesEqual "ask header" [| 1uy; 9uy; 0uy; 0uy; 0uy; 0xACuy; 0x02uy |] (Envelope.encodeMessage ask)
    let frame, at = Envelope.decodeMessage (Envelope.encodeMessage ask)
    equal "decode payload offset" 5 at
    equal "decode kind" FrameKind.Ask frame.Kind
    equal "decode correlation" 9u frame.Correlation
    bytesEqual "decode payload" [| 0xACuy; 0x02uy |] frame.Payload
    let _, badKind = Envelope.decodeMessage [| 9uy; 0uy; 0uy; 0uy; 0uy |]
    equal "unknown kind faults" Cursor.Fault badKind
    let _, short = Envelope.decodeMessage [| 1uy; 0uy |]
    equal "short header faults" Cursor.Fault short
    for f in [ Envelope.tell [| 1uy |]; Envelope.ask 7u [| 2uy |]; Envelope.reply 7u [| 3uy |]; Envelope.event [| 4uy |]; Envelope.control [| 5uy |] ] do
        let back, _ = Envelope.decodeMessage (Envelope.encodeMessage f)
        equal "message round trip" f back
        let sd = Envelope.tryDecodeStream (Envelope.encodeStream f)
        equal "stream round trip" f sd.Frame
        equal "stream consumed" (4 + 5 + f.Payload.Length) sd.Consumed
    let stream = Envelope.encodeStream (Envelope.tell [| 1uy; 2uy |])
    bytesEqual "stream prefix" [| 7uy; 0uy; 0uy; 0uy |] (Array.sub stream 0 4)
    let shortStream = Envelope.tryDecodeStream (Array.sub stream 0 (stream.Length - 1))
    equal "short stream yields fault consumed" Cursor.Fault shortStream.Consumed
    let hello = Envelope.hello 1 "abc123"
    equal "hello is control" FrameKind.Control hello.Kind
    bytesEqual "hello payload" [| 0x02uy; 0x06uy; 0x61uy; 0x62uy; 0x63uy; 0x31uy; 0x32uy; 0x33uy |] hello.Payload
    let h, hc = Envelope.tryReadHello hello
    equal "hello epoch" 1 h.Epoch
    equal "hello build" "abc123" h.Build
    equal "hello consumed" 8 hc
    let _, notHello = Envelope.tryReadHello (Envelope.tell hello.Payload)
    equal "hello on a tell faults" Cursor.Fault notHello
