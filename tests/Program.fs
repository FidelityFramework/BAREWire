module BAREWire.Tests.Program

[<EntryPoint>]
let main _ =
    BAREWire.Tests.EncodingTests.run ()
    BAREWire.Tests.FramingTests.run ()
    BAREWire.Tests.SchemaTests.run ()
    BAREWire.Tests.HardwareTests.run ()
    BAREWire.Tests.MemoryTests.run ()
    BAREWire.Tests.PlatformTests.run ()
    BAREWire.Tests.StaticStorageTests.run ()
    BAREWire.Tests.DispatchRegionTests.run ()
    BAREWire.Tests.TranscriptTests.run ()
    BAREWire.Tests.Harness.summary ()
