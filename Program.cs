// ============================================================================
//  MMServer Manager
//  Copyright (c) 2026 90minutes - https://www.youtube.com/@90minu93
//  Released under the MIT License (see LICENSE): free to use and modify, keep this notice and the
//  author credit when you fork or redistribute.
//  Written in C# 5 so the compiler that ships with Windows can build it (see build.bat).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle(MMServerManager.Credits.Product)]
[assembly: AssemblyDescription("Portable server control manager")]
[assembly: AssemblyCompany("90minutes")]
[assembly: AssemblyProduct(MMServerManager.Credits.Product)]
[assembly: AssemblyCopyright("Copyright \u00A9 2026 90minutes - https://www.youtube.com/@90minu93")]
[assembly: AssemblyVersion(MMServerManager.Credits.Version + ".0.0")]
[assembly: AssemblyFileVersion(MMServerManager.Credits.Version + ".0.0")]

namespace MMServerManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(Settings.BaseDir, "manager.log"), DateTime.Now.ToString("HH:mm:ss") + "  UNEXPECTED ERROR: " + e.Exception + Environment.NewLine); } catch (Exception) { }
                MessageBox.Show(e.Exception.Message, Credits.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }
    }

    class Comp
    {
        public string Name; public int Port; public int StartWait; public int StopWait;
        public Comp(string n, int p, int sw, int st) { Name = n; Port = p; StartWait = sw; StopWait = st; }
    }

    class Settings
    {
        public string ServerDir, ClientDir, Ip = "", LastEncodedIp = "";
        public string Lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi" ? "vi" : "en";
        public int DbPort = 3307;

        public static string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        static string FilePath { get { return Path.Combine(BaseDir, "MMServerManager.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            s.ServerDir = Path.Combine(BaseDir, "MuServer");
            s.ClientDir = Path.Combine(BaseDir, "Client");
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        if (k == "ServerDir" && v.Length > 0) s.ServerDir = v;
                        else if (k == "ClientDir" && v.Length > 0) s.ClientDir = v;
                        else if (k == "Ip") s.Ip = v;
                        else if (k == "LastEncodedIp") s.LastEncodedIp = v;
                        else if (k == "Lang" && (v == "vi" || v == "en")) s.Lang = v;
                        else if (k == "DbPort") { int p; if (int.TryParse(v, out p)) s.DbPort = p; }
                    }
            }
            catch (Exception) { }
            // folders saved earlier may have been moved: fall back to the folders next to the manager
            string defServer = Path.Combine(BaseDir, "MuServer"), defClient = Path.Combine(BaseDir, "Client");
            if (!Directory.Exists(s.ServerDir) && Directory.Exists(defServer)) s.ServerDir = defServer;
            if (!Directory.Exists(s.ClientDir) && Directory.Exists(defClient)) s.ClientDir = defClient;
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new string[] {
                    "ServerDir=" + ServerDir, "ClientDir=" + ClientDir, "Ip=" + Ip,
                    "LastEncodedIp=" + LastEncodedIp, "DbPort=" + DbPort, "Lang=" + Lang });
            }
            catch (Exception) { }
        }
    }

    class Core
    {
        public Settings S;
        public Action<string> Log;
        public volatile string Starting = "";
        string rootPw, userPw;

        public static readonly Comp[] Servers = new Comp[] {
            new Comp("DataServer", 55980, 60, 30),
            new Comp("JoinServer", 55990, 60, 15),
            new Comp("ConnectServer", 44405, 60, 15),
            new Comp("GameServer", 55901, 180, 120) };
        static readonly string[] Outputs = new string[] { "main.exe", "Main.dll", @"Data\Local\ClientInfo.bmd" };
        const string VcUrl = "https://aka.ms/vs/17/release/vc_redist.x86.exe";

        public Core(Settings s, Action<string> log)
        {
            S = s; Log = log;
            string v; int p;
            if (ReadCreds().TryGetValue("port", out v) && int.TryParse(v, out p)) S.DbPort = p;
        }

        // ---------- paths ----------
        string DbDir { get { return Path.Combine(S.ServerDir, "DB"); } }
        string MariaDir { get { return Path.Combine(DbDir, "mariadb"); } }
        string DataDir { get { return Path.Combine(DbDir, "data"); } }
        string Marker { get { return Path.Combine(DataDir, ".mu-initialized"); } }
        string CredFile { get { return Path.Combine(DbDir, "db-credentials.txt"); } }
        string Bin(string exe) { return Path.Combine(MariaDir, "bin", exe); }

        Dictionary<string, string> ReadCreds()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (!File.Exists(CredFile)) return d;
            foreach (string line in File.ReadAllLines(CredFile))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }

        // ---------- process / port helpers ----------
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        HashSet<long> seenWindows = new HashSet<long>();

        // The servers ask "are you sure?" when closed. Find that dialog and press its OK/Yes button
        // (button ids 1 = IDOK, 6 = IDYES). Every window found is logged once so a mismatch can be diagnosed.
        void ConfirmDialogs(List<Process> procs, string name)
        {
            HashSet<int> pids = new HashSet<int>();
            foreach (Process p in procs) pids.Add(p.Id);
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains((int)pid) || !IsWindowVisible(h)) return true;
                StringBuilder cls = new StringBuilder(64); GetClassName(h, cls, 64);
                StringBuilder title = new StringBuilder(256); GetWindowText(h, title, 256);
                bool isDialog = cls.ToString() == "#32770";
                IntPtr target = IntPtr.Zero;
                if (isDialog)
                    EnumChildWindows(h, delegate(IntPtr c, IntPtr lp2)
                    {
                        StringBuilder cc = new StringBuilder(32); GetClassName(c, cc, 32);
                        int id = GetDlgCtrlID(c);
                        if (cc.ToString() == "Button" && (id == 1 || id == 6) && target == IntPtr.Zero) target = c;
                        return true;
                    }, IntPtr.Zero);
                if (seenWindows.Add(h.ToInt64()))
                    Log(name + ": window [" + cls + "] \"" + title + "\"" + (isDialog ? (target != IntPtr.Zero ? " -> confirming" : " -> no OK/Yes button found") : ""));
                if (target != IntPtr.Zero) PostMessage(target, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
                return true;
            }, IntPtr.Zero);
        }

        static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION, works for 32 and 64-bit
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // Processes with this name whose exe lives under dir (so another MuServer copy is never touched)
        public static List<Process> FindProcs(string name, string dir)
        {
            List<Process> res = new List<Process>();
            string prefix = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            foreach (Process p in Process.GetProcessesByName(name))
            {
                string f = ImagePath(p.Id);
                if (f != null && f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) res.Add(p);
            }
            return res;
        }

        public static bool IsListening(int port)
        {
            try
            {
                foreach (IPEndPoint ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                    if (ep.Port == port) return true;
            }
            catch (Exception) { }
            return false;
        }

        static bool AnyAlive(List<Process> list)
        {
            foreach (Process p in list)
            {
                try { if (!p.HasExited) return true; } catch (Exception) { }
            }
            return false;
        }

        bool WaitPort(string name, int port, int timeoutSec, Process proc)
        {
            DateTime end = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < end)
            {
                if (IsListening(port)) return true;
                if (proc != null && proc.HasExited) { Log(name + ": process exited before opening port " + port + ". Check its log."); return false; }
                Thread.Sleep(500);
            }
            Log(name + ": port " + port + " not open after " + timeoutSec + " s.");
            return false;
        }

        int RunTool(string exe, string args, string pwd, string workDir, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.WorkingDirectory = workDir;
            if (pwd != null) psi.EnvironmentVariables["MYSQL_PWD"] = pwd;
            StringBuilder sb = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        // ---------- checks ----------
        public void Preflight()
        {
            int rel = 0;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k != null) { object v = k.GetValue("Release"); if (v != null) rel = Convert.ToInt32(v); }
                }
            }
            catch (Exception) { }
            Log(".NET Framework release " + rel + (rel >= 528040 ? " (4.8 or newer): OK" : " (older than 4.8): the manager may still work, 4.8 is recommended"));

            string sys = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);   // SysWOW64 on 64-bit Windows
            bool vc = File.Exists(Path.Combine(sys, "vcruntime140.dll")) && File.Exists(Path.Combine(sys, "msvcp140.dll"));
            if (!vc)
                throw new Exception("Visual C++ Redistributable 2015-2022 (x86, 32-bit) is missing. The servers need it. Download: " + VcUrl);
            Log("Visual C++ Redistributable (x86): OK");
        }

        // The Kayito repo keeps Encoder next to MuServer; a copy inside MuServer is accepted too.
        string EncDir
        {
            get
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(S.ServerDir).TrimEnd('\\'));
                string sibling = parent == null ? "" : Path.Combine(parent, "Encoder");
                if (sibling.Length > 0 && Directory.Exists(sibling)) return sibling;
                return Path.Combine(S.ServerDir, "Encoder");
            }
        }

        public void CheckPaths()
        {
            if (!Directory.Exists(S.ServerDir)) throw new Exception("Server folder not found: " + S.ServerDir);
            foreach (string n in new string[] { "ConnectServer", "GameServer" })
                if (!Directory.Exists(Path.Combine(S.ServerDir, n))) throw new Exception("Missing folder: " + Path.Combine(S.ServerDir, n));
            // DataServer / JoinServer are created from the MySQL folder when they are not there yet
            foreach (string n in new string[] { "DataServer", "JoinServer" })
                if (!Directory.Exists(Path.Combine(S.ServerDir, n)) && !Directory.Exists(Path.Combine(S.ServerDir, "MySQL", n)))
                    throw new Exception("Neither " + Path.Combine(S.ServerDir, n) + " nor " + Path.Combine(S.ServerDir, "MySQL", n) + " exists.");
            if (!Directory.Exists(EncDir)) throw new Exception("Encoder folder not found: " + EncDir);
            if (!Directory.Exists(S.ClientDir)) throw new Exception("Client folder not found: " + S.ClientDir);
            if (!Directory.Exists(Path.Combine(MariaDir, "bin")) && Directory.GetFiles(Settings.BaseDir, "mariadb*.zip").Length == 0)
                throw new Exception("MariaDB ZIP not found. Download the Windows ZIP (10.11) from https://mariadb.org/download and put it next to the manager; its file name must start with \"mariadb\".");
        }

        // ---------- IP + client ----------
        public void ApplyIp(string ip)
        {
            Match m = Regex.Match(ip ?? "", @"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$");
            bool ok = m.Success;
            for (int i = 1; ok && i <= 4; i++) if (int.Parse(m.Groups[i].Value) > 255) ok = false;
            if (!ok) throw new Exception("Enter a valid IPv4 address (example 192.168.1.20).");
            // Only the IP is edited; everything else in the original files stays as it is (first original kept as *.orig)
            EditFile(Path.Combine(S.ServerDir, "ConnectServer", "ServerList.dat"),
                     @"(?m)^([ \t]*\d+[ \t]+""[^""]*""[ \t]+"")[^""]*("")", ip, "server address");
            EditFile(Path.Combine(EncDir, "MainInfo.ini"),
                     @"(?m)^(IpAddress[ \t]*=[ \t]*)[^\r\n]*", ip, "IpAddress");
            Log("IP " + ip + " set in ServerList.dat and MainInfo.ini");
        }

        void EditFile(string path, string pattern, string ip, string what)
        {
            if (!File.Exists(path)) throw new Exception("Missing " + path);
            Encoding enc = Encoding.GetEncoding(28591);   // Latin-1: every byte survives the round trip unchanged
            string text = enc.GetString(File.ReadAllBytes(path));
            int count = 0;
            string result = Regex.Replace(text, pattern, delegate(Match m)
            {
                count++;
                return m.Groups[1].Value + ip + (m.Groups.Count > 2 ? m.Groups[2].Value : "");
            });
            if (count == 0) throw new Exception("Could not find the " + what + " in " + path);
            if (result == text) return;
            if (!File.Exists(path + ".orig")) File.Copy(path, path + ".orig");
            File.WriteAllBytes(path, enc.GetBytes(result));
        }

        // The encoder rewrites only Data\Local\ClientInfo.bmd (fresh timestamp every run).
        // main.exe and Main.dll are static files that live in Encoder\Client and are just copied along.
        static string OutputSignature(string outDir, DateTime start)
        {
            string f = Path.Combine(outDir, @"Data\Local\ClientInfo.bmd");
            if (!File.Exists(f) || File.GetLastWriteTime(f) < start.AddSeconds(-2)) return null;
            FileInfo fi = new FileInfo(f);
            return fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
        }

        public void RunEncoder()
        {
            string encDir = EncDir;
            string exe = Path.Combine(encDir, "InfoEncoder.exe");
            string outDir = Path.Combine(encDir, "Client");
            if (!File.Exists(exe)) throw new Exception("Encoder not found: " + exe);
            if (!File.Exists(Path.Combine(encDir, "MainInfo.ini"))) throw new Exception("Encoder\\MainInfo.ini is missing.");
            if (!Directory.Exists(S.ClientDir)) throw new Exception("Client folder not found: " + S.ClientDir);
            if (FindProcs("main", S.ClientDir).Count > 0) throw new Exception("The game (main.exe) is running. Close it first.");
            KillProcsTree("InfoEncoder", encDir);

            DateTime start = DateTime.Now;
            Log("Running InfoEncoder...");
            // Launched like a double-click / PowerShell Start-Process (own console window), the way that is known to work.
            // It is closed as soon as ClientInfo.bmd has been rewritten.
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = encDir; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Normal;
            Process p = Process.Start(psi);
            if (p == null) throw new Exception("Could not start InfoEncoder.");
            string last = null; int stable = 0; bool ready = false;
            for (int t = 0; t < 90; t++)
            {
                Thread.Sleep(1000);
                string sig = OutputSignature(outDir, start);
                if (sig != null && sig == last) stable++; else stable = 0;
                last = sig;
                if (stable >= 2) { ready = true; break; }
                if (p.HasExited) { Thread.Sleep(1000); ready = OutputSignature(outDir, start) != null; break; }
                if (t % 10 == 9) Log("Waiting for the encoder... " + (t + 1) + " s");
            }
            // The "press any key" prompt belongs to a child cmd.exe, so the whole tree has to go or the window stays open
            try { if (!p.HasExited) KillTree(p.Id); } catch (Exception) { }
            KillProcsTree("InfoEncoder", encDir);
            if (!ready)
            {
                string bmd = Path.Combine(outDir, @"Data\Local\ClientInfo.bmd");
                Log("ClientInfo.bmd: " + (File.Exists(bmd) ? "last written " + File.GetLastWriteTime(bmd) : "missing"));
                throw new Exception("InfoEncoder did not write a new ClientInfo.bmd within 90 s.");
            }

            foreach (string rel in Outputs)
            {
                string src = Path.Combine(outDir, rel), dst = Path.Combine(S.ClientDir, rel);
                if (!File.Exists(src)) { Log("Encoder\\Client has no " + rel + " (skipped)"); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (File.Exists(dst) && !File.Exists(dst + ".orig")) File.Copy(dst, dst + ".orig");
                File.Copy(src, dst, true);
                Log("Client updated: " + rel);
            }
            foreach (string rel in new string[] { "main.exe", "Main.dll" })
                if (!File.Exists(Path.Combine(S.ClientDir, rel)))
                    throw new Exception(rel + " is in neither the Client folder nor Encoder\\Client. Copy it from the original encoder package into Encoder\\Client and try again.");
        }

        public void RebuildClient()
        {
            CheckPaths();
            Log("Server: " + S.ServerDir + "  Client: " + S.ClientDir + "  IP: " + S.Ip);
            ApplyIp(S.Ip);
            RunEncoder();
            S.LastEncodedIp = S.Ip; S.Save();
        }

        static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                using (Process t = Process.Start(psi)) t.WaitForExit(5000);
            }
            catch (Exception) { }
        }

        static void KillProcsTree(string name, string dir)
        {
            foreach (Process p in FindProcs(name, dir)) KillTree(p.Id);
        }

        static void KillProcs(string name, string dir)
        {
            foreach (Process p in FindProcs(name, dir))
            {
                try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
            }
        }

        // ---------- database ----------
        static string NewPw()
        {
            byte[] b = new byte[12];
            using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b);
            return Convert.ToBase64String(b).Replace('+', 'a').Replace('/', 'b').Replace('=', 'c');
        }

        void ExtractMariaZip()
        {
            string[] zips = Directory.GetFiles(Settings.BaseDir, "mariadb*.zip");
            if (zips.Length == 0) throw new Exception("MariaDB ZIP not found. Download the Windows ZIP (10.11) from https://mariadb.org/download and put it next to the manager; its file name must start with \"mariadb\".");
            string tmp = Path.Combine(DbDir, "_extract");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(DbDir);
            Log("Extracting " + Path.GetFileName(zips[0]) + " (this takes a minute)...");
            ZipFile.ExtractToDirectory(zips[0], tmp);
            string root = null;
            if (File.Exists(Path.Combine(tmp, "bin", "mysql_install_db.exe"))) root = tmp;
            else foreach (string d in Directory.GetDirectories(tmp))
                    if (File.Exists(Path.Combine(d, "bin", "mysql_install_db.exe"))) { root = d; break; }
            if (root == null) throw new Exception("Unexpected MariaDB ZIP layout (bin\\mysql_install_db.exe not found).");
            if (Directory.Exists(MariaDir)) Directory.Delete(MariaDir, true);
            Directory.Move(root, MariaDir);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }

        string FindSql(string name)
        {
            // MySQL\DB first (always the MySQL version); the plain DB folder may hold the SQL Server version
            string[] cands = new string[] { Path.Combine(S.ServerDir, "MySQL", "DB", name), Path.Combine(DbDir, name) };
            foreach (string c in cands)
                if (File.Exists(c) && File.ReadAllText(c).Contains("`")) return c;
            throw new Exception("MySQL version of " + name + " not found (looked in MySQL\\DB and DB).");
        }

        Process StartMariaDb()
        {
            ProcessStartInfo psi = new ProcessStartInfo(Bin("mariadbd.exe"), "--defaults-file=\"" + Path.Combine(DataDir, "my.ini") + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = MariaDir;
            return Process.Start(psi);
        }

        void InitData()
        {
            rootPw = NewPw(); userPw = NewPw();
            string o;
            int code = RunTool(Bin("mysql_install_db.exe"),
                string.Format("--datadir=\"{0}\" --port={1} --password={2}", DataDir, S.DbPort, rootPw), null, MariaDir, out o);
            if (code != 0) throw new Exception("mysql_install_db failed: " + o.Trim());
            string ini = Path.Combine(DataDir, "my.ini");   // listen on 127.0.0.1 only
            string txt = File.ReadAllText(ini);
            txt = new Regex(@"\[mysqld\]").Replace(txt, "[mysqld]\r\nbind-address=127.0.0.1", 1);
            File.WriteAllText(ini, txt, Encoding.ASCII);
        }

        void LoadSql()
        {
            foreach (string name in new string[] { "CreateDatabase.sql", "PoblateDatabase.sql" })
            {
                string o;
                string path = FindSql(name).Replace('\\', '/');
                int code = RunTool(Bin("mariadb.exe"),
                    string.Format("--host=127.0.0.1 --port={0} --user=root --default-character-set=utf8mb4 -e \"source {1}\"", S.DbPort, path),
                    rootPw, MariaDir, out o);
                if (code != 0) throw new Exception(name + " failed: " + o.Trim());
                Log("Database: " + name + " loaded");
            }
            string grant = "CREATE USER 'mm'@'127.0.0.1' IDENTIFIED BY '" + userPw + "'; " +
                           "CREATE USER 'mm'@'localhost' IDENTIFIED BY '" + userPw + "'; " +
                           "GRANT ALL PRIVILEGES ON MuOnline97.* TO 'mm'@'127.0.0.1'; " +
                           "GRANT ALL PRIVILEGES ON MuOnline97.* TO 'mm'@'localhost'; FLUSH PRIVILEGES;";
            string o2;
            int c2 = RunTool(Bin("mariadb.exe"), string.Format("--host=127.0.0.1 --port={0} --user=root -e \"{1}\"", S.DbPort, grant), rootPw, MariaDir, out o2);
            if (c2 != 0) throw new Exception("User creation failed: " + o2.Trim());
            File.WriteAllText(Marker, "");
            File.WriteAllLines(CredFile, new string[] { "port=" + S.DbPort, "database=MuOnline97", "user=mm", "password=" + userPw, "root_password=" + rootPw });
            Log("Database ready (credentials saved in DB\\db-credentials.txt, do not share that file).");
        }

        void EnsureDb()
        {
            if (!File.Exists(Bin("mariadbd.exe"))) ExtractMariaZip();
            bool first = !File.Exists(Marker);
            if (IsListening(S.DbPort) && FindProcs("mariadbd", MariaDir).Count == 0)
                throw new Exception("Port " + S.DbPort + " is used by another program.");
            if (first)
            {
                if (Directory.Exists(DataDir)) throw new Exception(DataDir + " exists but is not initialized. Delete that folder and retry.");
                Log("MariaDB: first run, creating the database...");
                try { InitData(); }
                catch (Exception) { DiscardNewData(); throw; }
            }
            Process proc = null;
            if (!IsListening(S.DbPort)) { Log("MariaDB: starting..."); proc = StartMariaDb(); }
            if (!WaitPort("MariaDB", S.DbPort, 60, proc))
            {
                if (first) DiscardNewData();   // nothing usable yet: start clean next time
                throw new Exception("MariaDB did not start.");
            }
            Log("MariaDB: OK, port " + S.DbPort + " listening.");
            if (first)
            {
                try { LoadSql(); }
                catch (Exception) { DiscardNewData(); throw; }
            }
        }

        // Removes a half-created database so the next run starts clean (first run only)
        void DiscardNewData()
        {
            KillProcs("mariadbd", MariaDir);
            Thread.Sleep(2000);
            try { Directory.Delete(DataDir, true); } catch (Exception) { }
        }

        void StopMariaDb()
        {
            List<Process> list = FindProcs("mariadbd", MariaDir);
            if (list.Count == 0) { Log("MariaDB: not running."); return; }
            Log("MariaDB: shutting down...");
            string pw; ReadCreds().TryGetValue("root_password", out pw);
            try
            {
                string o;
                RunTool(Bin("mariadb-admin.exe"), string.Format("--host=127.0.0.1 --port={0} --user=root shutdown", S.DbPort), pw, MariaDir, out o);
            }
            catch (Exception) { }
            DateTime end = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < end && AnyAlive(list)) Thread.Sleep(500);
            KillProcs("mariadbd", MariaDir);
        }

        // ---------- servers ----------
        void EnsureMySqlVariant()
        {
            foreach (string n in new string[] { "DataServer", "JoinServer" })
            {
                string dst = Path.Combine(S.ServerDir, n), src = Path.Combine(S.ServerDir, "MySQL", n);
                if (Directory.Exists(dst) && Directory.GetFiles(dst, "mysqlcppconn*.dll").Length > 0) continue;   // already the MySQL variant
                if (!Directory.Exists(src)) throw new Exception(n + ": MySQL version not found (expected " + src + ").");
                if (FindProcs(n, S.ServerDir).Count > 0) throw new Exception(n + " is running with the old files. Stop it first.");
                Log(n + ": copying the MySQL version from MySQL\\" + n + " (existing files, if any, are backed up as *.orig)");
                Directory.CreateDirectory(dst);
                foreach (string f in Directory.GetFiles(src))
                {
                    string t = Path.Combine(dst, Path.GetFileName(f));
                    if (File.Exists(t) && !File.Exists(t + ".orig")) File.Copy(t, t + ".orig");
                    File.Copy(f, t, true);
                }
            }
        }

        static string SetKey(string text, string key, string val)
        {
            return Regex.Replace(text, "(?m)^" + key + "=[^\r\n]*", delegate(Match m) { return key + "=" + val; });
        }

        void PatchDbIni(string rel)
        {
            string f = Path.Combine(S.ServerDir, rel);
            if (!File.Exists(f)) throw new Exception("Missing " + f);
            Dictionary<string, string> c = ReadCreds();
            if (!c.ContainsKey("password") || !c.ContainsKey("user") || !c.ContainsKey("database")) throw new Exception("Database credentials are missing or incomplete (DB\\db-credentials.txt).");
            string t = File.ReadAllText(f);
            if (!Regex.IsMatch(t, "(?m)^DataBaseHost=")) throw new Exception(rel + " is not the MySQL version (no DataBaseHost).");
            if (!File.Exists(f + ".bak")) File.Copy(f, f + ".bak");
            t = SetKey(t, "DataBaseHost", "tcp://127.0.0.1");
            t = SetKey(t, "DataBasePort", S.DbPort.ToString());
            t = SetKey(t, "DataBaseUser", c["user"]);
            t = SetKey(t, "DataBasePass", c["password"]);
            t = SetKey(t, "DataBaseName", c["database"]);
            File.WriteAllText(f, t, new UTF8Encoding(false));
        }

        void StartComponent(Comp c)
        {
            string dir = Path.Combine(S.ServerDir, c.Name);
            string exe = Path.Combine(dir, c.Name + ".exe");
            if (!File.Exists(exe)) throw new Exception("Missing " + exe);
            List<Process> mine = FindProcs(c.Name, S.ServerDir);
            bool listening = IsListening(c.Port);
            if (listening && mine.Count == 0)
                throw new Exception("Port " + c.Port + " is already used by another program (maybe another server instance is running). Stop it first.");
            if (listening) { Log(c.Name + ": already running."); return; }
            Starting = c.Name;
            try
            {
                Process proc = null;
                if (mine.Count == 0)
                {
                    Log(c.Name + ": starting...");
                    ProcessStartInfo psi = new ProcessStartInfo(exe);
                    psi.WorkingDirectory = dir; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Minimized;
                    proc = Process.Start(psi);
                }
                else Log(c.Name + ": process found, waiting for its port...");
                if (!WaitPort(c.Name, c.Port, c.StartWait, proc)) throw new Exception("Start aborted: " + c.Name + " did not come up.");
                Log(c.Name + ": OK, port " + c.Port + " listening.");
            }
            finally { Starting = ""; }
        }

        public void StartAll(bool forceEncoder)
        {
            Preflight();
            CheckPaths();
            Log("Server: " + S.ServerDir + "  Client: " + S.ClientDir + "  IP: " + S.Ip);
            EnsureMySqlVariant();
            ApplyIp(S.Ip);
            bool needEnc = forceEncoder || S.Ip != S.LastEncodedIp ||
                           !File.Exists(Path.Combine(S.ClientDir, "Data", "Local", "ClientInfo.bmd"));
            if (needEnc) { RunEncoder(); S.LastEncodedIp = S.Ip; S.Save(); }
            else Log("Client already built for this IP (use Rebuild Client to force).");

            Starting = "MariaDB";
            try { EnsureDb(); } finally { Starting = ""; }
            PatchDbIni(@"DataServer\DataServer.ini");
            PatchDbIni(@"JoinServer\JoinServer.ini");
            foreach (Comp c in Servers) StartComponent(c);
            Log("All servers are up. You can open the client.");
        }

        void StopProcs(Comp c)
        {
            List<Process> list = FindProcs(c.Name, S.ServerDir);
            if (list.Count == 0) { Log(c.Name + ": not running."); return; }
            Log(c.Name + ": closing...");
            foreach (Process p in list) { try { p.CloseMainWindow(); } catch (Exception) { } }
            DateTime end = DateTime.Now.AddSeconds(c.StopWait);
            while (DateTime.Now < end && AnyAlive(list)) { ConfirmDialogs(list, c.Name); Thread.Sleep(500); }
            if (AnyAlive(list)) Log(c.Name + ": did not close in " + c.StopWait + " s, forcing.");
            KillProcs(c.Name, S.ServerDir);
        }

        public void StopAll()
        {
            for (int i = Servers.Length - 1; i >= 0; i--) StopProcs(Servers[i]);   // reverse of start order
            StopMariaDb();
            Log("All stopped.");
        }
    }

    // ---------- credits shown in the UI, About box and file properties ----------
    static class Credits
    {
        public const string Author = "90minutes";
        public const string Year = "2026";
        public const string Url = "https://www.youtube.com/@90minu93";
        public const string Product = "MMServer Manager";
        public const string Version = "1.0";   // shown as v1.0 in the title; also used for the exe file version
        public static string Title { get { return Product + " v" + Version; } }
        public static string Line { get { return "\u00A9 " + Year + " " + Author; } }
    }

    // ---------- theme ----------
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(14, 20, 32);
        public static readonly Color Panel = Color.FromArgb(23, 32, 51);
        public static readonly Color Panel2 = Color.FromArgb(18, 26, 42);
        public static readonly Color Input = Color.FromArgb(11, 16, 26);
        public static readonly Color Border = Color.FromArgb(40, 54, 82);
        public static readonly Color Text = Color.FromArgb(230, 237, 247);
        public static readonly Color Muted = Color.FromArgb(139, 152, 173);
        public static readonly Color Accent = Color.FromArgb(45, 140, 255);
        public static readonly Color Danger = Color.FromArgb(196, 61, 76);
        public static readonly Color Neutral = Color.FromArgb(46, 62, 94);
        public static readonly Color Ok = Color.FromArgb(46, 204, 113);
        public static readonly Color Warn = Color.FromArgb(245, 166, 35);
        public static readonly Color Off = Color.FromArgb(92, 104, 122);

        public static Color Shade(Color c, int delta)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, c.R + delta)), Math.Max(0, Math.Min(255, c.G + delta)), Math.Max(0, Math.Min(255, c.B + delta)));
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = radius * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        // Used when Assets\logo.png is not present
        public static Image FallbackLogo()
        {
            Bitmap bmp = new Bitmap(128, 128);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (GraphicsPath gp = RoundRect(new Rectangle(4, 4, 120, 120), 24))
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, 128, 128), Color.FromArgb(45, 140, 255), Color.FromArgb(15, 60, 140), 45f))
                    g.FillPath(b, gp);
                using (Font f = new Font("Segoe UI", 44f, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("MM", f, Brushes.White, new RectangleF(0, 0, 128, 128), sf);
                }
            }
            return bmp;
        }
    }

    // ---------- languages (UI text only; the log stays in English so it is easy to share for support) ----------
    static class Loc
    {
        public static string Current = "en";
        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>();
        static void A(string key, string en, string vi) { D[key] = new string[] { en, vi }; }

        static Loc()
        {
            A("subtitle", "Portable server manager", "Tr\u00ECnh qu\u1EA3n l\u00FD server portable");
            A("server", "Server folder", "Th\u01B0 m\u1EE5c server");
            A("client", "Client folder", "Th\u01B0 m\u1EE5c client");
            A("ip", "LAN IP", "IP m\u1EA1ng LAN");
            A("browse", "Browse", "Ch\u1ECDn...");
            A("detect", "Detect", "D\u00F2 IP");
            A("start", "Start All", "B\u1EADt t\u1EA5t c\u1EA3");
            A("stop", "Stop All", "T\u1EAFt t\u1EA5t c\u1EA3");
            A("rebuild", "Rebuild Client", "T\u1EA1o l\u1EA1i Client");
            A("open", "Open Client", "M\u1EDF Client");
            A("check", "Check System", "Ki\u1EC3m tra h\u1EC7 th\u1ED1ng");
            A("status", "Status", "Tr\u1EA1ng th\u00E1i");
            A("log", "Log", "Nh\u1EADt k\u00FD");
            A("lang", "Language", "Ng\u00F4n ng\u1EEF");
            A("about", "About", "Gi\u1EDBi thi\u1EC7u");
            A("running", "Running (port {0})", "\u0110ang ch\u1EA1y (c\u1ED5ng {0})");
            A("starting", "Starting...", "\u0110ang kh\u1EDFi \u0111\u1ED9ng...");
            A("stopped", "Stopped", "\u0110\u00E3 d\u1EEBng");
            A("busy", "A task is still running. Wait for it to finish.", "M\u1ED9t t\u00E1c v\u1EE5 \u0111ang ch\u1EA1y. H\u00E3y ch\u1EDD n\u00F3 ho\u00E0n t\u1EA5t.");
            A("stopask", "Servers are still running. Stop them now?\n\nYes = stop all and exit\nNo = leave them running and exit",
                         "Server v\u1EABn \u0111ang ch\u1EA1y. T\u1EAFt ch\u00FAng b\u00E2y gi\u1EDD?\n\nYes = t\u1EAFt t\u1EA5t c\u1EA3 r\u1ED3i tho\u00E1t\nNo = \u0111\u1EC3 ch\u1EA1y v\u00E0 tho\u00E1t");
            A("aboutCredits", "Server source: MuEmu 0.97k by Kayito (nicomuratona)\nDatabase: MariaDB (GPLv2) - mariadb.org",
                              "M\u00E3 ngu\u1ED3n server: MuEmu 0.97k c\u1EE7a Kayito (nicomuratona)\nC\u01A1 s\u1EDF d\u1EEF li\u1EC7u: MariaDB (GPLv2) - mariadb.org");
            A("aboutNote", "All trademarks belong to their owners. This is an unofficial tool and is not affiliated with any game publisher.",
                           "M\u1ECDi nh\u00E3n hi\u1EC7u thu\u1ED9c v\u1EC1 ch\u1EE7 s\u1EDF h\u1EEFu t\u01B0\u01A1ng \u1EE9ng. \u0110\u00E2y l\u00E0 c\u00F4ng c\u1EE5 kh\u00F4ng ch\u00EDnh th\u1EE9c, kh\u00F4ng li\u00EAn k\u1EBFt v\u1EDBi b\u1EA5t k\u1EF3 nh\u00E0 ph\u00E1t h\u00E0nh game n\u00E0o.");
            A("aboutLicense", "Open source under the MIT License. See Docs\\LICENSE for details.",
                              "M\u00E3 ngu\u1ED3n m\u1EDF theo gi\u1EA5y ph\u00E9p MIT. Xem Docs\\LICENSE \u0111\u1EC3 bi\u1EBFt chi ti\u1EBFt.");
        }

        public static string T(string key)
        {
            string[] v;
            if (!D.TryGetValue(key, out v)) return key;
            return Current == "vi" ? v[1] : v[0];
        }
    }

    class Card : Panel
    {
        public bool Bordered = true;
        public Card() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Bordered) using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    class Light : Control
    {
        Color color = Theme.Off;
        public Light()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Size = new Size(20, 20);
        }
        public void SetColor(Color c) { if (c != color) { color = c; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(55, color))) e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(color)) e.Graphics.FillEllipse(b, 4, 4, Width - 9, Height - 9);
        }
    }

    class MainForm : Form
    {
        Settings S; Core core;
        Card header, cardFolders, cardButtons, cardStatus, footer;
        PictureBox picLogo;
        Image logoImg, headerImg;
        Label lblTitle, lblSub, lblServer, lblClient, lblIp, lblStatus, lblLog, lblLang;
        TextBox txtServer, txtClient, txtLog;
        ComboBox cmbIp, cmbLang;
        Button btnStart, btnStop, btnRebuild, btnOpen, btnCheck, btnBrowseS, btnBrowseC, btnDetect;
        LinkLabel linkCopy, linkAbout;
        string[] names = new string[] { "MariaDB", "DataServer", "JoinServer", "ConnectServer", "GameServer" };
        Light[] lights = new Light[5];
        Label[] states = new Label[5];
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        volatile bool busy;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        static void DarkTitleBar(IntPtr h)
        {
            try { int on = 1; if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); }
            catch (Exception) { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitleBar(Handle);
        }

        // ----- small builders -----
        Label MakeLabel(Control parent, string text, int x, int y, int w, int h, Color fore, Font font)
        {
            Label l = new Label(); l.Text = text; l.SetBounds(x, y, w, h); l.ForeColor = fore; l.BackColor = Color.Transparent;
            if (font != null) l.Font = font;
            parent.Controls.Add(l); return l;
        }

        TextBox MakeBox(Control parent, int x, int y, int w)
        {
            TextBox t = new TextBox(); t.SetBounds(x, y, w, 26); t.BackColor = Theme.Input; t.ForeColor = Theme.Text; t.BorderStyle = BorderStyle.FixedSingle;
            t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; parent.Controls.Add(t); return t;
        }

        Button MakeButton(Control parent, string text, int x, int y, int w, int h, Color back, EventHandler click)
        {
            Button b = new Button();
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = Color.White; b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = Theme.Shade(back, 22); b.FlatAppearance.MouseDownBackColor = Theme.Shade(back, -18);
            b.Font = new Font("Segoe UI Semibold", 9.5f); b.Text = text; b.SetBounds(x, y, w, h);
            b.Click += click; parent.Controls.Add(b); return b;
        }

        // If logo.png has no real transparency and a black background, turn the black into transparency
        // (alpha = brightness, so neon glow edges stay soft). Logos that already have transparency are left alone.
        static Image KeyOutBlack(Image img)
        {
            Bitmap bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[Math.Abs(bd.Stride) * bmp.Height];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bool opaque = true;
            for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 255) { opaque = false; break; }
            if (opaque && buf[0] < 20 && buf[1] < 20 && buf[2] < 20)   // fully opaque with a black corner
            {
                for (int i = 0; i < buf.Length; i += 4)
                {
                    int m = Math.Max(buf[i + 2], Math.Max(buf[i + 1], buf[i]));   // pixels are stored as B, G, R, A
                    int a = m <= 8 ? 0 : Math.Min(255, (m - 8) * 255 / 247);
                    if (a > 0) { buf[i] = (byte)Math.Min(255, buf[i] * 255 / m); buf[i + 1] = (byte)Math.Min(255, buf[i + 1] * 255 / m); buf[i + 2] = (byte)Math.Min(255, buf[i + 2] * 255 / m); }
                    buf[i + 3] = (byte)a;
                }
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }

        static Image LoadImage(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (Image img = Image.FromStream(fs)) return new Bitmap(img);   // copy, so the file is not locked
        }

        void LoadAssets()
        {
            string dir = Path.Combine(Settings.BaseDir, "Assets");
            try { string f = Path.Combine(dir, "icon.ico"); if (File.Exists(f)) Icon = new Icon(f); } catch (Exception) { }
            try { string f = Path.Combine(dir, "logo.png"); if (File.Exists(f)) logoImg = KeyOutBlack(LoadImage(f)); } catch (Exception) { }
            try { string f = Path.Combine(dir, "header.png"); if (File.Exists(f)) headerImg = LoadImage(f); } catch (Exception) { }
            if (logoImg == null) logoImg = Theme.FallbackLogo();
        }

        public MainForm()
        {
            S = Settings.Load();
            Loc.Current = S.Lang;
            Text = Credits.Title;
            ClientSize = new Size(820, 724);
            MinimumSize = new Size(840, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            LoadAssets();

            // header
            header = new Card(); header.Bordered = false; header.SetBounds(0, 0, 820, 84);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Paint += PaintHeader; Controls.Add(header);
            picLogo = new PictureBox(); picLogo.Image = logoImg; picLogo.SizeMode = PictureBoxSizeMode.Zoom;
            picLogo.BackColor = Color.Transparent; picLogo.SetBounds(16, 10, 64, 64); header.Controls.Add(picLogo);
            lblTitle = MakeLabel(header, Credits.Title, 92, 14, 520, 30, Color.White, new Font("Segoe UI Semibold", 15f));
            lblSub = MakeLabel(header, "", 94, 46, 520, 22, Color.FromArgb(170, 205, 255), new Font("Segoe UI", 9.5f));
            lblLang = MakeLabel(header, "", 670, 12, 130, 20, Color.FromArgb(170, 205, 255), null);
            lblLang.Anchor = AnchorStyles.Top | AnchorStyles.Right; lblLang.TextAlign = ContentAlignment.MiddleRight;
            cmbLang = new ComboBox(); cmbLang.DropDownStyle = ComboBoxStyle.DropDownList; cmbLang.FlatStyle = FlatStyle.Flat;
            cmbLang.BackColor = Theme.Input; cmbLang.ForeColor = Theme.Text; cmbLang.SetBounds(660, 38, 140, 26);
            cmbLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cmbLang.Items.Add("English"); cmbLang.Items.Add("Ti\u1EBFng Vi\u1EC7t");
            cmbLang.SelectedIndex = S.Lang == "vi" ? 1 : 0;
            cmbLang.SelectedIndexChanged += delegate
            {
                S.Lang = cmbLang.SelectedIndex == 1 ? "vi" : "en"; Loc.Current = S.Lang; S.Save(); ApplyLanguage();
            };
            header.Controls.Add(cmbLang);

            // folders + IP
            cardFolders = new Card(); cardFolders.BackColor = Theme.Panel; cardFolders.SetBounds(16, 96, 788, 124);
            cardFolders.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(cardFolders);
            lblServer = MakeLabel(cardFolders, "", 14, 17, 112, 22, Theme.Muted, null);
            txtServer = MakeBox(cardFolders, 130, 14, 550);
            btnBrowseS = MakeButton(cardFolders, "", 690, 12, 84, 28, Theme.Neutral, delegate { Browse(txtServer); }); btnBrowseS.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblClient = MakeLabel(cardFolders, "", 14, 51, 112, 22, Theme.Muted, null);
            txtClient = MakeBox(cardFolders, 130, 48, 550);
            btnBrowseC = MakeButton(cardFolders, "", 690, 46, 84, 28, Theme.Neutral, delegate { Browse(txtClient); }); btnBrowseC.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblIp = MakeLabel(cardFolders, "", 14, 85, 112, 22, Theme.Muted, null);
            cmbIp = new ComboBox(); cmbIp.FlatStyle = FlatStyle.Flat; cmbIp.BackColor = Theme.Input; cmbIp.ForeColor = Theme.Text;
            cmbIp.SetBounds(130, 82, 220, 26); cardFolders.Controls.Add(cmbIp);
            btnDetect = MakeButton(cardFolders, "", 360, 80, 90, 28, Theme.Neutral, delegate { DetectIp(); });

            // action buttons
            cardButtons = new Card(); cardButtons.BackColor = Theme.Panel; cardButtons.SetBounds(16, 232, 788, 60);
            cardButtons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(cardButtons);
            btnStart = MakeButton(cardButtons, "", 14, 14, 144, 32, Theme.Accent, delegate { ReadUi(); RunBusy("Start All", delegate { core.StartAll(false); }); });
            btnStop = MakeButton(cardButtons, "", 168, 14, 144, 32, Theme.Danger, delegate { ReadUi(); RunBusy("Stop All", delegate { core.StopAll(); }); });
            btnRebuild = MakeButton(cardButtons, "", 322, 14, 144, 32, Theme.Neutral, delegate { ReadUi(); RunBusy("Rebuild Client", delegate { core.RebuildClient(); }); });
            btnOpen = MakeButton(cardButtons, "", 476, 14, 144, 32, Theme.Neutral, delegate { OpenClient(); });
            btnCheck = MakeButton(cardButtons, "", 630, 14, 144, 32, Theme.Neutral, delegate { ReadUi(); RunBusy("Check System", delegate { core.Preflight(); core.CheckPaths(); core.Log("All checks passed."); }); });

            // status
            cardStatus = new Card(); cardStatus.BackColor = Theme.Panel; cardStatus.SetBounds(16, 304, 788, 170);
            cardStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(cardStatus);
            lblStatus = MakeLabel(cardStatus, "", 14, 8, 300, 22, Theme.Accent, new Font("Segoe UI Semibold", 10f));
            for (int i = 0; i < 5; i++)
            {
                lights[i] = new Light(); lights[i].Location = new Point(16, 36 + i * 26); cardStatus.Controls.Add(lights[i]);
                MakeLabel(cardStatus, names[i], 46, 37 + i * 26, 150, 22, Theme.Text, null);
                states[i] = MakeLabel(cardStatus, "", 200, 37 + i * 26, 360, 22, Theme.Muted, null);
            }

            // log
            lblLog = MakeLabel(this, "", 18, 482, 300, 20, Theme.Accent, new Font("Segoe UI Semibold", 10f));
            txtLog = new TextBox(); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9f); txtLog.BackColor = Theme.Input; txtLog.ForeColor = Color.FromArgb(190, 210, 235);
            txtLog.BorderStyle = BorderStyle.FixedSingle; txtLog.SetBounds(16, 504, 788, 174);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(txtLog);
            txtLog.HandleCreated += delegate { try { SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null); } catch (Exception) { } };

            // footer with copyright
            footer = new Card(); footer.Bordered = false; footer.BackColor = Theme.Panel2; footer.SetBounds(0, 688, 820, 36);
            footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(footer);
            linkCopy = new LinkLabel(); linkCopy.Text = Credits.Line + "  \u00B7  youtube.com/@90minu93"; linkCopy.SetBounds(16, 8, 460, 20);
            StyleLink(linkCopy); linkCopy.LinkClicked += delegate { OpenUrl(Credits.Url); }; footer.Controls.Add(linkCopy);
            linkAbout = new LinkLabel(); linkAbout.SetBounds(690, 8, 114, 20); linkAbout.TextAlign = ContentAlignment.MiddleRight;
            linkAbout.Anchor = AnchorStyles.Top | AnchorStyles.Right; StyleLink(linkAbout);
            linkAbout.LinkClicked += delegate { ShowAbout(); }; footer.Controls.Add(linkAbout);

            txtServer.Text = S.ServerDir; txtClient.Text = S.ClientDir; cmbIp.Text = S.Ip;
            if (S.Ip.Length == 0) DetectIp();
            core = new Core(S, Log);
            ApplyLanguage();
            Log("Manager folder: " + Settings.BaseDir);
            Log("Press Check System first, then Start All.");

            timer.Interval = 1500; timer.Tick += delegate { RefreshLights(); }; timer.Start();
            FormClosing += OnClosing;
        }

        static void StyleLink(LinkLabel l)
        {
            l.BackColor = Color.Transparent; l.LinkColor = Theme.Accent; l.ActiveLinkColor = Theme.Shade(Theme.Accent, 40);
            l.VisitedLinkColor = Theme.Accent; l.LinkBehavior = LinkBehavior.HoverUnderline;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch (Exception) { }
        }

        void ApplyLanguage()
        {
            lblSub.Text = Loc.T("subtitle"); lblLang.Text = Loc.T("lang");
            lblServer.Text = Loc.T("server"); lblClient.Text = Loc.T("client"); lblIp.Text = Loc.T("ip");
            btnBrowseS.Text = Loc.T("browse"); btnBrowseC.Text = Loc.T("browse"); btnDetect.Text = Loc.T("detect");
            btnStart.Text = Loc.T("start"); btnStop.Text = Loc.T("stop"); btnRebuild.Text = Loc.T("rebuild");
            btnOpen.Text = Loc.T("open"); btnCheck.Text = Loc.T("check");
            lblStatus.Text = Loc.T("status"); lblLog.Text = Loc.T("log"); linkAbout.Text = Loc.T("about");
            RefreshLights();
        }

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics; Rectangle r = header.ClientRectangle;
            if (r.Width < 1 || r.Height < 1) return;
            if (headerImg != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float k = Math.Max((float)r.Width / headerImg.Width, (float)r.Height / headerImg.Height);   // keeps the proportions
                float w = headerImg.Width * k, h = headerImg.Height * k;
                g.DrawImage(headerImg, r.Width - w, (r.Height - h) / 2f, w, h);   // art stays on the right
            }
            else
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(10, 24, 52), Color.FromArgb(24, 86, 170), 0f))
                    g.FillRectangle(b, r);
            using (Pen p = new Pen(Theme.Accent, 2f)) g.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1);
        }

        void ShowAbout()
        {
            using (Form f = new Form())
            {
                f.Text = Loc.T("about"); f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false; f.Icon = Icon;
                f.ClientSize = new Size(500, 312); f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Font;
                PictureBox pb = new PictureBox(); pb.Image = logoImg; pb.SizeMode = PictureBoxSizeMode.Zoom; pb.SetBounds(20, 20, 72, 72); f.Controls.Add(pb);
                MakeLabel(f, Credits.Title, 104, 22, 380, 30, Color.White, new Font("Segoe UI Semibold", 13f));
                MakeLabel(f, Credits.Line, 106, 54, 380, 20, Theme.Muted, null);
                LinkLabel yt = new LinkLabel(); yt.Text = Credits.Url; yt.SetBounds(106, 76, 380, 20); StyleLink(yt);
                yt.LinkClicked += delegate { OpenUrl(Credits.Url); }; f.Controls.Add(yt);
                MakeLabel(f, Loc.T("aboutCredits") + "\n\n" + Loc.T("aboutNote") + "\n\n" + Loc.T("aboutLicense"), 20, 112, 460, 150, Theme.Text, null);
                MakeButton(f, "OK", 390, 266, 90, 32, Theme.Accent, delegate { f.Close(); });
                f.HandleCreated += delegate { DarkTitleBar(f.Handle); };   // same dark title bar as the main window
                f.ShowDialog(this);
            }
        }

        void Browse(TextBox box)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                if (Directory.Exists(box.Text)) d.SelectedPath = box.Text;
                if (d.ShowDialog() == DialogResult.OK) box.Text = d.SelectedPath;
            }
        }

        void DetectIp()
        {
            cmbIp.Items.Clear();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string a = ua.Address.ToString();
                        if (!a.StartsWith("169.254.")) cmbIp.Items.Add(a);
                    }
            }
            if (cmbIp.Text.Trim().Length == 0 && cmbIp.Items.Count > 0) cmbIp.SelectedIndex = 0;
        }

        void ReadUi()
        {
            S.ServerDir = txtServer.Text.Trim(); S.ClientDir = txtClient.Text.Trim(); S.Ip = cmbIp.Text.Trim();
            S.Save();
        }

        void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(new Action(delegate { AppendLog(line); })); }
            else AppendLog(line);
            try { File.AppendAllText(Path.Combine(Settings.BaseDir, "manager.log"), line + Environment.NewLine); } catch (Exception) { }
        }

        void AppendLog(string line) { txtLog.AppendText(line + Environment.NewLine); }

        void SetBusy(bool on)
        {
            foreach (Control c in new Control[] { btnStart, btnStop, btnRebuild, btnCheck, btnBrowseS, btnBrowseC, btnDetect, txtServer, txtClient, cmbIp })
                c.Enabled = !on;
        }

        void RunBusy(string title, Action work)
        {
            if (busy) return;
            busy = true; SetBusy(true); Log("== " + title + " ==");
            Thread t = new Thread(delegate()
            {
                try { work(); Log(title + ": finished."); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally
                {
                    busy = false;
                    if (IsHandleCreated) BeginInvoke(new Action(delegate { SetBusy(false); }));
                }
            });
            t.IsBackground = true; t.Start();
        }

        void OpenClient()
        {
            ReadUi();
            string exe = Path.Combine(S.ClientDir, "main.exe");
            if (!File.Exists(exe)) { Log("main.exe not found in " + S.ClientDir); return; }
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = S.ClientDir;
            try { Process.Start(psi); Log("Client started."); }
            catch (Exception ex) { Log("Could not start the client: " + ex.Message); }
        }

        void RefreshLights()
        {
            UpdateLight(0, S.DbPort);
            for (int i = 0; i < Core.Servers.Length; i++) UpdateLight(i + 1, Core.Servers[i].Port);
        }

        void UpdateLight(int i, int port)
        {
            bool on = Core.IsListening(port);
            bool starting = core != null && core.Starting == names[i];
            lights[i].SetColor(on ? Theme.Ok : (starting ? Theme.Warn : Theme.Off));
            states[i].ForeColor = on ? Theme.Ok : (starting ? Theme.Warn : Theme.Muted);
            states[i].Text = on ? string.Format(Loc.T("running"), port) : (starting ? Loc.T("starting") : Loc.T("stopped"));
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy) { MessageBox.Show(Loc.T("busy")); e.Cancel = true; return; }
            bool any = Core.IsListening(S.DbPort);
            foreach (Comp c in Core.Servers) if (Core.IsListening(c.Port)) any = true;
            if (!any) return;
            DialogResult r = MessageBox.Show(Loc.T("stopask"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes)
            {
                Cursor = Cursors.WaitCursor;
                try { core.StopAll(); } catch (Exception) { }
            }
        }
    }
}
