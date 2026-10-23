using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// ---------------------------------------------------------------- native bits

static class Native
{
    public const int AF_BTH = 0x0022;
    public const int SOCK_STREAM = 1;
    public const int BTHPROTO_RFCOMM = 0x0003;
    public const int INVALID_SOCKET_NEG = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct SOCKADDR_BTH
    {
        public ushort addressFamily;
        public ulong btAddr;
        public uint port;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WSAData
    {
        public short wVersion, wHighVersion;
        public ushort iSocketSize, iMaxSockets, iMaxUdpDg, lpVendorInfo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4008)] public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4008)] public string systemStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FdSet
    {
        public uint count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public uint[] bitarray;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeVal
    {
        public int sec;
        public int usec;
    }

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int WSAStartup(short wVersionRequested, out WSAData wsaData);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int WSACleanup();
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern IntPtr socket(int af, int type, int protocol);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int connect(IntPtr s, ref SOCKADDR_BTH name, int namelen);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int send(IntPtr s, byte[] buf, int len, int flags);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int recv(IntPtr s, [Out] byte[] buf, int len, int flags);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int closesocket(IntPtr s);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int select(int nfds, ref FdSet readfds, IntPtr writefds, IntPtr exceptfds, ref TimeVal tv);
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int ioctlsocket(IntPtr s, int cmd, ref uint arg);
    [DllImport("ws2_32.dll")]
    public static extern int WSAGetLastError();

    static bool started;
    public static void EnsureWsa()
    {
        if (started) return;
        WSAData d;
        WSAStartup(0x0202, out d);
        started = true;
    }

    public static FdSet MakeSet(IntPtr sock)
    {
        FdSet f = new FdSet();
        f.bitarray = new uint[128];
        int idx = (int)(sock.ToInt64() & 0x1FF);
        f.bitarray[idx >> 5] |= (1u << (idx & 31));
        f.count = 1;
        return f;
    }

    // ---------------- bluetooth pairing / inquiry (bthprops.cpl) ----------------

    [StructLayout(LayoutKind.Sequential)]
    struct BT_FIND_RADIO_PARAMS { public int dwSize; }

    [StructLayout(LayoutKind.Sequential)]
    struct BT_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public bool fReturnAuthenticated;
        public bool fReturnRemembered;
        public bool fReturnUnknown;
        public bool fReturnConnected;
        public bool fIssueInquiry;
        public byte cTimeoutSeconds;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BT_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassOfDevice;
        public bool fConnected;
        public bool fRemembered;
        public bool fAuthenticated;
        public long LastSeen;
        public long LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern IntPtr BluetoothFindFirstRadio(ref BT_FIND_RADIO_PARAMS p, out IntPtr hRadio);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr hRadio);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern void BluetoothFindRadioClose(IntPtr hFind);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern IntPtr BluetoothFindFirstDevice(ref BT_DEVICE_SEARCH_PARAMS p, ref BT_DEVICE_INFO d);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BT_DEVICE_INFO d);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern bool BluetoothFindDeviceClose(IntPtr hFind);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern int BluetoothAuthenticateDeviceEx(IntPtr hWnd, IntPtr hRadio, ref BT_DEVICE_INFO d, IntPtr authCB, IntPtr passcode);
    [DllImport("bthprops.cpl", SetLastError = true)]
    public static extern bool BluetoothRemoveDevice(ref ulong address);

    public static string Mac(ulong a)
    {
        return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
            (a >> 40) & 0xFF, (a >> 32) & 0xFF, (a >> 24) & 0xFF, (a >> 16) & 0xFF, (a >> 8) & 0xFF, a & 0xFF);
    }

    public static ulong ParseMac(string s)
    {
        string[] p = s.Replace('-', ':').Split(':');
        if (p.Length != 6) throw new ArgumentException("bad mac: " + s);
        ulong v = 0;
        foreach (string x in p) v = (v << 8) | Convert.ToUInt64(x, 16);
        return v;
    }

    public static bool RadioPresent()
    {
        BT_FIND_RADIO_PARAMS rp = new BT_FIND_RADIO_PARAMS();
        rp.dwSize = Marshal.SizeOf(typeof(BT_FIND_RADIO_PARAMS));
        IntPtr hRadio;
        IntPtr h = BluetoothFindFirstRadio(ref rp, out hRadio);
        if (h == IntPtr.Zero) return false;
        BluetoothFindRadioClose(h);
        return true;
    }

    public static List<BT_DEVICE_INFO> FindDevices(int inquirySeconds)
    {
        List<BT_DEVICE_INFO> list = new List<BT_DEVICE_INFO>();
        IntPtr hRadio = IntPtr.Zero;
        if (inquirySeconds > 0)
        {
            // an inquiry needs a concrete radio handle; NULL is not accepted for discovery
            BT_FIND_RADIO_PARAMS rp = new BT_FIND_RADIO_PARAMS();
            rp.dwSize = Marshal.SizeOf(typeof(BT_FIND_RADIO_PARAMS));
            IntPtr hRf = BluetoothFindFirstRadio(ref rp, out hRadio);
            if (hRf == IntPtr.Zero)
            {
                Console.WriteLine("no radio handle, win32err=" + Marshal.GetLastWin32Error());
                return list;
            }
            BluetoothFindRadioClose(hRf);
        }
        BT_DEVICE_SEARCH_PARAMS sp = new BT_DEVICE_SEARCH_PARAMS();
        sp.dwSize = Marshal.SizeOf(typeof(BT_DEVICE_SEARCH_PARAMS));
        sp.fReturnAuthenticated = true;
        sp.fReturnRemembered = true;
        sp.fReturnUnknown = true;
        sp.fReturnConnected = true;
        sp.fIssueInquiry = inquirySeconds > 0;
        sp.cTimeoutSeconds = (byte)Math.Max(1, Math.Min(30, inquirySeconds));
        sp.hRadio = hRadio;
        BT_DEVICE_INFO di = new BT_DEVICE_INFO();
        di.dwSize = Marshal.SizeOf(typeof(BT_DEVICE_INFO));
        IntPtr h = BluetoothFindFirstDevice(ref sp, ref di);
        if (h == IntPtr.Zero) return list;
        do list.Add(di); while (BluetoothFindNextDevice(h, ref di));
        BluetoothFindDeviceClose(h);
        return list;
    }

    public static int Pair(ref BT_DEVICE_INFO d)
    {
        return BluetoothAuthenticateDeviceEx(IntPtr.Zero, IntPtr.Zero, ref d, IntPtr.Zero, IntPtr.Zero);
    }
}

