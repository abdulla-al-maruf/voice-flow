using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Capture begins at the hotkey, before the cloud connects. The queued beginning is sent when the CLI arrives.
internal sealed class AudioPipe : IDisposable
{
    readonly object gate = new object();
    readonly Queue<byte[]> queued = new Queue<byte[]>();
    readonly MemoryStream captured = new MemoryStream();
    readonly TcpListener listener;
    WaveInput microphone;
    readonly AutoResetEvent changed = new AutoResetEvent(false);
    TcpClient client;
    Task sender;
    volatile bool disposed, intentionalClose;
    long sent;
    Exception error;
    int generation;
    long lastSound;
    volatile float level;
    // Loudness of the latest microphone chunk, 0 (silence) to 1 (loud speech), for the listening wave.
    internal float Level { get { return level; } }
    internal string Address { get { return "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; } }
    internal long CapturedBytes { get { lock (gate) return captured.Length; } }
    internal long SentBytes { get { lock (gate) return sent; } }
    internal bool HasSound { get { lock (gate) return lastSound != 0; } }
    internal Exception Error { get { lock (gate) return error ?? (microphone == null ? null : microphone.Error); } }

    internal AudioPipe(bool useMicrophone)
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        sender = Task.Run((Action)SendLoop);
        if (useMicrophone) StartCapture(true);
    }

    internal void StartCapture(bool useMicrophone)
    {
        EndSession();
        lock (gate) { queued.Clear(); captured.SetLength(0); sent = 0; error = null; intentionalClose = false; lastSound = 0; level = 0; }
        if (useMicrophone) microphone = new WaveInput(Append);
    }

    internal void Append(byte[] bytes)
    {
        lock (gate)
        {
            if (disposed) return;
            captured.Write(bytes, 0, bytes.Length);
            queued.Enqueue(bytes);
            long energy = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2) { int sample = (short)(bytes[i] | bytes[i + 1] << 8); energy += (long)sample * sample; }
            if (bytes.Length > 1 && energy / (bytes.Length / 2) > 14400) lastSound = Stopwatch.GetTimestamp();
            if (bytes.Length > 1)
            {
                // sqrt curve lifts normal speech (RMS ~0.02-0.2) into a visible range; the 0.12 floor hides room noise.
                double rms = Math.Sqrt((double)energy / (bytes.Length / 2)) / 32768;
                level = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(rms) * 2.4 - 0.12) / 0.88));
            }
        }
        changed.Set();
    }

    void SendLoop()
    {
        try
        {
            while (!disposed)
            {
                TcpClient connection = listener.AcceptTcpClient();
                int current;
                lock (gate) { client = connection; current = generation; }
                connection.NoDelay = true;
                connection.SendTimeout = 5000;
                try
                {
                    using (NetworkStream stream = connection.GetStream())
                    {
                        while (!disposed)
                        {
                            byte[] bytes = null;
                            lock (gate) { if (current != generation) break; if (queued.Count > 0) bytes = queued.Dequeue(); }
                            if (bytes == null)
                            {
                                if (connection.Client.Poll(0, SelectMode.SelectRead) && connection.Available == 0)
                                    throw new IOException("The microphone stream closed.");
                                changed.WaitOne(30); continue;
                            }
                            stream.Write(bytes, 0, bytes.Length);
                            lock (gate) { if (current == generation) sent += bytes.Length; }
                        }
                    }
                }
                catch (Exception ex) { lock (gate) { if (!disposed && !intentionalClose && current == generation) error = ex; } }
                finally { connection.Close(); lock (gate) { if (client == connection) client = null; } }
            }
        }
        catch (Exception ex) { if (!disposed && !intentionalClose) lock (gate) error = ex; }
    }

    internal async Task Drain()
    {
        if (microphone != null) microphone.Stop();
        if (Error != null) throw new IOException("Microphone stream was interrupted. The recording is kept for recovery.", Error);
        var timer = Stopwatch.StartNew();
        while (SentBytes < CapturedBytes)
        {
            if (Error != null) throw new IOException("Microphone stream was interrupted. The recording is kept for recovery.", Error);
            if (timer.ElapsedMilliseconds > 20000) throw new TimeoutException("Audio is still waiting to reach the voice helper. The recording is kept.");
            await Task.Delay(20);
        }
        // Allow the CLI's current audio chunk to reach its transcription stream before F5.
        await Task.Delay(200);
    }

    internal void StopCapture() { if (microphone != null) microphone.Stop(); }
    internal async Task CaptureTail()
    {
        var timer = Stopwatch.StartNew();
        // Keep the normal 600 ms grace; extend a still-audible final syllable, bounded for noisy microphones.
        while (timer.ElapsedMilliseconds < 600 || timer.ElapsedMilliseconds < 1200 && SoundAge() < 200) await Task.Delay(25);
        StopCapture();
    }
    double SoundAge() { lock (gate) return (Stopwatch.GetTimestamp() - lastSound) * 1000.0 / Stopwatch.Frequency; }
    internal void ExpectDisconnect() { intentionalClose = true; }

    internal void EndSession()
    {
        intentionalClose = true;
        if (microphone != null) { microphone.Dispose(); microphone = null; }
        lock (gate) { generation++; if (client != null) client.Close(); }
        changed.Set();
    }

    internal byte[] Recording { get { lock (gate) return captured.ToArray(); } }
    internal byte[] Since(long offset)
    {
        lock (gate)
        {
            if (offset < 0 || offset > captured.Length) return new byte[0];
            var result = new byte[(int)(captured.Length - offset)];
            Buffer.BlockCopy(captured.GetBuffer(), (int)offset, result, 0, result.Length); return result;
        }
    }

    internal void Save(string path)
    {
        byte[] pcm = Recording;
        if (pcm.Length == 0) return;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (microphone != null) microphone.Dispose();
        disposed = true;
        listener.Stop();
        if (client != null) client.Close();
        changed.Set();
        // The sender owns the wait handle until it has observed shutdown.
        if (sender != null) sender.ContinueWith(delegate { changed.Dispose(); captured.Dispose(); });
    }
}

