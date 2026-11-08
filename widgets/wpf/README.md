# Integrate NevaBridge into a WPF application

Use native WPF controls or show the web chat inside WebView2. Your application reads the API key
and product id from its database and makes the API calls. The web page never receives the key.
Build the components from this repository; a published NuGet package is not required.

To try the demo without a database, follow the [WPF sample guide](../../samples/dotnet-wpf/README.md).
The [changelog](CHANGELOG.md) lists what changed between revisions.

## What the package offers

### Two kinds of conversation: use Generic Intake

The package runs two kinds of conversation:

- **Generic Intake** (`NevaBridgeIntakeChatControl`, and the hosted web chat when the tenant has
  Generic Intake): the assistant works out what the reporter needs, answers questions, and one
  conversation can file several reports. It supports file attachments, dictation and a hands-free
  voice conversation.
- **The classic chat** (`NevaBridgeChatControl`): the reporter first picks a report type (bug
  report, feature request or support request), and one conversation files one report. It has no
  attachments and no voice.

We recommend Generic Intake for every new integration and for upgrading an existing one. Hide the
classic chat UI from your users rather than offering both. If you want a way back in an
emergency, keep the classic chat behind a feature toggle that is off by default, as the sample's
`classicChat` switch does, so you can turn it on again without a new release. The classic chat
stays supported; it receives no new features.

### Generic Intake is designed around polling

Each message starts a turn that runs in the background on NevaBridge. The chat does not hold one
request open for the whole reply: it starts the turn, then asks for the turn's events again and
again (`getIntakeTurnEvents`, each call waiting up to 20 seconds for something new). The events
say what the assistant is doing (for example reading attachments or searching knowledge) and
carry the reply as it is written. When the turn is done, the chat reads the saved conversation.

A reply therefore takes a few seconds to start and then grows in steps, as each poll returns,
rather than as a continuous stream. Build your UI so the user always sees that something is
happening: animate the waiting state (a typing indicator, a pulsing status line or a progress
animation) and show the assistant's current status next to it. The native control shows the
status as text; adding an animation around it is a good place to match your application's look.
Losing a poll answer is harmless: the chat asks again from where it left off, and a turn already
running is followed rather than started twice.

### Attachments and voice

- **Attachments:** the reporter attaches files, sees them listed and removes them. NevaBridge
  reads them while it answers; images get a description first. Requires the tenant feature
  `chatAttachments`. In the native Generic Intake chat, files can be attached once the first
  reply has been saved.
- **Dictation:** Dictate types what the reporter says into the message box. Requires
  `voiceDictation`.
- **Hands-free conversation:** Talk sends what the reporter said after a pause, reads the reply
  aloud and listens again. Requires `voiceConversation`.

### Turning features on and off: configuration or code

A feature appears only when two things allow it: your application decides to offer it, and the
tenant's NevaBridge feature gate allows it (read with `GetFeaturesAsync`). Always check the
tenant's gates; they reflect what the tenant's plan includes.

For your own decision, choose what fits your release process:

