using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using NevaBridge.Chat.Settings;

namespace NevaBridge.Wpf;

/// <summary>
/// Keeps the NevaBridge settings in Windows Credential Manager, protected by the signed-in Windows
/// user's credentials (DPAPI). All four settings are stored together as one JSON value, so the web
/// chat address travels with the key.
/// </summary>
/// <remarks>
/// The entry belongs to the Windows user, not the machine: every user of the PC saves their own.
/// Anyone signed in as that user, and any program they run, can read it; that includes the
/// application itself, which is how the chat gets the key.
/// </remarks>
public sealed class WindowsCredentialSettingsStore : INevaBridgeSettingsProvider
{
    /// <summary>The Credential Manager entry name used unless another is given.</summary>
    public const string DefaultTargetName = "NevaBridge";

    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    // CRED_MAX_CREDENTIAL_BLOB_SIZE: 5 * 512 bytes.
    private const int MaxBlobBytes = 2560;

    private readonly string _targetName;

    public WindowsCredentialSettingsStore(string targetName = DefaultTargetName)
    {
        _targetName = targetName;
    }

    /// <summary>Whether settings are saved for the current Windows user.</summary>
    public bool Exists()
    {
        if (CredRead(_targetName, CredentialTypeGeneric, 0, out IntPtr credential))
        {
            CredFree(credential);
            return true;
        }

        return false;
    }

    public ValueTask<NevaBridgeSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CredRead(_targetName, CredentialTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            throw error == ErrorNotFound
                ? new NevaBridgeSettingsException(
                    $"No NevaBridge settings are saved in Windows Credential Manager under '{_targetName}'.")
                : new NevaBridgeSettingsException(
                    $"Windows Credential Manager could not read '{_targetName}'.", new Win32Exception(error));
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            string json = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return ValueTask.FromResult(NevaBridgeSettingsJson.Deserialize(json));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>Saves the settings for the current Windows user, replacing any saved before.</summary>
    public void Save(NevaBridgeSettings settings)
    {
        byte[] blob = Encoding.Unicode.GetBytes(NevaBridgeSettingsJson.Serialize(settings));
        if (blob.Length > MaxBlobBytes)
        {
            throw new NevaBridgeSettingsException("The NevaBridge settings are too long for Windows Credential Manager.");
        }

        IntPtr blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            NativeCredential credential = new()
            {
                Type = CredentialTypeGeneric,
                TargetName = _targetName,
                Comment = "NevaBridge chat settings",
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = PersistLocalMachine,
                UserName = "NevaBridge",
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new NevaBridgeSettingsException(
                    $"Windows Credential Manager could not save '{_targetName}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPointer);
        }
    }

    /// <summary>Removes the saved settings, for example after revoking the key.</summary>
    public void Delete()
    {
        if (!CredDelete(_targetName, CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new NevaBridgeSettingsException(
                $"Windows Credential Manager could not delete '{_targetName}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
