# NevaBridge WPF example

A small WPF application that embeds the NevaBridge chat with the
[`NevaBridge.Wpf`](../../widgets/wpf) package. Run it to see the chat working against your NevaBridge
sandbox, then read the four files listed under [Where to look](#where-to-look) to do the same in your
own application.

The example is a stand-in business app ("Contoso Billing") with two buttons:

- **Support** opens the native WPF chat, styled like the application.
- **The NevaBridge mark** opens the NevaBridge web chat in WebView2.

For your own application, follow the [WPF integration guide](../../widgets/wpf/README.md). It
shows how to build the components into your app and use database settings for three options:
native WPF, NevaBridge-hosted web chat, and a bundled or self-hosted copy. No published NevaBridge
NuGet package is required. This guide keeps the quick demo paths using environment variables or
Windows Credential Manager.

## 1. Prerequisites

- Windows 10 or 11 with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or later).
- The [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) for the web chat. It
  ships with Windows 11.
- A NevaBridge **sandbox API key** and **product id**. Create a key in NevaBridge under Setup > API.

## 2. Where the API key goes

The example reads its settings from one of three sources, chosen with the environment variable
`NEVABRIDGE_SETTINGS_SOURCE`. Start with the first.

### Try it out: environment variables (default)

In PowerShell, in the folder of this README:

```powershell
$env:NEVABRIDGE_API_KEY = "<your sandbox API key>"
$env:NEVABRIDGE_PRODUCT_ID = "<your product id>"
```

The sandbox is used unless you also set `NEVABRIDGE_API_BASE_URL`. `NEVABRIDGE_WEBCHAT_URL` is optional;
without it the web chat page shipped in the package is used. When you are done trying, revoke the key
in NevaBridge.

### Recommended on Windows: Windows Credential Manager

1. Start the example (step 3) and open **File > NevaBridge settings**.
2. Enter the API key, the product id and the environment, and save. The settings are stored in Windows
   Credential Manager for your Windows user.
3. Close the example, then start it with this source:

   ```powershell
   $env:NEVABRIDGE_SETTINGS_SOURCE = "credential-manager"
   ```

**Remove saved settings** in the same window deletes them again.

### Settings in your SQL Server database

For applications that keep their configuration in SQL Server. The settings live in one row that the
application reads through a stored procedure; users get permission to run the procedure, not to read
the table.

1. Create the table, the reader role and the procedure:

   ```powershell
   sqlcmd -S <server> -d <database> -i sql\create-nevabridge-settings.sql
   ```

2. Store your settings and let the application's database user read them. Leave `WebChatUrl` `NULL` for
   the page shipped in the package:

   ```sql
   INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey, WebChatUrl)
   VALUES (1, 'https://tenant.api.sandbox.nevabridge.com', '<product id>', '<API key>', NULL);
   ALTER ROLE NevaBridgeReader ADD MEMBER [<the application's database user>];
   ```

3. Start the example with this source and your connection string:

   ```powershell
   $env:NEVABRIDGE_SETTINGS_SOURCE = "sql"
   $env:NEVABRIDGE_SAMPLE_SQL = "Server=<server>;Database=<database>;Integrated Security=true;TrustServerCertificate=true"
   ```

Change `WebChatUrl` in the row to move every user to another page address at once.

The provider reads the row when the chat opens, so reopen the chat after changing it. For
production settings and the page address for each integration option, see the
[integration guide](../../widgets/wpf/README.md#choose-an-integration).

## 3. Build and run

```powershell
dotnet run --project src\NevaBridge.Sample.DesktopApp
```

The Contoso Billing window opens. Click **Support** or the NevaBridge mark, describe a problem and send
it: the assistant answers with a follow-up question, and a draft report appears under "Report so far".
Choose the report type (bug report, feature request or support request) before the first message.

## Where to look

Everything you do in your own application is in one file per step, in
`src/NevaBridge.Sample.DesktopApp`:

| In your application you...           | Example file                                                                   |
| ------------------------------------ | ------------------------------------------------------------------------------ |
| Provide the NevaBridge settings      | `NevaBridgeSettingsSource.cs`                                                  |
| Tell NevaBridge who is reporting     | `CurrentReporter.cs`                                                           |
| Open the native chat or the web chat | `NevaBridgeIntegration.cs`, used by `SupportWindow` and `NevaBridgeChatWindow` |
| Explain why the chat cannot open     | `ChatErrors.cs`                                                                |

Two more pieces you can copy:

- `src/NevaBridge.Sample.Client/NevaBridgeClient.cs`: calls the NevaBridge API. It stands in for the
  NevaBridge .NET SDK until that is published.
- `src/NevaBridge.Sample.KeyStore/SqlServerSettingsProvider.cs` with `sql/create-nevabridge-settings.sql`:
  the SQL Server settings source.

The [package guide](../../widgets/wpf/README.md) explains the controls, the page sources, feature gates
and the security model.

## Troubleshooting

| Symptom                                                          | Cause                                                                            |
| ---------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| "The environment variable NEVABRIDGE_API_KEY is not set."        | Set the variables in the same PowerShell window you run the example from.        |
| "... must use https ..."                                         | An address in the settings uses plain `http`. Only `localhost` may.              |
| "No NevaBridge settings are saved in Windows Credential Manager" | Save them with File > NevaBridge settings first.                                 |
| The web chat says the WebView2 Runtime is not installed          | Install the WebView2 Runtime (see prerequisites).                                |
| The chat says NevaBridge refused the request (401 or 403)        | The key was revoked, belongs to another environment, or the product id is wrong. |
| No "Attach file" button                                          | Attachments are not enabled for your organization; ask NevaBridge.               |

## Building and testing on Linux or macOS

The WPF projects compile anywhere (`EnableWindowsTargeting` is set) but run only on Windows. The tests
run anywhere with Docker (the SQL Server tests start a container):

```bash
dotnet test NevaBridge.WpfSample.sln
```

To produce a Windows build: `dotnet publish src/NevaBridge.Sample.DesktopApp -c Release -r win-x64 --self-contained false -o out`.