// ---------------------------------------------------------------- links

interface IPrintLink : IDisposable
{
    void Write(byte[] data);
    int Read(byte[] buf, int timeoutMs);
    string Name { get; }
}

class BthLink : IPrintLink
{
    IntPtr sock = (IntPtr)(-1);
    string name;

    public BthLink(ulong mac, int channel)
    {
        Native.EnsureWsa();
        sock = Native.socket(Native.AF_BTH, Native.SOCK_STREAM, Native.BTHPROTO_RFCOMM);
        if (sock == (IntPtr)(-1)) throw new Exception("socket(AF_BTH) failed, wsaerr=" + Marshal.GetLastWin32Error());
        Native.SOCKADDR_BTH a = new Native.SOCKADDR_BTH();
        a.addressFamily = (ushort)Native.AF_BTH;
        a.btAddr = mac;
        a.port = (uint)channel;
        int r = Native.connect(sock, ref a, Marshal.SizeOf(typeof(Native.SOCKADDR_BTH)));
        if (r != 0)
        {
            int e = Marshal.GetLastWin32Error();
            closesocket();
            throw new Exception("rfcomm connect to " + Native.Mac(mac) + " channel " + channel +
                                " failed, wsaerr=" + e + " (10061=no service / 10060=timeout / 10065=host down)");
        }
        name = "rfcomm://" + Native.Mac(mac) + ":" + channel;
    }

    public string Name { get { return name; } }

    public void Write(byte[] data)
    {
        int off = 0;
        while (off < data.Length)
        {
            int chunk = Math.Min(1024, data.Length - off);
            byte[] tmp = new byte[chunk];
            Array.Copy(data, off, tmp, 0, chunk);
            int n = Native.send(sock, tmp, chunk, 0);
            if (n <= 0) throw new Exception("rfcomm send failed after " + off + "/" + data.Length + " bytes, wsaerr=" + Marshal.GetLastWin32Error());
            off += n;
        }
    }

    public int Read(byte[] buf, int timeoutMs)
    {
        Native.FdSet set = Native.MakeSet(sock);
        Native.TimeVal tv = new Native.TimeVal();
        tv.sec = timeoutMs / 1000;
        tv.usec = (timeoutMs % 1000) * 1000;
        int r = Native.select(0, ref set, IntPtr.Zero, IntPtr.Zero, ref tv);
        if (r <= 0) return 0;
        int got = Native.recv(sock, buf, buf.Length, 0);
        return got < 0 ? 0 : got;
    }

    void closesocket() { if (sock != (IntPtr)(-1)) { Native.closesocket(sock); sock = (IntPtr)(-1); } }
    public void Dispose() { closesocket(); }
}

class SerialLink : IPrintLink
{
    System.IO.Ports.SerialPort port;
    public SerialLink(string com)
    {
        port = new System.IO.Ports.SerialPort(com, 115200, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One);
        port.ReadTimeout = 1000;
        port.WriteTimeout = 15000;
        port.DtrEnable = true;
        port.RtsEnable = false;
        port.Handshake = System.IO.Ports.Handshake.None;
        port.Open();
    }
    public string Name { get { return port.PortName; } }
    public void Write(byte[] data) { port.Write(data, 0, data.Length); }
    public int Read(byte[] buf, int timeoutMs)
    {
        DateTime until = DateTime.Now.AddMilliseconds(timeoutMs);
        while (port.BytesToRead == 0 && DateTime.Now < until) System.Threading.Thread.Sleep(20);
        if (port.BytesToRead == 0) return 0;
        int n = 0;
        while (port.BytesToRead > 0 && n < buf.Length) buf[n++] = (byte)port.ReadByte();
        return n;
    }
    public void Dispose() { if (port != null) port.Close(); }
}

// ---------------------------------------------------------------- tspl

class Tspl
{
    public const int WidthDots = 384;
    public const int DotsPerMM = 8;
    public const int YOff = 8;
    public const double PaperMM = 54.0;

    public static byte[] Ascii(string s) { return Encoding.ASCII.GetBytes(s); }

    public static byte[] QueryStatus { get { return new byte[] { 0x1B, 0x21, 0x3F }; } }
    public static byte[] QueryConfig { get { return Ascii("CONFIG?\r\n"); } }
    public static byte[] QueryBattery { get { return Ascii("BATTERY?\r\n"); } }

    public static double LabelMM(int heightDots) { return (heightDots + 2.0 * YOff) / DotsPerMM; }

