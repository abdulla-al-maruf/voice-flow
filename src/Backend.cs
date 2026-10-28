using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class Backend : IDisposable
{
    readonly object gate = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 24 };
    Process process;
    bool disposed;
    internal event Action<Dictionary<string, object>> Message;
    internal event Action Stopped;
    internal bool Alive { get { try { return process != null && !process.HasExited; } catch { return false; } } }
    internal void Start()
    {
        if (Alive) return;
        string node = Path.Combine(Program.Root,"runtime","node.exe");
        if(!File.Exists(node))node=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"nodejs","node.exe");
        if (!File.Exists(node) || !Path.GetFileName(node).Equals("node.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Node runtime is unavailable.");
        process = new Process { StartInfo = new ProcessStartInfo(node, "\"" + Path.Combine(Program.Root, "bridge", "bridge.cjs") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Program.Root, RedirectStandardInput = true,
          RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Program.Utf8, StandardErrorEncoding = Program.Utf8 }, EnableRaisingEvents = true };
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
        {
            if (disposed || string.IsNullOrEmpty(args.Data)) return;
            try
            {
                var value = json.Deserialize<Dictionary<string, object>>(args.Data);
                if (value != null && Message != null) Message(value);
            }
            catch { if (Stopped != null) Stopped(); }
        };
        // Drain stderr without writing cloud errors or authentication data to disk.
        process.ErrorDataReceived += delegate { };
        process.Exited += delegate { if (!disposed && Stopped != null) Stopped(); };
        if (!process.Start()) throw new IOException("Voice helper did not start.");
        process.StandardInput.NewLine = "\n";
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
    }
    internal void Send(object command)
    {
        lock (gate)
        {
            if (!Alive || disposed) throw new IOException("Voice helper stopped.");
            process.StandardInput.WriteLine(json.Serialize(command)); process.StandardInput.Flush();
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (process == null) return;
        try { process.StandardInput.Close(); if (!process.WaitForExit(1200)) process.Kill(); } catch { }
        process.Dispose();
    }
}