- **Feature switches in configuration**, as the sample does with its
  [features file](../../samples/dotnet-wpf/README.md#features-file): you can switch each chat,
  attachments, dictation and the hands-free conversation on or off per installation without a
  rebuild. This also gives you the emergency switch back to the classic chat described above.
- **Fixed in code**: decide once which features your UI offers and leave out the switches. This is
  simpler when every installation should look the same.

## Choose an integration

| Option                                                         | UI updates                                    | Database setting `WebChatUrl`                        |
| -------------------------------------------------------------- | --------------------------------------------- | ---------------------------------------------------- |
| [1. Native WPF](#1-native-wpf-chat)                            | You update the controls with your application | `NULL` (unused)                                      |
| [2. NevaBridge-hosted web chat](#2-nevabridge-hosted-web-chat) | NevaBridge deploys UI updates                 | `https://chat.app.nevabridge.com/v1/`                |
| [3. Your own copy](#3-bundle-or-host-your-own-copy)            | You update the bundled or self-hosted files   | `NULL` for bundled files, or your own HTTPS page URL |

## Common setup

### Build the components into your application

You need Git, Windows 10 or 11, the .NET 8 SDK, and a WPF application targeting `net8.0-windows`
or a compatible later target. Web chat also needs the Microsoft Edge WebView2 Runtime.

Clone beside your project so its source files are outside your application's default compile glob.
From the directory containing your application project, replace `YourApp.csproj` with its name:

```powershell
git clone https://github.com/NevaBridge/clients.git ../nevabridge-clients
dotnet add YourApp.csproj reference ../nevabridge-clients/widgets/wpf/src/NevaBridge.Wpf/NevaBridge.Wpf.csproj
dotnet add YourApp.csproj reference ../nevabridge-clients/widgets/wpf/src/NevaBridge.Chat/NevaBridge.Chat.csproj
dotnet add YourApp.csproj reference ../nevabridge-clients/samples/dotnet-wpf/src/NevaBridge.Sample.Client/NevaBridge.Sample.Client.csproj
dotnet add YourApp.csproj reference ../nevabridge-clients/samples/dotnet-wpf/src/NevaBridge.Sample.KeyStore/NevaBridge.Sample.KeyStore.csproj
dotnet build YourApp.csproj -c Release
```

The references bring in the controls and bundled page, the HTTP client implementing
`INevaBridgeConversations` (for the native chat) and `INevaBridgeWebChatApi` (for the web chat), and
the SQL Server settings provider. Keep the direct `NevaBridge.Chat`
reference if you replace the sample HTTP client or SQL provider with your own implementations.
NuGet restores their public
WebView2 and SQL client dependencies; no published NevaBridge package is needed.

Keep this checkout in your build inputs and pin it to a tested revision. Record that revision with
`git -C ../nevabridge-clients rev-parse HEAD`. Preserve the directory layout because the project
references and page-copy step use relative paths. If you prefer copying source, copy this layout
intact rather than individual control files without their dependencies.

### Store the settings in SQL Server

Create a production API key and find your product id in NevaBridge under Setup > API. Sandbox keys
and product ids belong to sandbox, so use a production key and product id for production.

1. Run the [setup script](../../samples/dotnet-wpf/sql/create-nevabridge-settings.sql) once in your
   application's database:

   ```powershell
   sqlcmd -S <server> -d <database> -i ../nevabridge-clients/samples/dotnet-wpf/sql/create-nevabridge-settings.sql
   ```

2. Insert the settings and grant the application's database user the reader role. Replace the
   placeholders with your values; keep the key out of source control and build logs:

   ```sql
   INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey, WebChatUrl)
   VALUES (1, 'https://tenant.api.nevabridge.com', '<product id>', '<API key>', NULL);
   ALTER ROLE NevaBridgeReader ADD MEMBER [<application database user>];
   ```

3. Read settings with your application's existing connection string:

   ```csharp
   using NevaBridge.Chat;
   using NevaBridge.Chat.Settings;
   using NevaBridge.Sample.Client;
   using NevaBridge.Sample.KeyStore;
   using System.Net.Http;

   INevaBridgeSettingsProvider provider = new SqlServerSettingsProvider(connectionString);
   NevaBridgeSettings settings = await provider.GetSettingsAsync();
   NevaBridgeClient nevaBridge = new NevaBridgeClient(httpClient, settings);
   ```

Keep one `HttpClient` for the application's lifetime, with a timeout of at least 60 seconds for
assistant replies. The reader role executes `dbo.GetNevaBridgeSettings` without direct access to
the table. For another database, implement `INevaBridgeSettingsProvider` to return the same settings.

For sandbox, use `https://tenant.api.sandbox.nevabridge.com` with a sandbox key and product id.
Remote API and page addresses must use HTTPS; plain HTTP is accepted only on loopback for testing.

### Map the signed-in user

Use your application's identity, with a stable ASCII reporter id:

```csharp
NevaBridgeReporter reporter = new(Id: currentUser.Id, DisplayName: currentUser.Name);
IReadOnlyDictionary<string, string> applicationContext = new Dictionary<string, string>
{
    ["application"] = "Your application",
    ["version"] = applicationVersion,
    ["screen"] = currentScreen,
};
```

The context is stored with the report. Optional roles (`anonymous`, `customer`, `tenant`) select
which knowledge audiences the assistant may use.

## 1. Native WPF chat

Use this when you want the chat to follow your application's appearance and release schedule.
Leave `WebChatUrl` as `NULL` and complete the common setup above. For a new integration, go
straight to the [native Generic Intake chat](#native-generic-intake-chat); the steps directly
below add the classic chat.

1. Add the control to your window:

   ```xml
   <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           xmlns:nevabridge="clr-namespace:NevaBridge.Wpf;assembly=NevaBridge.Wpf">
       <nevabridge:NevaBridgeChatControl x:Name="Chat" />
   </Window>
   ```

2. After reading settings and mapping the reporter, set the control's data context:

   ```csharp
   Chat.DataContext = new ChatViewModel(
       nevaBridge, settings.ProductId, reporter, applicationContext);
   ```

3. Build and run your app. Start a conversation, send a follow-up, and submit a report to a safe
   sandbox test product. Verify the reporter and application context before using production.

The native control provides report type selection, replies, a draft report and submission. It
offers no file attachments; the Generic Intake chat below and the web chat do.

### Native Generic Intake chat

With the tenant's `genericIntake` feature, the assistant works out what the reporter needs instead
of asking for a report type, and one conversation can file several reports. Each message starts a
turn that runs in the background; the chat shows what the assistant is doing and the reply as it
is written, then the conversation as saved. Finish ends the conversation and files everything it
noted; the chat then lists what was passed on to the team.

1. Add the control to your window:

   ```xml
   <nevabridge:NevaBridgeIntakeChatControl x:Name="IntakeChat" />
   ```

2. Read the tenant's features, then create the view model. Pass the control's file picker only when
   your application offers attachments and the tenant has `chatAttachments`; pass the reporter's
   contact details when your application knows them, so the assistant does not ask:

   ```csharp
   IReadOnlyDictionary<string, bool> features = await nevaBridge.GetFeaturesAsync();
   if (features.GetValueOrDefault("genericIntake"))
   {
       IntakeChatViewModel chat = new(nevaBridge, settings.ProductId, reporter, new IntakeChatOptions
       {
           ApplicationContext = applicationContext,
           ReporterContact = new ReporterContact { Email = currentUser.Email },
           PickFile = features.GetValueOrDefault("chatAttachments") ? IntakeChat.PickFileAsync : null,
       });
       IntakeChat.DataContext = chat;
       Closed += (_, _) => chat.Dispose();
   }
   ```

   `nevaBridge` implements `INevaBridgeIntakeConversations`; the sample's `NevaBridgeClient` does.
   It is separate from `INevaBridgeConversations`, so an implementation of that interface keeps
   compiling.

3. Attach a file before writing, then send a message, attach and remove another file, and press
   Finish in a sandbox test product. The chat lists the filed reports.

Files can be attached before the first message, as in the NevaBridge web app. The first file starts
the conversation without a message (`StartHeldIntakeConversationAsync`) and uploads at once to the
address NevaBridge returns (`UploadAttachmentAsync`); the first message then waits for uploads still
running, so the reply sees the files. A file that does not upload stays listed with Retry and
Dismiss and does not hold up the reply. Finish is offered once the first reply has arrived.

The chat recovers from a lost answer the way the web chat does: a turn already running is
followed, a message the conversation already saved is not sent again, and a reply that could not
be loaded offers Check again. One case cannot be recovered: when the answer to the very first
message's start is lost, the chat cannot find that conversation, and sending again starts a new
one. Its texts are English and live in `IntakeChatText`.

#### Dictation and the hands-free conversation

The Generic Intake chat can take spoken input, as the NevaBridge web app does:

- **Dictate** types what the reporter says into the message box, after anything already typed.
  Words still being recognized are shown apart until they settle; Send waits until dictation stops.
- **Talk** replaces the composer with a voice panel. The chat sends what the reporter said after a
  short pause, reads the reply aloud sentence by sentence while it is written, and listens again.
  The microphone is closed while the reply is on its way and while it is read. Send now, Stop
  reading and End voice mode (or Escape) control it; words heard but not sent go into the message
  box when voice mode ends.
- A small menu chooses English or Deutsch for recognition and speech.

Each needs its own tenant feature: `voiceDictation` for Dictate, `voiceConversation` for Talk.
Pass an `IntakeChatVoice` only when you offer at least one, and set each switch only when the tenant
has its feature:

```csharp
bool dictation = features.GetValueOrDefault("voiceDictation");
bool handsFree = features.GetValueOrDefault("voiceConversation");
IntakeChatOptions options = new()
{
    // ...
    Voice = dictation || handsFree
        ? new IntakeChatVoice(nevaBridge, new NAudioMicrophone(), new NAudioSpeechPlayer())
        {
            Dictation = dictation,
            HandsFree = handsFree,
        }
        : null,
    VoiceLanguage = VoiceLanguage.German, // optional; otherwise the Windows display language decides
};
```

`nevaBridge` implements `INevaBridgeVoice` (`CreateTranscriptionSessionAsync` and
`SynthesizeSpeechAsync`); the sample's `NevaBridgeClient` does. The chat streams the microphone
straight to Amazon Transcribe over the presigned address NevaBridge returns. That address is a
credential for the session, so never log it; `TranscriptionSession.ToString()` leaves it out.
Each recognition session is charged for 60 seconds up front, and the chat asks for the next one
shortly before it runs out only while it listens. Audio runs through
[NAudio](https://github.com/naudio/NAudio) (MIT): the microphone through WASAPI, the speech as
MP3. When Windows privacy settings deny the microphone, the chat says so and keeps offering voice.
A 403 from a voice call switches voice off for the chat; a 402 ends voice with NevaBridge's
message.

## 2. NevaBridge-hosted web chat

Use this when you want UI updates from NevaBridge deployments without redistributing the desktop
app. The bridge supports the hosted page's string request ids and the bundled prototype's numeric ids.

1. Complete the common setup and store the production page address alongside the key:

   ```sql
   UPDATE dbo.NevaBridgeSettings
   SET WebChatUrl = 'https://chat.app.nevabridge.com/v1/'
   WHERE Id = 1;
   ```

2. Add the WebView2 control to your window:

   ```xml
   <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           xmlns:nevabridge="clr-namespace:NevaBridge.Wpf;assembly=NevaBridge.Wpf">
       <nevabridge:NevaBridgeWebChatControl x:Name="WebChat" />
   </Window>
   ```

3. Show the window before initializing WebView2:

   ```csharp
   window.Show();
   await WebChat.InitializeAsync(nevaBridge, settings, reporter, applicationContext);
   ```

4. Test with `https://chat.app.sandbox.nevabridge.com/v1/` and sandbox API settings first. Verify
   a reply, follow-up, enabled attachment upload, and submission before switching to production.

`/v1/` receives UI updates within protocol major 1. It does not freeze the UI or remove the need to
test updates. `/latest/` follows the newest protocol major and can require a new host bridge; use it
only if you also coordinate those host updates. Reopen the chat to load new page files or reread a
changed database URL.

The bridge speaks version 1 of the webview chat bridge protocol, pinned in this repository under
`contracts/webview-chat-protocol/`, and passes every conformance case there. That includes the Generic
Intake requests: when your tenant has Generic Intake, the hosted page runs a Generic Intake
conversation through the bridge, polling each turn. It also answers `deleteAttachment`, so the
page can remove an attached file. The bundled copy runs the classic chat.

## 3. Bundle or host your own copy

Use this when you want to review and control changes to the UI inside your desktop app. The
HTML, JavaScript, CSS and image files are under [widgets/webview-chat](../webview-chat/README.md).
This revision contains the prototype page. Copy all its files from the same tested revision;
it needs no JavaScript build step.

Complete the common setup and add and initialize `NevaBridgeWebChatControl` as in option 2, then
choose where the page lives:

### Bundle it with your application

```sql
UPDATE dbo.NevaBridgeSettings SET WebChatUrl = NULL WHERE Id = 1;
```

The WPF project copies the page into the build output's `webview-chat` folder. Include that entire
folder when packaging your app. The control serves the page locally to WebView2; there is no page
URL to store. API requests still need a network connection.

### Serve it from your own web server

1. Copy `index.html`, `chat.js`, `chat.css` and `nevabridge-mark.svg` from `widgets/webview-chat`
   to a dedicated directory on your HTTPS web server. Preserve relative paths and serve JavaScript
   and CSS with their normal MIME types. No API key belongs in these files or on that server.
2. Store the full page address:

   ```sql
   UPDATE dbo.NevaBridgeSettings
   SET WebChatUrl = 'https://support.example.com/nevabridge/index.html'
   WHERE Id = 1;
   ```

3. Test a conversation and attachment upload inside your app. A normal browser does not supply the
   native bridge the page needs.

NevaBridge deployments do not replace these UI files. You decide when to update them, keep the
page and bridge compatible, and apply relevant security fixes yourself. This fixes the UI version;
it does not isolate the app from API changes, service outages, or host and WebView2 vulnerabilities.

## Features, errors and API key access

Web chat attachments require `chatAttachments`. The host reads the tenant's features, shows the
Windows file picker, and uploads the file. Set `AllowAttachments = false` to disable the picker.
The page receives neither the file bytes nor the API key. The native Generic Intake chat offers
attachments when it gets a file picker (see above).

Conversation errors appear inside the chat. Missing settings raise `NevaBridgeSettingsException`;
show its message when opening the chat fails. The sample's `ChatErrors.cs` demonstrates this.

A direct API integration makes the tenant-wide key accessible to the desktop process, including
when it is stored in SQL Server or Credential Manager. Anyone who obtains it can read the tenant's
conversations and act as any reporter. If users must not have that authority, keep the key in a
backend service and implement `INevaBridgeConversations`, `INevaBridgeIntakeConversations`,
`INevaBridgeVoice` and `INevaBridgeWebChatApi` to call that service instead. `INevaBridgeWebChatApi` returns the API's JSON for each operation unchanged, because
the bridge passes it to the page as it is.

See the [sample guide](../../samples/dotnet-wpf/README.md) for environment-variable and Credential
Manager demo setup and the reference files that connect the controls to an application.
