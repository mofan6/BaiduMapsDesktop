using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace BaiduMapsDesktop
{
    // Persistent state is a single compressed, DPAPI-encrypted file. The complete
    // browser working profile is temporary; by default it uses LocalAppData.
    internal sealed class SessionStorage
    {
        private static readonly byte[] signature = Encoding.ASCII.GetBytes("BMDATA2\0");
        private static readonly byte[] entropy = Encoding.UTF8.GetBytes("BaiduMapsDesktop.state.v2");
        private const long MaximumArchiveBytes = 536870912;
        internal readonly string VaultPath, WorkingRoot, SessionRoot;
        private readonly string legacyRoot;
        private readonly string portableVault, portableWorkParent;
        private readonly HashSet<int> browserIds = new HashSet<int>();
        private FileStream sessionLock;
        private bool ready;

        internal SessionStorage(string vault, string workParent, string legacy, string portable = null, string oldWorkParent = null)
        {
            VaultPath = Path.GetFullPath(vault);
            legacyRoot = legacy == null ? null : Path.GetFullPath(legacy);
            portableVault = portable == null ? null : Path.GetFullPath(portable);
            portableWorkParent = oldWorkParent;
            string identity = WindowsIdentity.GetCurrent().User.Value + "|" + VaultPath.ToUpperInvariant();
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 24);
            WorkingRoot = Path.Combine(Path.GetFullPath(workParent), hash);
            SessionRoot = Path.Combine(WorkingRoot, "Session");
        }

        internal static string ChooseWorkParent(string executable, string requested, bool test)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                string path = Path.GetFullPath(requested);
                if (!Path.IsPathRooted(requested)) throw new ArgumentException("工作目录必须是绝对路径。");
                return path;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop", "Work");
        }

        internal void Open()
        {
            EnsurePrivateDirectory(WorkingRoot);
            sessionLock = new FileStream(Path.Combine(WorkingRoot, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                string marker = Path.Combine(SessionRoot, "session.info");
                string cleanup = Path.Combine(WorkingRoot, "cleanup.ready");
                if (Directory.Exists(SessionRoot) && !File.Exists(marker) && File.Exists(cleanup) && File.ReadAllText(cleanup) == VaultPath && File.Exists(VaultPath))
                {
                    ReadVault(VaultPath); // Only clear partially deleted state backed by a valid archive.
                    DeleteCheckedTree(SessionRoot, SessionRoot);
                }
                // A delayed cleanup from the previous process must not touch a
                // newly active session. The exclusive lock is already held.
                if (File.Exists(cleanup)) File.Delete(cleanup);
                if (Directory.Exists(SessionRoot) && File.Exists(marker))
                {
                    if (!string.Equals(File.ReadAllText(marker), VaultPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工作目录的资料标识不匹配。");
                    // Recover the freshest working state after a power loss/crash.
                    RejectReparsePoints(SessionRoot);
                    ready = true;
                    return;
                }
                if (Directory.Exists(SessionRoot)) throw new InvalidOperationException("工作目录存在未识别资料，未覆盖。请选择另一个工作目录。");
                Directory.CreateDirectory(SessionRoot);
                bool imported = false;
                string recovery = null;
                byte[] portableBytes = null;
                if (File.Exists(VaultPath)) RestoreArchive(ReadVault(VaultPath), SessionRoot);
                else if (portableVault != null && File.Exists(portableVault))
                {
                    portableBytes = File.ReadAllBytes(portableVault);
                    ReadVault(portableVault); // Verify DPAPI ownership before moving it.
                    recovery = FindPortableRecovery();
                    if (recovery != null) CopyPersistentSource(recovery);
                    else RestoreArchive(ReadVault(portableVault), SessionRoot);
                    // Preserve v1 data inside the new encrypted archive before
                    // retiring its caches, even if the newer profile differs.
                    if (legacyRoot != null && Directory.Exists(Path.Combine(legacyRoot, "WebView2")))
                        File.WriteAllBytes(Path.Combine(SessionRoot, "previous-profile.zip"), BuildArchive(legacyRoot));
                }
                else if (legacyRoot != null && Directory.Exists(legacyRoot)) { ImportLegacy(); imported = true; }
                File.WriteAllText(marker, VaultPath, Encoding.UTF8);
                ready = true;
                SaveVault(); // Validate destination and commit migration before opening Edge.
                if (imported || portableBytes != null) RetireLegacy();
                if (portableBytes != null)
                {
                    // A source changed by another application is never deleted.
                    if (SelfTests.SameHash(File.ReadAllBytes(portableVault), portableBytes)) File.Delete(portableVault);
                    if (recovery != null)
                    {
                        try { DeleteCheckedTree(Path.GetDirectoryName(recovery), Path.GetDirectoryName(recovery)); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            catch { ReleaseLock(); throw; }
        }

        private void ImportLegacy()
        {
            CopyPersistentSource(legacyRoot);
        }
        private void CopyPersistentSource(string source)
        {
            RejectReparsePoints(source);
            string oldLock = Path.Combine(source, "WebView2", "EBWebView", "lockfile");
            if (File.Exists(oldLock)) using (var handle = new FileStream(oldLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            foreach (string file in PersistentFiles(source))
            {
                string relative = Relative(source, file);
                string target = SafeTarget(SessionRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
        }
        private string FindPortableRecovery()
        {
            var candidates = new List<string>();
            if (portableWorkParent != null) candidates.Add(portableWorkParent);
            foreach (var drive in DriveInfo.GetDrives()) if (drive.DriveType == DriveType.Fixed) candidates.Add(Path.Combine(drive.Name, "BaiduMapsDesktop.Work"));
            foreach (string parent in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var old = new SessionStorage(portableVault, parent, null);
                string marker = Path.Combine(old.SessionRoot, "session.info");
                if (File.Exists(marker) && File.ReadAllText(marker).Equals(portableVault, StringComparison.OrdinalIgnoreCase)) return old.SessionRoot;
            }
            return null;
        }

        private void RetireLegacy()
        {
            // Only retire the exact v1 data folder after durable encrypted storage
            // has been verified. Never recursively remove a computed arbitrary path.
            string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop"));
            if (legacyRoot == null || !legacyRoot.Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                RejectReparsePoints(legacyRoot);
                // v1 and the new vault share LocalAppData now. Delete only the
                // known old entries, never the root containing state.data/Work.
                foreach (string name in new[] { "WebView2", "Runtime", "settings.json", "library.json", "diagnostics.log", "self-test-result.txt" })
                {
                    string target = Path.Combine(expected, name);
                    if (Directory.Exists(target)) DeleteCheckedTree(target, target);
                    else if (File.Exists(target)) File.Delete(target);
                }
            }
            catch (IOException) { Diagnostics.Write("legacy-folder-busy", "旧目录仍被占用，资料已迁移。请关闭旧版本。"); }
            catch (UnauthorizedAccessException) { Diagnostics.Write("legacy-folder-retained", "旧目录暂不能移除，资料已迁移。"); }
        }

        internal void RegisterBrowser(uint id) { if (id > 0 && id <= int.MaxValue) browserIds.Add((int)id); }
        internal bool Finish()
        {
            if (!ready) { ReleaseLock(); return false; }
            bool saved = false;
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(6);
                foreach (int id in browserIds)
                {
                    try
                    {
                        using (var process = Process.GetProcessById(id))
                        {
                            int milliseconds = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                            if (!process.HasExited && !process.WaitForExit(milliseconds)) return false;
                        }
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                // The browser must have flushed and released its databases first.
                string lockFile = Path.Combine(SessionRoot, "WebView2", "EBWebView", "lockfile");
                if (File.Exists(lockFile)) using (var handle = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                SaveVault();
                saved = true;
                File.WriteAllText(Path.Combine(WorkingRoot, "cleanup.ready"), VaultPath, Encoding.UTF8);
                DeleteCheckedTree(SessionRoot, SessionRoot);
                return true;
            }
            catch (Exception ex) { Diagnostics.Write("state-save-recovery", ex.GetType().Name + ": " + ex.Message); return false; }
            finally
            {
                ReleaseLock();
                if (saved && !Directory.Exists(SessionRoot))
                {
                    try
                    {
                        if (Directory.Exists(Path.Combine(WorkingRoot, "Runtime"))) ScheduleCleanup();
                        else CleanupEmptyWorkRoot();
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
        }

        private void CleanupEmptyWorkRoot()
        {
            File.Delete(Path.Combine(WorkingRoot, "session.lock"));
            File.Delete(Path.Combine(WorkingRoot, "cleanup.ready"));
            Directory.Delete(WorkingRoot, false);
            string parent = Path.GetDirectoryName(WorkingRoot);
            if (Path.GetFileName(parent) == "BaiduMapsDesktop.Work" && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent, false);
        }
        private void ScheduleCleanup()
        {
            // Windows keeps the loaded native DLL locked until this host exits.
            // A short-lived second invocation performs only guarded cleanup.
            using (var parent = Process.GetCurrentProcess())
            {
                string arguments = "--cleanup-parent=" + parent.Id + " --cleanup-start=" + parent.StartTime.ToUniversalTime().Ticks
                    + " " + QuoteArgument("--cleanup-vault=" + VaultPath) + " " + QuoteArgument("--cleanup-work=" + Path.GetDirectoryName(WorkingRoot));
                Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
        }
        internal static string QuoteArgument(string value)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                output.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); output.Append(c); slashes = 0;
            }
            output.Append('\\', slashes * 2); return output.Append('"').ToString();
        }
        internal static bool RunCleanup(string[] args)
        {
            string parentArg = Array.Find(args, a => a.StartsWith("--cleanup-parent=", StringComparison.Ordinal));
            if (parentArg == null) return false;
            try
            {
                int id = int.Parse(parentArg.Substring("--cleanup-parent=".Length));
                long ticks = long.Parse(Array.Find(args, a => a.StartsWith("--cleanup-start=", StringComparison.Ordinal)).Substring("--cleanup-start=".Length));
                string vault = Array.Find(args, a => a.StartsWith("--cleanup-vault=", StringComparison.Ordinal)).Substring("--cleanup-vault=".Length);
                string work = Array.Find(args, a => a.StartsWith("--cleanup-work=", StringComparison.Ordinal)).Substring("--cleanup-work=".Length);
                try { using (var parent = Process.GetProcessById(id)) if (parent.StartTime.ToUniversalTime().Ticks == ticks && !parent.WaitForExit(30000)) return true; } catch (ArgumentException) { }
                var storage = new SessionStorage(vault, work, null);
                using (var gate = new Mutex(false, @"Local\BaiduMapsDesktop.v1"))
                {
                    bool own = false;
                    try
                    {
                        try { own = gate.WaitOne(0); } catch (AbandonedMutexException) { own = true; }
                        if (Directory.Exists(storage.SessionRoot) || !Directory.Exists(storage.WorkingRoot)) return true;
                        string marker = Path.Combine(storage.WorkingRoot, "cleanup.ready");
                        if (!File.Exists(marker) || File.ReadAllText(marker) != storage.VaultPath || !File.Exists(storage.VaultPath)) return true;
                        RejectReparsePoints(storage.WorkingRoot);
                        using (var handle = new FileStream(Path.Combine(storage.WorkingRoot, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        {
                            if (Directory.Exists(storage.SessionRoot)) return true;
                            DeleteCheckedTree(Path.Combine(storage.WorkingRoot, "Runtime"), Path.Combine(storage.WorkingRoot, "Runtime"));
                        }
                        storage.CleanupEmptyWorkRoot();
                    }
                    finally { if (own) gate.ReleaseMutex(); }
                }
            }
            catch { } // A small locked loader can be reused at the next startup.
            return true;
        }

        private void ReleaseLock() { if (sessionLock != null) { sessionLock.Dispose(); sessionLock = null; } }
        private void SaveVault()
        {
            byte[] zip = BuildArchive(SessionRoot);
            byte[] protectedBytes = ProtectedData.Protect(zip, entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(VaultPath));
            string temp = VaultPath + "." + Process.GetCurrentProcess().Id + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(signature, 0, signature.Length);
                    stream.Write(protectedBytes, 0, protectedBytes.Length);
                    stream.Flush(true);
                }
                if (!SelfTests.SameHash(ReadVault(temp), zip)) throw new IOException("资料文件完整性验证失败。");
                if (File.Exists(VaultPath)) File.Replace(temp, VaultPath, null); else File.Move(temp, VaultPath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static byte[] ReadVault(string path)
        {
            var info = new FileInfo(path);
            if (info.Length <= signature.Length || info.Length > MaximumArchiveBytes) throw new InvalidDataException("资料文件长度不正确。");
            byte[] bytes = File.ReadAllBytes(path);
            if (!bytes.Take(signature.Length).SequenceEqual(signature)) throw new InvalidDataException("资料文件格式不正确。");
            return ProtectedData.Unprotect(bytes.Skip(signature.Length).ToArray(), entropy, DataProtectionScope.CurrentUser);
        }

        internal static bool KeepPath(string relative)
        {
            string[] parts = relative.Replace('\\', '/').Split('/');
            if (parts.Length == 1) return parts[0] == "settings.json" || parts[0] == "library.json" || parts[0] == "previous-profile.zip";
            if (parts[0] != "WebView2") return false;
            if (parts.Length > 2 && parts[1] == "EBWebView")
            {
                string component = parts[2];
                var disposableRoot = new[] { "BrowserMetrics", "Crashpad", "component_crx_cache", "extensions_crx_cache", "GPUCache", "GPUPersistentCache", "GrShaderCache", "ShaderCache", "DawnGraphiteCache", "DawnWebGPUCache", "BrowserMetrics-spare.pma", "CrashpadMetrics-active.pma", "lockfile" };
                if (disposableRoot.Contains(component, StringComparer.OrdinalIgnoreCase)) return false;
                bool profile = component.Equals("Default", StringComparison.OrdinalIgnoreCase) || component.StartsWith("WV2Profile_", StringComparison.OrdinalIgnoreCase) || component.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                if (profile && parts.Length > 3)
                {
                    var disposableProfile = new[] { "Cache", "Code Cache", "GPUCache", "GPUPersistentCache", "DawnGraphiteCache", "DawnWebGPUCache", "ShaderCache", "GrShaderCache", "blob_storage", "LOCK", "LOG", "LOG.old" };
                    if (disposableProfile.Contains(parts[3], StringComparer.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }

        private static IEnumerable<string> PersistentFiles(string root)
        {
            RejectReparsePoints(root);
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) if (KeepPath(Relative(root, file))) yield return file;
        }
        internal static byte[] BuildArchive(string root)
        {
            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                {
                    long total = 0;
                    foreach (string file in PersistentFiles(root))
                    {
                        total += new FileInfo(file).Length;
                        if (total > MaximumArchiveBytes) throw new IOException("网站持久资料超过 512 MB，工作目录保留以便恢复。");
                        var entry = zip.CreateEntry(Relative(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
                        using (var input = File.OpenRead(file)) using (var output = entry.Open()) input.CopyTo(output);
                    }
                }
                return buffer.ToArray();
            }
        }
        internal static void RestoreArchive(byte[] bytes, string root)
        {
            using (var buffer = new MemoryStream(bytes)) using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read))
            {
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    total += entry.Length;
                    if (total > MaximumArchiveBytes) throw new InvalidDataException("资料包展开过大。");
                    // Retired settings from the previous 1.4 build are ignored;
                    // all unrelated login/history files still restore normally.
                    if (entry.FullName == "weather.json") continue;
                    if (!KeepPath(entry.FullName)) throw new InvalidDataException("资料包包含非持久数据。");
                    string target = SafeTarget(root, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }

        private static string Relative(string root, string path) { return Path.GetFullPath(path).Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length + 1); }
        private static string SafeTarget(string root, string relative)
        {
            if (Path.IsPathRooted(relative) || relative.Contains(":") || relative.Replace('\\', '/').Split('/').Any(p => p == ".." || p.Length == 0)) throw new InvalidDataException("资料包路径不正确。");
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资料包路径越界。");
            return target;
        }
        private static void EnsurePrivateDirectory(string path)
        {
            Directory.CreateDirectory(path);
            RejectReparsePoints(path);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }
        private static void RejectReparsePoints(string root)
        {
            string current = Path.GetFullPath(root);
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("工作目录不能通过链接或挂载点重定向。");
                current = Path.GetDirectoryName(current);
            }
            if (!Directory.Exists(root)) return;
            CheckChildren(root);
        }
        private static void CheckChildren(string root)
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(root))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("资料目录中存在链接，已保留原资料。");
                if ((attributes & FileAttributes.Directory) != 0) CheckChildren(item);
            }
        }
        private static void DeleteCheckedTree(string target, string allowedRoot)
        {
            string absolute = Path.GetFullPath(target), allowed = Path.GetFullPath(allowedRoot);
            if (!absolute.Equals(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("清理目录不匹配。");
            RejectReparsePoints(absolute);
            Directory.Delete(absolute, true);
        }
    }
}
