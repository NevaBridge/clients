using System.IO;
using NevaBridge.Sample.Client;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Which chats and features this application offers: the features file decides what may appear,
/// and the tenant's NevaBridge feature gates decide what NevaBridge allows. A feature appears
/// only when both say yes.
/// </summary>
internal static class NevaBridgeFeatureSwitches
{
    /// <summary>Read once at start-up; the application does not start without a valid file.</summary>
    public static SampleFeatures File { get; private set; } = new(false, false, false, false, false, false);

    public static void Load() =>
        File = SampleFeatures.Read(Path.Combine(AppContext.BaseDirectory, SampleFeatures.FileName));

    /// <summary>The Generic Intake chat: switched on in the file, and the tenant has <c>genericIntake</c>.</summary>
    public static bool GenericIntakeChat(IReadOnlyDictionary<string, bool> tenant) =>
        File.GenericIntakeChat && tenant.GetValueOrDefault("genericIntake");

    /// <summary>Attachments: switched on in the file, and the tenant has <c>chatAttachments</c>.</summary>
    public static bool Attachments(IReadOnlyDictionary<string, bool> tenant) =>
        File.Attachments && tenant.GetValueOrDefault("chatAttachments");

    /// <summary>Dictation: switched on in the file, and the tenant has <c>voiceDictation</c>.</summary>
    public static bool Dictation(IReadOnlyDictionary<string, bool> tenant) =>
        File.Dictation && tenant.GetValueOrDefault("voiceDictation");

    /// <summary>The hands-free conversation: switched on in the file, and the tenant has <c>voiceConversation</c>.</summary>
    public static bool HandsFreeVoice(IReadOnlyDictionary<string, bool> tenant) =>
        File.HandsFreeVoice && tenant.GetValueOrDefault("voiceConversation");
}
