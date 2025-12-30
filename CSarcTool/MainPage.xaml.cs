using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CSarcTool.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.UI.Popups;

namespace CSarcTool;

public sealed partial class MainPage : Page
{
    private List<string> _batchFiles = new List<string>();

    public MainPage()
    {
        this.InitializeComponent();
    }

    // --- Helpers ---

    private void SetStatus(string msg, bool loading = false)
    {
        StatusTextBlock.Text = msg;
        StatusProgressBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ShowDialog(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // --- Compress Section ---

    private async void PickCompressFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        // WinUI3/Desktop workaround for Window Handle if needed (Not included for pure Uno/UWP compat)
        // InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Window));

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            CompressInputPath.Text = folder.Path;
            // Auto generate output path if empty
            if (string.IsNullOrEmpty(CompressOutputPath.Text))
            {
                CompressOutputPath.Text = folder.Path + ".szs";
            }
        }
    }

    private async void PickCompressOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeChoices.Add("Yaz0 Compressed SARC", new List<string>() { ".szs" });
        picker.FileTypeChoices.Add("SARC Archive", new List<string>() { ".sarc", ".pack", ".arc" });
        picker.SuggestedFileName = "Archive";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            CompressOutputPath.Text = file.Path;
        }
    }

    private async void CompressAction_Click(object sender, RoutedEventArgs e)
    {
        string inputPath = CompressInputPath.Text;
        string outputPath = CompressOutputPath.Text;
        bool disableYaz0 = CompressNoYaz0Check.IsChecked ?? false;
        int level = (int)CompressLevelSlider.Value;

        if (string.IsNullOrEmpty(inputPath) || !Directory.Exists(inputPath))
        {
            await ShowDialog("Error", "Please select a valid input folder.");
            return;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = inputPath + (disableYaz0 ? ".sarc" : ".szs");
        }

        SetStatus("Compressing...", true);

        try
        {
            await Task.Run(() =>
            {
                var arc = new SarcArchive { Endian = Endianness.Big }; // Default to Big Endian (Wii U/common)

                string[] files = Directory.GetFiles(inputPath, "*", SearchOption.AllDirectories);

                foreach (string fsPath in files)
                {
                    string relPath = Path.GetRelativePath(inputPath, fsPath).Replace("\\", "/");

                    byte[] data = File.ReadAllBytes(fsPath);
                    bool hasFilename = !Path.GetFileName(relPath).StartsWith("hash_");

                    // Add to archive tree
                    string[] parts = relPath.Split('/');
                    SarcFolder current = arc.Root;

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var next = current.GetFolder(parts[i]);
                        if (next == null) { next = new SarcFolder(parts[i]); current.Add(next); }
                        current = next;
                    }
                    current.Add(new SarcFile(parts.Last(), data, hasFilename));
                }

                var result = arc.Save();
                byte[] finalData = result.data;

                if (!disableYaz0)
                {
                    finalData = Yaz0.Compress(finalData, level);
                }

                File.WriteAllBytes(outputPath, finalData);
            });

            SetStatus("Compression Complete!");
            await ShowDialog("Success", $"Archive saved to:\n{outputPath}");
        }
        catch (Exception ex)
        {
            SetStatus("Error");
            await ShowDialog("Error", ex.Message);
        }
        finally
        {
            SetStatus("", false);
        }
    }

    // --- Extract Section ---

    private async void PickExtractFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".szs");
        picker.FileTypeFilter.Add(".sarc");
        picker.FileTypeFilter.Add(".pack");
        picker.FileTypeFilter.Add(".arc");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ExtractInputPath.Text = file.Path;
        }
    }

    private async void PickExtractOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            ExtractOutputPath.Text = folder.Path;
        }
    }

    private async void ExtractAction_Click(object sender, RoutedEventArgs e)
    {
        string inputFile = ExtractInputPath.Text;
        string outputRoot = ExtractOutputPath.Text;

        if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
        {
            await ShowDialog("Error", "Please select a valid archive file.");
            return;
        }

        if (string.IsNullOrEmpty(outputRoot))
        {
            outputRoot = Path.GetDirectoryName(inputFile);
        }

        SetStatus("Extracting...", true);

        try
        {
            await Task.Run(() => PerformExtraction(inputFile, outputRoot));
            SetStatus("Extraction Complete!");
            await ShowDialog("Success", $"Files extracted to:\n{outputRoot}");
        }
        catch (Exception ex)
        {
            SetStatus("Error");
            await ShowDialog("Error", ex.Message);
        }
        finally
        {
            SetStatus("", false);
        }
    }

    // --- Batch Section ---

    private async void BatchAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".szs");
        picker.FileTypeFilter.Add(".sarc");
        picker.FileTypeFilter.Add(".pack");
        picker.FileTypeFilter.Add("*");

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            BatchPlaceholderText.Visibility = Visibility.Collapsed;
            foreach (var file in files)
            {
                if (!_batchFiles.Contains(file.Path))
                {
                    _batchFiles.Add(file.Path);
                    var txt = new TextBlock { Text = file.Name, Margin = new Thickness(0, 2, 0, 2) };
                    BatchFileListPanel.Children.Add(txt);
                }
            }
        }
    }

    private async void PickBatchOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            BatchOutputPath.Text = folder.Path;
        }
    }

    private async void BatchAction_Click(object sender, RoutedEventArgs e)
    {
        if (_batchFiles.Count == 0)
        {
            await ShowDialog("Error", "No files selected for batch extraction.");
            return;
        }

        string outputRoot = BatchOutputPath.Text;
        bool useSubfolders = BatchSubfolderCheck.IsChecked ?? true;

        SetStatus("Batch Extracting...", true);
        int successCount = 0;
        int failCount = 0;

        await Task.Run(() =>
        {
            foreach (var file in _batchFiles)
            {
                try
                {
                    string targetFolder = outputRoot;
                    if (string.IsNullOrEmpty(targetFolder)) targetFolder = Path.GetDirectoryName(file);

                    // If user didn't check "use subfolders", extracting multiple SARCs to same root 
                    // might collide if they have same internal filenames.
                    // But PerformExtraction creates a folder named after the file anyway if it's a SARC.
                    // If useSubfolders is TRUE, we might want an EXTRA layer, or rely on PerformExtraction's behavior.

                    // Logic: PerformExtraction creates a folder named {FileName} inside {OutputRoot}.
                    // If we want to dump to root directly, we'd pass the folder inside.

                    // Current PerformExtraction behavior: Creates Folder {FileName} inside {OutputRoot}.
                    // This matches "Extract each archive to its own subfolder".

                    PerformExtraction(file, targetFolder);
                    successCount++;
                }
                catch
                {
                    failCount++;
                }
            }
        });

        SetStatus("", false);
        await ShowDialog("Batch Complete", $"Processed {_batchFiles.Count} files.\nSuccess: {successCount}\nFailed: {failCount}");
    }


    // --- Core Logic Bridge ---

    private void PerformExtraction(string inputFile, string outputRoot)
    {
        byte[] inb = File.ReadAllBytes(inputFile);

        // 1. Decompress if needed
        while (Yaz0.IsYazCompressed(inb))
        {
            inb = Yaz0.Decompress(inb);
        }

        string name = Path.GetFileNameWithoutExtension(inputFile);
        string ext = SarcArchive.GuessFileExtension(inb);

        // 2. Prepare Folder
        string folderName = name;
        string targetFolder = Path.Combine(outputRoot, folderName);

        if (ext == ".sarc" || (inb.Length >= 4 && System.Text.Encoding.ASCII.GetString(inb, 0, 4) == "SARC"))
        {
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            var arc = new SarcArchive();
            arc.Load(inb);

            var files = new List<(string path, byte[] data)>();

            void GetAbsPath(SarcFolder folder, string path)
            {
                foreach (var entry in folder.Contents)
                {
                    if (entry is SarcFile f)
                        files.Add((string.IsNullOrEmpty(path) ? f.Name : path + "/" + f.Name, f.Data));
                    else if (entry is SarcFolder sub)
                        GetAbsPath(sub, string.IsNullOrEmpty(path) ? sub.Name : path + "/" + sub.Name);
                }
            }

            foreach (var entry in arc.Root.Contents)
            {
                if (entry is SarcFile f) files.Add((f.Name, f.Data));
                else if (entry is SarcFolder sub) GetAbsPath(sub, sub.Name);
            }

            foreach (var (fpath, data) in files)
            {
                string fullPath = Path.Combine(targetFolder, fpath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(fullPath, data);
            }
        }
        else
        {
            // Just a decompressed file
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
            string outFile = Path.Combine(targetFolder, name + ext);
            File.WriteAllBytes(outFile, inb);
        }
    }
}