internal sealed class WaveInput : IDisposable
{
    const uint CallbackFunction = 0x30000, Data = 0x3C0;
    readonly Action<byte[]> receive;
    readonly Callback callback;
    readonly List<IntPtr> headers = new List<IntPtr>();
    readonly List<IntPtr> buffers = new List<IntPtr>();
    IntPtr device;
    volatile bool stopped;
    readonly object gate = new object();
    readonly ManualResetEventSlim returned = new ManualResetEventSlim(true);
    int inFlight;
    GCHandle lifetime;
    internal volatile Exception Error;

    internal WaveInput(Action<byte[]> receiver)
    {
        receive = receiver;
        callback = OnData;
        var format = new Format { tag = 1, channels = 1, rate = 16000, bytes = 32000, block = 2, bits = 16 };
        Check(waveInOpen(out device, uint.MaxValue, ref format, callback, IntPtr.Zero, CallbackFunction));
        lifetime = GCHandle.Alloc(this);
        try
        {
            for (int i = 0; i < 32; i++)
            {
                IntPtr data = Marshal.AllocHGlobal(640);
                IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));
                buffers.Add(data); headers.Add(pointer);
                Marshal.StructureToPtr(new Header { data = data, length = 640 }, pointer, false);
                Check(waveInPrepareHeader(device, pointer, (uint)Marshal.SizeOf(typeof(Header))));
                Check(waveInAddBuffer(device, pointer, (uint)Marshal.SizeOf(typeof(Header))));
                inFlight++; returned.Reset();
            }
            Check(waveInStart(device));
        }
        catch { Dispose(); throw; }
    }

    void OnData(IntPtr handle, uint message, IntPtr instance, IntPtr pointer, IntPtr ignored)
    {
        if (message != Data) return;
        lock (gate)
        {
            try
            {
                Header header = (Header)Marshal.PtrToStructure(pointer, typeof(Header));
                if (header.recorded > 0)
                {
                    var bytes = new byte[header.recorded];
                    Marshal.Copy(header.data, bytes, 0, bytes.Length);
                    receive(bytes);
                }
            }
            catch (Exception ex) { Error = ex; }
            if (--inFlight == 0) returned.Set();
            if (!stopped) ThreadPool.QueueUserWorkItem(delegate
            {
                lock (gate)
                {
                    if (stopped) return;
                    uint result = waveInAddBuffer(handle, pointer, (uint)Marshal.SizeOf(typeof(Header)));
                    if (result == 0) { inFlight++; returned.Reset(); }
                    else Error = new IOException("Microphone buffer could not be returned to Windows (" + result + ").");
                }
            });
        }
    }

    internal void Stop()
    {
        if (stopped) return;
        lock (gate) stopped = true;
        waveInStop(device);
        waveInReset(device); // Returns the partial final buffer too.
        if (!returned.Wait(3000)) Error = new IOException("Windows did not return the final microphone buffers.");
    }

    public void Dispose()
    {
        if (device == IntPtr.Zero) return;
        Stop();
        // A stalled driver must never receive pointers to freed buffers; retain its rooted handle until process exit.
        if (!returned.Wait(1000)) return;
        foreach (IntPtr header in headers) waveInUnprepareHeader(device, header, (uint)Marshal.SizeOf(typeof(Header)));
        waveInClose(device); device = IntPtr.Zero;
        foreach (IntPtr header in headers) Marshal.FreeHGlobal(header);
        foreach (IntPtr buffer in buffers) Marshal.FreeHGlobal(buffer);
        if (lifetime.IsAllocated) lifetime.Free();
        returned.Dispose();
    }

    static void Check(uint code) { if (code != 0) throw new IOException("Microphone could not start (Windows audio error " + code + ")."); }
    delegate void Callback(IntPtr handle, uint message, IntPtr instance, IntPtr parameter, IntPtr ignored);
    [StructLayout(LayoutKind.Sequential, Pack = 2)] struct Format { public ushort tag, channels; public uint rate, bytes; public ushort block, bits, extra; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public IntPtr data; public uint length, recorded; public UIntPtr user; public uint flags, loops; public IntPtr next; public UIntPtr reserved; }
    [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr device, uint id, ref Format format, Callback callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveInStop(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr device);
}
