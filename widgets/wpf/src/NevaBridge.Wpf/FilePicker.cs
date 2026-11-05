using System.IO;
using System.Windows;
using NevaBridge.Chat;

namespace NevaBridge.Wpf;

/// <summary>The operating system's file dialog, limited to the kinds of files NevaBridge accepts.</summary>
internal static class FilePicker
{
    public static PickedFile? Pick(Window? owner)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Attach a file",
            Filter = "Screenshots, videos and logs|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.txt;*.log;*.md;*.csv;*.json;*.xml;*.yaml;*.yml",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return null;
        }

        FileInfo file = new(dialog.FileName);
        return new PickedFile(file.Name, file.Length, file.OpenRead);
    }
}