    public static byte[] Job(byte[] raster, int widthBytes, int heightDots, int density, double paperMM)
    {
        List<byte> b = new List<byte>();
        b.AddRange(QueryStatus); b.AddRange(Ascii("\r\n"));
        b.AddRange(Ascii("SIZE " + paperMM.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                        " mm," + LabelMM(heightDots).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm\r\n"));
        b.AddRange(Ascii("DIRECTION 0,0\r\n"));
        b.AddRange(Ascii("DENSITY " + density + "\r\n"));
        b.AddRange(Ascii("CLS\r\n"));
        b.AddRange(Ascii("PRINT 1\r\n"));
        b.AddRange(Ascii("BITMAP 0," + YOff + "," + widthBytes + "," + heightDots + ",1,"));
        b.AddRange(raster);
        b.AddRange(Ascii("ALLEND\r\n\r\n"));
        return b.ToArray();
    }

    public static string StatusText(byte b)
    {
        if (b == 0) return "ready";
        string[] names = { "head open", "paper jam", "no paper", "no ribbon", "paused", "printing", "cover open", "other error" };
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) sb.Append(names[i] + ", ");
        return sb.ToString().TrimEnd(',', ' ') + " (0x" + b.ToString("X2") + ")";
    }
}

// ---------------------------------------------------------------- raster

class Mono
{
    public int W, H;
    public byte[] P;               // one byte per pixel, 255 = white
    public byte[] Pack()
    {
        int wb = W / 8;
        byte[] o = new byte[wb * H];
        for (int i = 0; i < o.Length; i++) o[i] = 0xFF;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (P[y * W + x] < 128) o[y * wb + (x >> 3)] &= (byte)~(1 << (7 - (x & 7)));
        return o;
    }
    public int WidthBytes { get { return W / 8; } }

    public static Mono FromBitmap(Bitmap src, bool dither)
    {
        Bitmap img = src;
        bool owned = false;
        if (img.PixelFormat != PixelFormat.Format32bppArgb)
        {
            Bitmap c = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(c)) g.DrawImage(img, 0, 0);
            img = c; owned = true;
        }
        Rectangle r = new Rectangle(0, 0, img.Width, img.Height);
        BitmapData d = img.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[d.Stride * img.Height];
        Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        img.UnlockBits(d);
        if (owned) img.Dispose();

        Mono m = new Mono();
        m.W = src.Width; m.H = src.Height;
        m.P = new byte[m.W * m.H];
        double[] gray = new double[m.W * m.H];
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
            {
                int i = y * d.Stride + x * 4;
                double b = buf[i], g2 = buf[i + 1], rr = buf[i + 2], a = buf[i + 3];
                double v = 0.114 * b + 0.587 * g2 + 0.299 * rr;
                gray[y * m.W + x] = v * (a / 255.0) + 255.0 * (1 - a / 255.0);
            }
        if (!dither)
        {
            for (int i = 0; i < gray.Length; i++) m.P[i] = (byte)(gray[i] < 128 ? 0 : 255);
            return m;
        }
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
            {
                int i = y * m.W + x;
                double old = gray[i];
                double nv = old < 128 ? 0 : 255;
                double err = old - nv;
                m.P[i] = (byte)nv;
                if (x + 1 < m.W) gray[i + 1] += err * 7.0 / 16.0;
                if (y + 1 < m.H)
                {
                    if (x > 0) gray[i + m.W - 1] += err * 3.0 / 16.0;
                    gray[i + m.W] += err * 5.0 / 16.0;
                    if (x + 1 < m.W) gray[i + m.W + 1] += err * 1.0 / 16.0;
                }
            }
        return m;
    }
}

class Doc
{
    public Bitmap Bmp;
    public Bitmap ToPrintWidth(int targetDots)
    {
        double s = (double)targetDots / Bmp.Width;
        int w = targetDots, h = Math.Max(1, (int)Math.Round(Bmp.Height * s));
        Bitmap o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(o))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(Bmp, new Rectangle(0, 0, w, h));
        }
        return o;
    }
}

// ---------------------------------------------------------------- options

class Opt
{
    public int Density = 10;
    public double PaperMM = 54.0;
    public double MaxMM = 200;
    public int MaxDots = Tspl.WidthDots;
    public string Align = "left";
    public string Font = "Segoe UI";
    public int Size = 22;
    public bool Bold = false;
    public int Rotate = 0;
    public int Margin = 8;
    public bool Dither = true;
    public int Copies = 1;
    public int Channel = 1;
    public string Dev = "PM230";
    public string Mac = null;
    public string Com = null;
    public bool DryRun = false;
    public string Out = null;
    public int Settle = 6000;
    public int Port = 8090;
}

// ---------------------------------------------------------------- main

class Program
{
    static Opt opt = new Opt();
    static List<string> pos = new List<string>();

    [STAThread]
    static void Main(string[] argv)
    {
        List<string> args = new List<string>(argv);
        List<string> kept = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (!a.StartsWith("--")) { kept.Add(a); continue; }
            string k = a.Substring(2);
            if (k.Contains("="))
            {
                int p = k.IndexOf('=');
                Set(k.Substring(0, p), k.Substring(p + 1));
                continue;
            }
            switch (k)
            {
                case "dry-run": opt.DryRun = true; break;
                case "nodither": opt.Dither = false; break;
                case "bold": opt.Bold = true; break;
                case "wide": opt.MaxDots = Tspl.WidthDots; break;
                default:
                    if (i + 1 < args.Count) { Set(k, args[++i]); }
                    break;
            }
        }
        pos = kept;

