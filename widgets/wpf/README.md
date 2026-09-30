# Integrate NevaBridge into a WPF application

Use native WPF controls or show the web chat inside WebView2. Your application reads the API key
and product id from its database and makes the API calls. The web page never receives the key.
Build the components from this repository; a published NuGet package is not required.

To try the demo without a database, follow the [WPF sample guide](../../samples/dotnet-wpf/README.md).

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
`INevaBridgeConversations`, and the SQL Server settings provider. Keep the direct `NevaBridge.Chat`
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
   INevaBridgeConversations conversations = new NevaBridgeClient(httpClient, settings);
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
Leave `WebChatUrl` as `NULL` and complete the common setup above.

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
       conversations, settings.ProductId, reporter, applicationContext);
   ```

3. Build and run your app. Start a conversation, send a follow-up, and submit a report to a safe
   sandbox test product. Verify the reporter and application context before using production.

The native control provides report type selection, replies, a draft report and submission. The
file attachment picker is provided by the WebView2 variant.

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
   await WebChat.InitializeAsync(conversations, settings, reporter, applicationContext);
   ```

4. Test with `https://chat.app.sandbox.nevabridge.com/v1/` and sandbox API settings first. Verify
   a reply, follow-up, enabled attachment upload, and submission before switching to production.

`/v1/` receives UI updates within protocol major 1. It does not freeze the UI or remove the need to
test updates. `/latest/` follows the newest protocol major and can require a new host bridge; use it
only if you also coordinate those host updates. Reopen the chat to load new page files or reread a
changed database URL.

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
The page receives neither the file bytes nor the API key.

Conversation errors appear inside the chat. Missing settings raise `NevaBridgeSettingsException`;
show its message when opening the chat fails. The sample's `ChatErrors.cs` demonstrates this.

A direct API integration makes the tenant-wide key accessible to the desktop process, including
when it is stored in SQL Server or Credential Manager. Anyone who obtains it can read the tenant's
conversations and act as any reporter. If users must not have that authority, keep the key in a
backend service and implement `INevaBridgeConversations` to call that service instead.

See the [sample guide](../../samples/dotnet-wpf/README.md) for environment-variable and Credential
Manager demo setup and the reference files that connect the controls to an application.
