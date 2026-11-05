using NevaBridge.Sample.Client;

namespace NevaBridge.Sample.Tests;

public sealed class SampleFeaturesTests
{
    [Fact]
    public void A_complete_file_switches_each_feature()
    {
        // Given
        string json = """{"genericIntakeChat": true, "classicChat": false, "webChat": true, "attachments": false}""";

        // When
        SampleFeatures features = SampleFeatures.Parse(json, SampleFeatures.FileName);

        // Then
        Assert.Equal(new SampleFeatures(GenericIntakeChat: true, ClassicChat: false, WebChat: true, Attachments: false), features);
    }

    [Theory]
    [InlineData("""{"genericIntakeChat": true, "classicChat": true, "webChat": true}""", "missing attachments")]
    [InlineData("""{}""", "missing genericIntakeChat, classicChat, webChat, attachments")]
    [InlineData("""{"genericIntakeChat": true, "classicChat": true, "webChat": true, "attachments": true, "voice": true}""", "unknown key 'voice'")]
    [InlineData("""{"genericIntakeChat": "yes", "classicChat": true, "webChat": true, "attachments": true}""", "'genericIntakeChat' must be true or false")]
    [InlineData("""[true]""", "must hold a JSON object")]
    [InlineData("""{"genericIntakeChat": tru""", "is not valid JSON")]
    public void An_invalid_file_is_refused_with_what_is_wrong(string json, string problem)
    {
        // When
        SampleFeaturesException error = Assert.Throws<SampleFeaturesException>(() => SampleFeatures.Parse(json, "features.json"));

        // Then
        Assert.StartsWith("features.json", error.Message);
        Assert.Contains(problem, error.Message);
    }

    [Fact]
    public void A_missing_file_is_refused_with_its_path()
    {
        // Given
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", SampleFeatures.FileName);

        // When
        SampleFeaturesException error = Assert.Throws<SampleFeaturesException>(() => SampleFeatures.Read(path));

        // Then
        Assert.StartsWith(path, error.Message);
    }

    [Fact]
    public void The_shipped_file_switches_every_feature_on()
    {
        // Given
        string path = Path.Combine(AppContext.BaseDirectory, SampleFeatures.FileName);

        // When
        SampleFeatures features = SampleFeatures.Read(path);

        // Then
        Assert.Equal(new SampleFeatures(true, true, true, true), features);
    }
}
