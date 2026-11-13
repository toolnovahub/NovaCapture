using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using NAudio.Wave;

namespace NovaCapture;
public partial class MainWindow : Window
{
    private string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NovaCapture");
    private Process? _recorder;
    private string? _videoPath;
    private bool _stopping;
    private WasapiLoopbackCapture? _systemCapture;
    private WaveInEvent? _micCapture;
    private WaveFileWriter? _systemWriter;
    private WaveFileWriter? _micWriter;
    private string? _systemWav;
    private string? _micWav;
    private string? _tempAudioDir;
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCapture", "last-folder.txt");

    public MainWindow()
    {
        InitializeComponent();
        try { if (File.Exists(SettingsPath) && Directory.Exists(File.ReadAllText(SettingsPath).Trim())) _folder = File.ReadAllText(SettingsPath).Trim(); } catch { }
        FolderBox.Text = _folder;
        Closed += (_, _) => { try { if (_recorder is { HasExited: false }) _recorder.Kill(entireProcessTree: true); } catch { } };
    }

    private void RememberFolder(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        _folder = dir;
        FolderBox.Text = _folder;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, _folder); } catch { }
    }

    private string? ChooseSavePath(string extension, string filter, string prefix)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = extension == ".png" ? "Salvează captura de ecran" : "Salvează înregistrarea video",
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            InitialDirectory = Directory.Exists(_folder) ? _folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            FileName = $"{prefix}_{Stamp()}{extension}",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return null;
        RememberFolder(dialog.FileName);
        return dialog.FileName;
    }

    private void SetStatus(string value) => StatusText.Text = value;
    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");

    private async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ChooseSavePath(".png", "PNG image (*.png)|*.png", "Screenshot");
            if (path is null) { SetStatus("Captura anulată."); return; }
            // Temporarily hide our window so it does not appear in the capture.
            Hide();
            await Task.Delay(300);
            var bounds = Forms.SystemInformation.VirtualScreen;
            using (var bmp = new Bitmap(bounds.Width, bounds.Height))
            using (var graphics = Graphics.FromImage(bmp))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                bmp.Save(path, ImageFormat.Png);
            }
            Show();
            Activate();
            SetStatus($"Screenshot salvat: {path}");
        }
        catch (Exception ex) { Show(); SetStatus($"Eroare screenshot: {ex.Message}"); }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "Alege folderul pentru capturi" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) { _folder = dialog.SelectedPath; FolderBox.Text = _folder; try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, _folder); } catch { } }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_folder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus($"Nu pot deschide folderul: {ex.Message}"); }
    }

    private static string? FindFFmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Select(p => Path.Combine(p.Trim().Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
    }


    private void StartAudio()
    {
        var system = SystemAudioCheck.IsChecked == true;
        var mic = MicAudioCheck.IsChecked == true;
        if (!system && !mic) return;
        _tempAudioDir = Path.Combine(Path.GetTempPath(), "NovaCapture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAudioDir);
        try
        {
            if (system)
            {
                _systemWav = Path.Combine(_tempAudioDir, "system.wav");
                _systemCapture = new WasapiLoopbackCapture();
                _systemWriter = new WaveFileWriter(_systemWav, _systemCapture.WaveFormat);
                _systemCapture.DataAvailable += (_, a) => _systemWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                _systemCapture.StartRecording();
            }
            if (mic)
            {
                if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("Nu a fost găsit niciun microfon. Debifează Microfon pentru a continua.");
                _micWav = Path.Combine(_tempAudioDir, "mic.wav");
                _micCapture = new WaveInEvent { WaveFormat = new WaveFormat(48000, 16, 1), BufferMilliseconds = 100 };
                _micWriter = new WaveFileWriter(_micWav, _micCapture.WaveFormat);
                _micCapture.DataAvailable += (_, a) => _micWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                _micCapture.StartRecording();
            }
        }
        catch { StopAudio(); throw; }
    }

    private void StopAudio()
    {
        try { _systemCapture?.StopRecording(); } catch { }
        try { _micCapture?.StopRecording(); } catch { }
        System.Threading.Thread.Sleep(250);
        _systemCapture?.Dispose(); _systemCapture = null;
        _micCapture?.Dispose(); _micCapture = null;
        _systemWriter?.Dispose(); _systemWriter = null;
        _micWriter?.Dispose(); _micWriter = null;
    }

    private void CleanAudio()
    {
        if (_tempAudioDir is not null)
        {
            try { Directory.Delete(_tempAudioDir, true); } catch { }
        }
        _tempAudioDir = null;
        _systemWav = null;
        _micWav = null;
    }

    private async Task<string> MergeAudioAsync(string ffmpeg, string videoPath)
    {
        var tracks = new[] { _systemWav, _micWav }
            .Where(p => p is not null && File.Exists(p) && new FileInfo(p).Length > 44)
            .ToArray();
        if (tracks.Length == 0) return "Video salvat fără audio (nu s-au capturat date audio).";
        var mergedPath = Path.Combine(Path.GetDirectoryName(videoPath)!, "NovaCapture_mux_" + Guid.NewGuid().ToString("N") + ".mp4");
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel"); start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-i"); start.ArgumentList.Add(videoPath);
        foreach (var track in tracks) { start.ArgumentList.Add("-i"); start.ArgumentList.Add(track!); }
        if (tracks.Length == 2)
        {
            start.ArgumentList.Add("-filter_complex");
            start.ArgumentList.Add("[1:a][2:a]amix=inputs=2:duration=longest:dropout_transition=0[a]");
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("0:v:0");
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("[a]");
        }
        else
        {
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("0:v:0");
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("1:a:0");
        }
        start.ArgumentList.Add("-c:v"); start.ArgumentList.Add("copy");
        start.ArgumentList.Add("-c:a"); start.ArgumentList.Add("aac");
        start.ArgumentList.Add("-b:a"); start.ArgumentList.Add("192k");
        start.ArgumentList.Add("-shortest");
        start.ArgumentList.Add(mergedPath);
        using var process = Process.Start(start) ?? throw new Exception("FFmpeg nu a pornit pentru audio.");
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(mergedPath))
        {
            try { File.Delete(mergedPath); } catch { }
            throw new Exception("Audio nu a putut fi adăugat: " + error);
        }
        File.Move(mergedPath, videoPath, true);
        return "Video și audio salvate.";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ffmpeg = FindFFmpeg();
            if (ffmpeg is null) { SetStatus("FFmpeg nu a fost găsit. Pune ffmpeg.exe lângă NovaCapture.exe sau în PATH."); return; }
            var selectedPath = ChooseSavePath(".mp4", "MP4 video (*.mp4)|*.mp4", "Recording");
            if (selectedPath is null) { SetStatus("Înregistrarea anulată."); return; }
            _videoPath = selectedPath;
            var screen = Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Monitorul principal nu a fost detectat.");
            // gdigrab records the primary display area; desktop coordinates are explicit for multi-monitor setups.
            var ultra = QualityCombo.SelectedIndex == 1;
            var fps = ultra ? 60 : 30;
            var crf = ultra ? 15 : 23;
            var preset = ultra ? "veryfast" : "ultrafast";
            var args = $"-hide_banner -loglevel error -f gdigrab -framerate {fps} -offset_x {screen.Left} -offset_y {screen.Top} -video_size {screen.Width}x{screen.Height} -i desktop -c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p -movflags +faststart \"{_videoPath}\"";
            _recorder = new Process { StartInfo = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true } };
            _recorder.Start();
            StartAudio();
            _stopping = false;
            StartButton.IsEnabled = false;
            QualityCombo.IsEnabled = false;
            SystemAudioCheck.IsEnabled = false;
            MicAudioCheck.IsEnabled = false;
            StopButton.IsEnabled = true;
            SetStatus($"Înregistrare pornită: {fps} FPS, CRF {crf}, rezoluție nativă. Apasă Stop pentru salvare MP4.");
            await Task.Delay(650);
            if (_recorder.HasExited && !_stopping)
            {
                var error = await _recorder.StandardError.ReadToEndAsync();
                SetStatus($"FFmpeg nu a pornit corect: {error}");
                ResetRecording();
            }
        }
        catch (Exception ex) { SetStatus($"Eroare recorder: {ex.Message}"); ResetRecording(); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder is null || _stopping) return;
        _stopping = true;
        StopButton.IsEnabled = false;
        SetStatus("Finalizez fișierul MP4...");
        try
        {
            if (!_recorder.HasExited)
            {
                await _recorder.StandardInput.WriteLineAsync("q");
                await _recorder.StandardInput.FlushAsync();
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await _recorder.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { _recorder.Kill(entireProcessTree: true); await _recorder.WaitForExitAsync(); throw new Exception("FFmpeg nu s-a oprit la timp; fișierul poate fi incomplet."); }
            }
            StopAudio();
            var error = await _recorder.StandardError.ReadToEndAsync();
            if (_recorder.ExitCode != 0) SetStatus($"Înregistrarea a eșuat: {error}");
            else if (_videoPath is not null && File.Exists(_videoPath))
            {
                SetStatus("Adaug sunetul în MP4...");
                var ffmpeg = FindFFmpeg() ?? throw new Exception("FFmpeg lipsește.");
                var result = await MergeAudioAsync(ffmpeg, _videoPath);
                SetStatus($"{result} {_videoPath}");
            }
            else SetStatus("FFmpeg s-a închis, dar fișierul MP4 nu a fost găsit.");
        }
        catch (Exception ex) { SetStatus($"Eroare la oprire: {ex.Message}"); }
        finally { ResetRecording(); }
    }

    private void ResetRecording()
    {
        StopAudio();
        CleanAudio();
        _recorder?.Dispose();
        _recorder = null;
        _stopping = false;
        StartButton.IsEnabled = true;
        QualityCombo.IsEnabled = true;
        SystemAudioCheck.IsEnabled = true;
        MicAudioCheck.IsEnabled = true;
        StopButton.IsEnabled = false;
    }
}