        string cmd = pos.Count > 0 ? pos[0] : "help";
        try
        {
            switch (cmd)
            {
                case "help": case "-h": case "?": Help(); break;
                case "scan": Scan(); break;
                case "pair": PairCmd(); break;
                case "status": StatusCmd(); break;
                case "info": InfoCmd(); break;
                case "battery": InfoCmd(); break;
                case "text": TextCmd(); break;
                case "image": ImageCmd(); break;
                case "file": FileCmd(); break;
                case "clip": ClipCmd(); break;
                case "preview": PreviewCmd(); break;
                case "raw": RawCmd(); break;
                case "watch": WatchCmd(); break;
                case "sendto": SendToCmd(); break;
                case "web": case "ui": WebCmd(); break;
                default:
                    Console.WriteLine("unknown command: " + cmd);
                    Help();
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e.Message);
            Environment.Exit(1);
        }
    }

    static void Set(string k, string v)
    {
        switch (k)
        {
            case "density": opt.Density = int.Parse(v); break;
            case "paper": opt.PaperMM = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
            case "max-mm": opt.MaxMM = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
            case "width": opt.MaxDots = Math.Min(Tspl.WidthDots, int.Parse(v)); break;
            case "align": opt.Align = v; break;
            case "font": opt.Font = v; break;
            case "size": opt.Size = int.Parse(v); break;
            case "rotate": opt.Rotate = int.Parse(v); break;
            case "margin": opt.Margin = int.Parse(v); break;
            case "copies": opt.Copies = int.Parse(v); break;
            case "channel": opt.Channel = int.Parse(v); break;
            case "dev": opt.Dev = v; break;
            case "mac": opt.Mac = v; break;
            case "com": opt.Com = v; break;
            case "out": opt.Out = v; break;
            case "settle": opt.Settle = int.Parse(v); break;
            case "port": opt.Port = int.Parse(v); break;
        }
    }

    static void Help()
    {
        Console.WriteLine(@"Nelko PM230 - PC driver (TSPL over Bluetooth SPP / serial)

  pm230 scan [seconds]            find bluetooth devices (inquiry)
  pm230 pair [name]               pair the printer (default: PM230)
  pm230 status                    ask the printer if it is ready
  pm230 info                      CONFIG? + BATTERY? + status
  pm230 text ""Hello World""       print a text label
  pm230 image photo.png           print an image (png/jpg/bmp/gif)
  pm230 file a.png b.txt ...      print files, type chosen by extension
  pm230 clip                      print whatever is on the clipboard (screenshot or text)
  pm230 watch C:\pm230-queue      print every file dropped into a folder
  pm230 sendto                    add ""Send To -> PM230"" to the Windows menu
  pm230 web                       open the label editor in a browser (like the app)
  pm230 preview out.png text ""x"" write the exact bitmap instead of printing
  pm230 raw 1B213F0D0A            send raw bytes (debug)

options (after the command):
  --com COM4          use a serial port instead of bluetooth
  --mac AA:BB:CC:DD:EE:FF --channel 1   bluetooth address / rfcomm channel
  --dev PM230         device name to look up
  --density 0..15     darkness (default 10)
  --paper 54          paper width in mm (default 54)
  --width 384         print width in dots (max 384)
  --max-mm 200        refuse labels longer than this
  --align left|center|right      --margin 8      --font ""Segoe UI""  --size 22  --bold
  --rotate 0|90|180|270          --copies N      --nodither
  --dry-run           build the job, print nothing
  --out job.bin       also write the job bytes to a file");
    }

    // ---------------- transport ----------------

    static IPrintLink Open()
    {
        if (opt.Com != null)
        {
            Console.WriteLine("link: serial " + opt.Com);
            return new SerialLink(opt.Com);
        }
        List<string> ports = BluetoothComPorts();
        if (ports.Count > 0)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (string p in ports)
                {
                    try { IPrintLink l = new SerialLink(p); Console.WriteLine("link: bluetooth spp " + p + "  (" + opt.Dev + ")"); return l; }
                    catch (Exception e) { last = e; }
                }
                if (attempt == 0) System.Threading.Thread.Sleep(1500);   // a sleeping printer often needs a second try
            }
            throw new Exception("the printer is paired, but its serial link could not be opened (asleep? busy? phone still connected?): " +
                                (last == null ? "?" : last.Message));
        }
        if (opt.Mac != null)
        {
            ulong mac = Native.ParseMac(opt.Mac);
            Exception last = null;
            int[] channels = { opt.Channel, 2, 3, 4, 5 };
            foreach (int ch in channels)
            {
                try { IPrintLink l = new BthLink(mac, ch); Console.WriteLine("link: " + l.Name); return l; }
                catch (Exception e) { last = e; }
            }
            throw new Exception("could not open an rfcomm link: " + (last == null ? "?" : last.Message));
        }
        Console.WriteLine("no bluetooth printer named '" + opt.Dev + "' found.");
        Console.WriteLine("  pair it:   Settings > Bluetooth & other devices > Add device > Bluetooth   (name: " + opt.Dev + ")");
        Console.WriteLine("  or pick a port by hand:  pm230 info --com COM6");
        Environment.Exit(2);
        return null;
    }

    // the paired printer owns a "Serial over Bluetooth link" COM port; find it by the MAC in its PnP id
    static List<string> BluetoothComPorts()
    {
        List<string> outp = new List<string>();
        string macHex = null;
        try
        {
            using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'BTHENUM%'"))
            {
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    string nm = o["Name"] as string, id = o["DeviceID"] as string;
                    if (nm != null && id != null && nm.IndexOf(opt.Dev, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int i = id.IndexOf("DEV_");
                        if (i >= 0 && id.Length >= i + 16) { macHex = id.Substring(i + 4, 12); break; }
                    }
                }
            }
            if (macHex == null) return outp;
            using (System.Management.ManagementObjectSearcher s2 = new System.Management.ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports' AND DeviceID LIKE '%" + macHex + "%'"))
            {
                foreach (System.Management.ManagementObject o in s2.Get())
                {
                    string nm = o["Name"] as string;
                    if (nm == null) continue;
                    int a = nm.IndexOf("(COM"), b = a >= 0 ? nm.IndexOf(')', a) : -1;
                    if (a >= 0 && b > a) outp.Add(nm.Substring(a + 1, b - a - 1));
                }
            }
        }
        catch (Exception e) { Console.WriteLine("note: bluetooth port lookup failed: " + e.Message); }
        return outp;
    }

    static string Hex(byte[] b, int max)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < b.Length && i < max; i++)
        {
            byte c = b[i];
            if (c >= 32 && c < 127) sb.Append((char)c);
            else sb.Append("[" + c.ToString("X2") + "]");
        }
        return sb.ToString();
    }

    static int Ask(IPrintLink link, byte[] q, byte[] buf, StringBuilder log)
    {
        link.Write(q);
        int total = 0;
        byte[] tmp = new byte[256];
        DateTime until = DateTime.Now.AddMilliseconds(2500);
        while (DateTime.Now < until && total < buf.Length)
        {
            int n = link.Read(tmp, 400);
            if (n == 0) { if (total > 0) break; continue; }
            if (total + n > buf.Length) n = buf.Length - total;
            Array.Copy(tmp, 0, buf, total, n);
            total += n;
        }
        string s = total == 0 ? "(no answer)" : Hex(buf, total);
        log.Append(s);
        Console.WriteLine("  " + s);
        return total;
    }

    static void StatusCmd()
    {
        using (IPrintLink l = Open())
        {
            byte[] buf = new byte[256];
            int n = Ask(l, Tspl.QueryStatus, buf, new StringBuilder());
            if (n == 1) Console.WriteLine("  status: " + Tspl.StatusText(buf[0]));
        }
    }

    static void InfoCmd()
    {
        using (IPrintLink l = Open())
        {
            byte[] buf = new byte[512];
            Console.WriteLine("CONFIG?");
            int n = Ask(l, Tspl.QueryConfig, buf, new StringBuilder());
            int cAt = IndexOfBytes(buf, n, Tspl.Ascii("CONFIG "));
            if (cAt >= 0 && n >= cAt + 9)
            {
                int dpi = (buf[cAt + 7] << 8) | buf[cAt + 8];
                Console.WriteLine("  dpi = " + dpi + ", config bytes = " + HexRange(buf, cAt + 7, n - (cAt + 7)));
            }
            Array.Clear(buf, 0, buf.Length);
            Console.WriteLine("BATTERY?");
            n = Ask(l, Tspl.QueryBattery, buf, new StringBuilder());
            int bAt = IndexOfBytes(buf, n, Tspl.Ascii("BATTERY "));
            if (bAt >= 0 && n > bAt + 8)
            {
                int raw = buf[bAt + 8];
                int bcd = ((raw >> 4) & 0x0F) * 10 + (raw & 0x0F);
                Console.WriteLine("  battery = " + (bcd > 100 ? raw : bcd) + " %" + (raw == 0x99 ? " (0x99 = charging / full)" : ""));
            }
            Array.Clear(buf, 0, buf.Length);
            Console.WriteLine("status (ESC !?)");
            n = Ask(l, Tspl.QueryStatus, buf, new StringBuilder());
            if (n == 1) Console.WriteLine("  " + Tspl.StatusText(buf[0]));
            else if (n > 1) Console.WriteLine("  (not a status byte: the port answered '" + Encoding.ASCII.GetString(buf, 0, Math.Min(n, 12)) + "')");
        }
    }

    static int IndexOfBytes(byte[] hay, int len, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= len; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    static string HexRange(byte[] b, int from, int count)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = from; i < b.Length && i < from + count; i++) sb.Append(b[i].ToString("X2") + " ");
        return sb.ToString().TrimEnd();
    }

    static void Scan()
    {
        int secs = pos.Count > 1 ? int.Parse(pos[1]) : 12;
        if (!Native.RadioPresent())
        {
            Console.WriteLine("no bluetooth radio. On this PC:  Start-Service bthserv   and then");
            Console.WriteLine("  pnputil /restart-device \"USB\\VID_0E8D&PID_0717&MI_00\\7&26DBA176&0&0000\"");
            return;
        }
        Console.WriteLine("inquiry for " + secs + "s ...");
        List<Native.BT_DEVICE_INFO> ds = Native.FindDevices(secs);
        if (ds.Count == 0)
            Console.WriteLine("(the inquiry found nothing new - already paired devices are listed below)");
        foreach (Native.BT_DEVICE_INFO d in ds)
            Console.WriteLine("{0,-24} {1} class=0x{2:X6} paired={3,-5} connected={4} {5}",
                d.szName, Native.Mac(d.Address), d.ulClassOfDevice, d.fAuthenticated, d.fConnected,
                d.szName != null && d.szName.IndexOf("PM230", StringComparison.OrdinalIgnoreCase) >= 0 ? "  <-- printer" : "");
        Console.WriteLine("paired with windows:");
        using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher(
            "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'BTHENUM%'"))
        {
            foreach (System.Management.ManagementObject o in s.Get())
            {
                string nm = o["Name"] as string, id = o["DeviceID"] as string;
                int i = id != null ? id.IndexOf("DEV_") : -1;
                string mac = i >= 0 && id.Length >= i + 16 ? id.Substring(i + 4, 12) : "?";
                Console.WriteLine("  {0,-24} {1} {2}", nm, mac, nm != null && nm.IndexOf(opt.Dev, StringComparison.OrdinalIgnoreCase) >= 0 ? "  <-- printer" : "");
            }
        }
        List<string> ports = BluetoothComPorts();
        if (ports.Count > 0) Console.WriteLine("  serial link of the printer: " + string.Join(", ", ports.ToArray()));
    }

    static void PairCmd()
    {
        string want = pos.Count > 1 ? pos[1] : opt.Dev;
        Console.WriteLine("searching for '" + want + "' (15s inquiry), keep the printer switched on ...");
        List<Native.BT_DEVICE_INFO> ds = Native.FindDevices(15);
        Native.BT_DEVICE_INFO hit = new Native.BT_DEVICE_INFO();
        bool found = false;
        foreach (Native.BT_DEVICE_INFO d in ds)
            if ((d.szName ?? "").IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { hit = d; found = true; break; }
        if (!found)
        {
            Console.WriteLine("not in range. devices seen: " + ds.Count);
            foreach (Native.BT_DEVICE_INFO d in ds) Console.WriteLine("   " + d.szName + " " + Native.Mac(d.Address));
            Environment.Exit(2);
        }
        if (hit.fAuthenticated) { Console.WriteLine("already paired: " + Native.Mac(hit.Address)); return; }
        int r = Native.Pair(ref hit);
        Console.WriteLine("pair -> ret=" + r + " win32err=" + Marshal.GetLastWin32Error() + " authenticated=" + hit.fAuthenticated);
        Console.WriteLine("now try:  pm230 info");
    }

    // ---------------- printing ----------------

    static Bitmap TextBitmap(string text)
    {
        int w = opt.MaxDots - 2 * opt.Margin;
        if (w < 16) w = 16;
        FontStyle st = opt.Bold ? FontStyle.Bold : FontStyle.Regular;
        Font f = new Font(opt.Font, opt.Size, st, GraphicsUnit.Pixel);
        StringFormat sf = new StringFormat(StringFormatFlags.LineLimit);
        Rectangle layout = new Rectangle(0, 0, w, 100000);
        SizeF need;
        using (Bitmap probe = new Bitmap(64, 64))
        using (Graphics g = Graphics.FromImage(probe))
            need = g.MeasureString(text, f, new SizeF(w, 100000), sf);
        int h = (int)Math.Ceiling(need.Height) + 4;
        Bitmap bmp = new Bitmap(opt.MaxDots, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float x = opt.Margin;
            if (opt.Align == "center") x = (opt.MaxDots - need.Width) / 2.0f;
            if (opt.Align == "right") x = opt.MaxDots - opt.Margin - need.Width;
            g.DrawString(text, f, Brushes.Black, new RectangleF(x, 2, need.Width + 2, h), sf);
        }
        f.Dispose();
        return bmp;
    }

    static Bitmap LoadImage(string path)
    {
        using (Bitmap raw = new Bitmap(path))
        {
            Bitmap bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(raw, 0, 0);
            switch (((opt.Rotate % 360) + 360) % 360)
            {
                case 90: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 180: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 270: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
            if (bmp.Width > Tspl.WidthDots)
            {
                double s = (double)Tspl.WidthDots / bmp.Width;
                Bitmap r = new Bitmap(Tspl.WidthDots, Math.Max(1, (int)Math.Round(bmp.Height * s)), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(r))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new Rectangle(0, 0, r.Width, r.Height));
                }
                bmp.Dispose();
                return r;
            }
            return bmp;
        }
    }

    static byte[] BuildJob(Bitmap bmp, out int dots)
    {
        Bitmap use = bmp;
        if (use.Width != Tspl.WidthDots)
        {
            double s = (double)Tspl.WidthDots / use.Width;
            Bitmap r = new Bitmap(Tspl.WidthDots, Math.Max(1, (int)Math.Round(use.Height * s)), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(r))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(use, new Rectangle(0, 0, r.Width, r.Height));
            }
            use = r;
        }
        double mm = Tspl.LabelMM(use.Height);
        if (mm > opt.MaxMM) throw new Exception("label would be " + mm.ToString("0.#") + " mm tall, limit is --max-mm " + opt.MaxMM);
        Mono m = Mono.FromBitmap(use, opt.Dither);
        dots = use.Height;
        if (use != bmp) use.Dispose();
        return Tspl.Job(m.Pack(), m.WidthBytes, m.H, opt.Density, opt.PaperMM);
    }

    static void Emit(Bitmap bmp, string what)
    {
        int dots;
        byte[] job = BuildJob(bmp, out dots);
        double mm = Tspl.LabelMM(dots);
        Console.WriteLine(what + ": " + dots + " dots = " + mm.ToString("0.0") + " mm paper, job " + job.Length + " bytes");
        if (opt.Out != null) { File.WriteAllBytes(opt.Out, job); Console.WriteLine("  job written to " + opt.Out); }
        if (opt.DryRun) return;
        using (IPrintLink l = Open())
        {
            for (int i = 0; i < opt.Copies; i++)
            {
                l.Write(job);
                byte[] buf = new byte[512];
                StringBuilder heard = new StringBuilder();
                DateTime until = DateTime.Now.AddMilliseconds(opt.Settle);
                while (DateTime.Now < until)
                {
                    int n = l.Read(buf, 300);
                    if (n > 0) heard.Append(Hex(buf, n));
                }
                Console.WriteLine("  printer said: " + (heard.Length == 0 ? "(nothing)" : heard.ToString()));
            }
        }
    }

    static void TextCmd()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 1; i < pos.Count; i++) { if (i > 1) sb.Append(' '); sb.Append(pos[i]); }
        string text = sb.ToString();
        if (text == "-" || text == "") text = Console.In.ReadToEnd();
        Emit(TextBitmap(text.Replace("\\n", "\n")), "text");
    }

    static void ImageCmd()
    {
        if (pos.Count < 2) throw new Exception("usage: pm230 image <file>");
        Emit(LoadImage(pos[1]), "image " + Path.GetFileName(pos[1]));
    }

    static void FileCmd()
    {
        for (int i = 1; i < pos.Count; i++) PrintOne(pos[i]);
    }

    static void PrintOne(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".txt" || ext == ".log" || ext == ".md" || ext == ".csv" || ext == "")
        {
            string t = File.ReadAllText(path).TrimEnd();
            Emit(TextBitmap(t), "text " + Path.GetFileName(path));
        }
        else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff")
        {
            Emit(LoadImage(path), "image " + Path.GetFileName(path));
        }
        else
        {
            Console.WriteLine("skipping " + path + " (." + ext.TrimStart('.') + " not handled yet)");
        }
    }

    static void ClipCmd()
    {
        if (System.Windows.Forms.Clipboard.ContainsImage())
        {
            Image raw = System.Windows.Forms.Clipboard.GetImage();
            if (raw == null) throw new Exception("clipboard image could not be read");
            Bitmap bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
            }
            raw.Dispose();
            if (opt.Rotate != 0)
            {
                switch (((opt.Rotate % 360) + 360) % 360)
                {
                    case 90: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 180: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 270: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                }
            }
            Emit(bmp, "clipboard image " + bmp.Width + "x" + bmp.Height);
            return;
        }
        if (System.Windows.Forms.Clipboard.ContainsText())
        {
            Emit(TextBitmap(System.Windows.Forms.Clipboard.GetText().TrimEnd()), "clipboard text");
            return;
        }
        Console.WriteLine("clipboard holds neither an image nor text.");
    }

    static void PreviewCmd()
    {
        string outp = pos.Count > 1 ? pos[1] : "preview.png";
        string kind = pos.Count > 2 ? pos[2] : "text";
        Bitmap bmp;
        if (kind == "image" && pos.Count > 3) bmp = LoadImage(pos[3]);
        else
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 3; i < pos.Count; i++) { if (sb.Length > 0) sb.Append(' '); sb.Append(pos[i]); }
            bmp = TextBitmap(sb.Length == 0 ? "PM230 preview" : sb.ToString());
        }
        bmp.Save(outp, ImageFormat.Png);
        Console.WriteLine("preview -> " + outp + "  (" + bmp.Width + " x " + bmp.Height + " dots = " + Tspl.LabelMM(bmp.Height).ToString("0.0") + " mm)");
        bmp.Dispose();
    }

    static void RawCmd()
    {
        if (pos.Count < 2) throw new Exception("usage: pm230 raw <hexbytes|@file>");
        byte[] data;
        string a = pos[1];
        if (a.StartsWith("@")) data = File.ReadAllBytes(a.Substring(1));
        else
        {
            string hex = a.Replace(" ", "");
            data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        using (IPrintLink l = Open())
        {
            l.Write(data);
            byte[] buf = new byte[1024];
            StringBuilder sb = new StringBuilder();
            DateTime until = DateTime.Now.AddMilliseconds(opt.Settle);
            while (DateTime.Now < until) { int n = l.Read(buf, 300); if (n > 0) sb.Append(Hex(buf, n)); }
            Console.WriteLine("sent " + data.Length + " bytes, reply: " + (sb.Length == 0 ? "(nothing)" : sb.ToString()));
        }
    }

    static void WatchCmd()
    {
        string dir = pos.Count > 1 ? pos[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "pm230-queue");
        Directory.CreateDirectory(dir);
        Console.WriteLine("watching " + dir + "  (drop png/jpg/txt files there, Ctrl+C stops)");
        Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();
        while (true)
        {
            foreach (string f in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith(".") || name.EndsWith(".done") || name.EndsWith(".err")) continue;
                DateTime w = File.GetLastWriteTime(f);
                if (seen.ContainsKey(name) && seen[name] == w) continue;
                seen[name] = w;
                System.Threading.Thread.Sleep(400);
                try { PrintOne(f); File.Move(f, f + ".done"); }
                catch (Exception e) { Console.WriteLine("  failed: " + e.Message); try { File.Move(f, f + ".err"); } catch { } }
            }
            System.Threading.Thread.Sleep(500);
        }
    }

    static void SendToCmd()
    {
        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft\Windows\SendTo";
        Directory.CreateDirectory(sendTo);
        string lnk = Path.Combine(sendTo, "PM230.lnk");
        Type sh = Type.GetTypeFromProgID("WScript.Shell");
        dynamic w = Activator.CreateInstance(sh);
        dynamic l = w.CreateShortcut(lnk);
        l.TargetPath = exe;
        l.Arguments = "file \"%1\"";
        l.WorkingDirectory = Path.GetDirectoryName(exe);
        l.Description = "print on the Nelko PM230";
        l.Save();
        System.Runtime.InteropServices.Marshal.ReleaseComObject(w);
        Console.WriteLine("added to the Windows 'Send To' menu:  PM230   (" + lnk + ")");
    }

    // ---------------- label editor (local web ui) ----------------

    static string ExeDir() { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }

    static string WebRoot() { return Path.GetFullPath(Path.Combine(ExeDir(), "..", "web")); }

    static void WebCmd()
    {
        string root = WebRoot();
        if (!File.Exists(Path.Combine(root, "index.html")))
        {
            Console.WriteLine("the editor files are missing in " + root);
            return;
        }
        System.Net.HttpListener ls = new System.Net.HttpListener();
        ls.Prefixes.Add("http://localhost:" + opt.Port + "/");
        try { ls.Start(); }
        catch (Exception e)
        {
            Console.WriteLine("port " + opt.Port + " is not usable: " + e.Message);
            return;
        }
        string url = "http://localhost:" + opt.Port + "/";
        Console.WriteLine("Nelko PM230 label editor:  " + url);
        List<string> ports = BluetoothComPorts();
        Console.WriteLine(ports.Count > 0 ? "printer reachable on " + ports[0] + "  (Ctrl+C stops the editor)"
                                          : "warning: printer not paired / switched off");
        try { System.Diagnostics.Process.Start(url); } catch { }
        while (true)
        {
            System.Net.HttpListenerContext ctx;
            try { ctx = ls.GetContext(); } catch { break; }
            try { Route(ctx, root); }
            catch (Exception e) { Reply(ctx, 500, "text/plain", Encoding.UTF8.GetBytes("error: " + e.Message)); }
        }
    }

    static void Route(System.Net.HttpListenerContext ctx, string root)
    {
        string p = ctx.Request.Url.AbsolutePath;
        if (p.StartsWith("/api/")) { Api(ctx, p); return; }
        if (p == "/" || p == "") p = "/index.html";
        string rel = p.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string f = Path.GetFullPath(Path.Combine(root, rel));
        if (!f.StartsWith(root + Path.DirectorySeparatorChar) || !File.Exists(f))
        {
            Reply(ctx, 404, "text/plain", Encoding.UTF8.GetBytes("not found: " + p));
            return;
        }
        string type = f.EndsWith(".js") ? "application/javascript" : f.EndsWith(".css") ? "text/css" : "text/html; charset=utf-8";
        Reply(ctx, 200, type, File.ReadAllBytes(f));
    }

    static void Reply(System.Net.HttpListenerContext ctx, int code, string type, byte[] data)
    {
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = type;
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }

    static byte[] Body(System.Net.HttpListenerContext ctx)
    {
        using (Stream s = ctx.Request.InputStream)
        using (MemoryStream m = new MemoryStream())
        {
            byte[] b = new byte[8192];
            int n;
            while ((n = s.Read(b, 0, b.Length)) > 0) m.Write(b, 0, n);
            return m.ToArray();
        }
    }

    static int Qint(System.Net.HttpListenerContext ctx, string name, int dflt)
    {
        string v = ctx.Request.QueryString[name];
        int r;
        return v != null && int.TryParse(v, out r) ? r : dflt;
    }

    static double Qdbl(System.Net.HttpListenerContext ctx, string name, double dflt)
    {
        string v = ctx.Request.QueryString[name];
        double r;
        return v != null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : dflt;
    }

    static string JsonEscape(string s)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c < 32) sb.Append("\\n");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static void Api(System.Net.HttpListenerContext ctx, string p)
    {
        if (p == "/api/info")
        {
            string status = "unknown", battery = "?", link = "none";
            try
            {
                using (IPrintLink l = Open())
                {
                    link = l.Name;
                    byte[] buf = new byte[256];
                    int n = Ask(l, Tspl.QueryBattery, buf, new StringBuilder());
                    int at = IndexOfBytes(buf, n, Tspl.Ascii("BATTERY "));
                    if (at >= 0 && n > at + 8)
                    {
                        int raw = buf[at + 8];
                        int bcd = ((raw >> 4) & 0x0F) * 10 + (raw & 0x0F);
                        battery = (bcd > 100 ? raw : bcd) + "";
                    }
                    Array.Clear(buf, 0, buf.Length);
                    n = Ask(l, Tspl.QueryStatus, buf, new StringBuilder());
                    status = n == 1 ? Tspl.StatusText(buf[0]) : (n == 0 ? "no answer" : Hex(buf, Math.Min(n, 12)));
                }
            }
            catch (Exception e) { status = "not reachable: " + e.Message; }
            Reply(ctx, 200, "application/json", Encoding.UTF8.GetBytes(
                "{\"battery\":\"" + battery + "\",\"status\":\"" + JsonEscape(status) + "\",\"link\":\"" + link + "\"}"));
            return;
        }
        if (p == "/api/print" || p == "/api/job")
        {
            byte[] packed = Body(ctx);
            int h = Qint(ctx, "h", 0), wb = Qint(ctx, "wb", 48);
            int density = Qint(ctx, "density", 10), copies = Math.Max(1, Qint(ctx, "copies", 1));
            double paper = Qdbl(ctx, "paper", 54);
            byte[] job;
            if (p == "/api/job") job = packed;
            else
            {
                if (h <= 0 || wb <= 0 || packed.Length < wb * h)
                {
                    Reply(ctx, 400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"bad raster: got " + packed.Length + " bytes, need " + wb * h + "\"}"));
                    return;
                }
                job = Tspl.Job(packed, wb, h, density, paper);
            }
            StringBuilder heard = new StringBuilder();
            try
            {
                using (IPrintLink l = Open())
                {
                    byte[] buf = new byte[512];
                    Ask(l, Tspl.QueryStatus, buf, heard);   // wake up / handshake, same as the CLI does
                    for (int i = 0; i < copies; i++)
                    {
                        l.Write(job);
                        DateTime until = DateTime.Now.AddMilliseconds(opt.Settle);
                        while (DateTime.Now < until)
                        {
                            int n = l.Read(buf, 300);
                            if (n > 0) heard.Append(Hex(buf, n));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Reply(ctx, 500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"" + JsonEscape(e.Message) + "\"}"));
                return;
            }
            Reply(ctx, 200, "application/json", Encoding.UTF8.GetBytes(
                "{\"ok\":true,\"bytes\":" + job.Length + ",\"reply\":\"" + JsonEscape(heard.ToString()) + "\"}"));
            return;
        }
        if (p == "/api/pdf")
        {
            byte[] pdf = Body(ctx);
            string tmp = Path.Combine(Path.GetTempPath(), "pm230");
            Directory.CreateDirectory(tmp);
            foreach (string old in Directory.GetFiles(tmp, "page-*.png")) { try { File.Delete(old); } catch { } }
            string src = Path.Combine(tmp, "input.pdf");
            File.WriteAllBytes(src, pdf);
            string helper = Path.Combine(ExeDir(), "pdf2png.ps1");
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + helper + "\" -In \"" + src + "\" -OutDir \"" + tmp + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            string err = "";
            try
            {
                using (System.Diagnostics.Process pr = System.Diagnostics.Process.Start(psi))
                {
                    err = pr.StandardError.ReadToEnd();
                    pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(90000);
                }
            }
            catch (Exception e) { err = e.Message; }
            string[] files = Directory.GetFiles(tmp, "page-*.png");
            Array.Sort(files);
            if (files.Length == 0)
            {
                Reply(ctx, 500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"" + JsonEscape("no pages rendered: " + err) + "\"}"));
                return;
            }
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"pages\":[");
            bool first = true;
            foreach (string f in files)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("\"data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(f)) + "\"");
            }
            sb.Append("]}");
            Reply(ctx, 200, "application/json", Encoding.UTF8.GetBytes(sb.ToString()));
            return;
        }
        Reply(ctx, 404, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"unknown api " + p + "\"}"));
    }
}
