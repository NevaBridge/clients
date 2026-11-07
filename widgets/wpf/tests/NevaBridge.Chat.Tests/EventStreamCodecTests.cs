namespace NevaBridge.Chat.Tests;

/// <summary>
/// The fixtures are the AWS event-stream test vectors (empty_message, payload_no_headers,
/// payload_one_str_header) and frames encoded by the <c>@smithy/eventstream-codec</c> the
/// NevaBridge Tenant UI uses, so this codec matches both byte for byte.
/// </summary>
public sealed class EventStreamCodecTests
{
    private const string EmptyMessage = "000000100000000005c248eb7d98c8ff";

    private const string PayloadNoHeaders = "0000001d00000000fd528c5a7b27666f6f273a27626172277dc3653936";

    private const string PayloadOneStringHeader =
        "0000003d0000002007fd83960c636f6e74656e742d747970650700106170706c69636174696f6e2f6a736f6e" +
        "7b27666f6f273a27626172277d8d9c08b1";

    /// <summary>Smithy's encoding of an AudioEvent carrying the bytes 1, 2, 3, 4.</summary>
    private const string AudioEventOf1234 =
        "0000006c000000585c509ce30d3a6d6573736167652d747970650700056576656e740b3a6576656e742d747970" +
        "6507000a417564696f4576656e740d3a636f6e74656e742d747970650700186170706c69636174696f6e2f6f63" +
        "7465742d73747265616d010203044c67b50e";

    /// <summary>Smithy's encoding of a TranscriptEvent with a final, a partial and an empty result.</summary>
    private const string TranscriptEvent =
        "0000016d00000055d4dd1a4b0d3a6d6573736167652d747970650700056576656e740b3a6576656e742d747970" +
        "6507000f5472616e7363726970744576656e740d3a636f6e74656e742d747970650700106170706c6963617469" +
        "6f6e2f6a736f6e7b225472616e736372697074223a7b22526573756c7473223a5b7b22526573756c744964223a" +
        "227231222c2249735061727469616c223a66616c73652c22416c7465726e617469766573223a5b7b225472616e" +
        "736372697074223a22446572204578706f7274206272696368742061622e227d5d7d2c7b22526573756c744964" +
        "223a227232222c2249735061727469616c223a747275652c22416c7465726e617469766573223a5b7b22547261" +
        "6e736372697074223a2253656974227d5d7d2c7b22526573756c744964223a227233222c224973506172746961" +
        "6c223a747275652c22416c7465726e617469766573223a5b7b225472616e736372697074223a22227d5d7d5d7d" +
        "7d555dadb1";

    /// <summary>Smithy's encoding of a BadRequestException.</summary>
    private const string ExceptionEvent =
        "000000960000006101d15c170d3a6d6573736167652d74797065070009657863657074696f6e0f3a6578636570" +
        "74696f6e2d7479706507001342616452657175657374457863657074696f6e0d3a636f6e74656e742d74797065" +
        "0700106170706c69636174696f6e2f6a736f6e7b224d657373616765223a22596f757220726571756573742074" +
        "696d6564206f75742e227d3c78a5ad";

    [Fact]
    public void Audio_is_framed_as_a_Transcribe_AudioEvent_exactly_as_the_reference_codec_frames_it()
    {
        // Given
        byte[] pcm = [1, 2, 3, 4];

        // When
        byte[] frame = EventStreamCodec.EncodeAudioEvent(pcm);

        // Then
        Assert.Equal(AudioEventOf1234, Convert.ToHexString(frame).ToLowerInvariant());
    }

    [Fact]
    public void Empty_audio_is_an_AudioEvent_without_payload_which_ends_the_stream()
    {
        // When
        EventStreamMessage message = EventStreamCodec.Decode(EventStreamCodec.EncodeAudioEvent([]));

        // Then
        Assert.Equal("AudioEvent", message.Headers[":event-type"]);
        Assert.Empty(message.Payload);
    }

    [Theory]
    [InlineData(EmptyMessage, "")]
    [InlineData(PayloadNoHeaders, "{'foo':'bar'}")]
    public void The_AWS_vectors_without_headers_decode_to_their_payload(string hex, string payload)
    {
        // When
        EventStreamMessage message = EventStreamCodec.Decode(Convert.FromHexString(hex));

        // Then
        Assert.Empty(message.Headers);
        Assert.Equal(payload, System.Text.Encoding.UTF8.GetString(message.Payload));
    }

    [Fact]
    public void The_AWS_vector_with_a_string_header_decodes_the_header_and_the_payload()
    {
        // When
        EventStreamMessage message = EventStreamCodec.Decode(Convert.FromHexString(PayloadOneStringHeader));

        // Then
        Assert.Equal("application/json", Assert.Single(message.Headers, h => h.Key == "content-type").Value);
        Assert.Equal("{'foo':'bar'}", System.Text.Encoding.UTF8.GetString(message.Payload));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(30)]
    [InlineData(60)]
    public void A_frame_with_a_damaged_byte_is_refused(int index)
    {
        // Given
        byte[] frame = Convert.FromHexString(PayloadOneStringHeader);
        frame[index] ^= 0x01;

        // When, Then
        Assert.Throws<InvalidDataException>(() => EventStreamCodec.Decode(frame));
    }

    [Fact]
    public void A_truncated_frame_is_refused()
    {
        // Given
        byte[] frame = Convert.FromHexString(PayloadNoHeaders)[..20];

        // When, Then
        Assert.Throws<InvalidDataException>(() => EventStreamCodec.Decode(frame));
    }

    [Fact]
    public void A_TranscriptEvent_reads_its_final_and_partial_results_and_drops_empty_ones()
    {
        // When
        TranscribeMessage? message = EventStreamCodec.DecodeTranscribeMessage(Convert.FromHexString(TranscriptEvent));

        // Then
        TranscribeResults results = Assert.IsType<TranscribeResults>(message);
        Assert.Equal(
            [new TranscriptResult("r1", "Der Export bricht ab.", IsPartial: false), new TranscriptResult("r2", "Seit", IsPartial: true)],
            results.Results);
    }

    [Fact]
    public void An_exception_reads_its_type_and_message()
    {
        // When
        TranscribeMessage? message = EventStreamCodec.DecodeTranscribeMessage(Convert.FromHexString(ExceptionEvent));

        // Then
        Assert.Equal(new TranscribeFailure("BadRequestException", "Your request timed out."), message);
    }

    [Fact]
    public void Another_event_type_means_nothing_to_the_listener()
    {
        // When
        TranscribeMessage? message = EventStreamCodec.DecodeTranscribeMessage(Convert.FromHexString(AudioEventOf1234));

        // Then
        Assert.Null(message);
    }
}
