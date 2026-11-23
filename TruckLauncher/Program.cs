using System.Diagnostics;
using System.Text.Json;

namespace TruckLauncher;

// ตัวเปิดโปรแกรม: หน้าตา/ไอคอนเหมือนโปรแกรมชั่งเดิม พนักงานคลิกไอคอนที่คุ้นเคย
// แล้วมันเปิด "โปรแกรมชั่งเดิม + sidecar" พร้อมกัน จากนั้นตัวมันเองก็จบการทำงาน (หายไป)
// จุดประสงค์: บังคับให้ sidecar เปิดคู่กับโปรแกรมชั่งเสมอ
internal static class Program
{
    sealed class LauncherConfig
    {
        public string TruckExe { get; set; } = "";
        public string SidecarExe { get; set; } = "";
        public string SidecarProcess { get; set; } = "TruckSidecar";
        public string WatchdogExe { get; set; } = "";
        public string WatchdogProcess { get; set; } = "TruckWatchdog";
        public string UroborosExe { get; set; } = "";
        public string UroborosProcess { get; set; } = "Uroboros.MiniUploader";
    }

    [STAThread]
    static void Main()
    {
        var baseDir = AppContext.BaseDirectory;
        LauncherConfig cfg;
        try
        {
            var json = File.ReadAllText(Path.Combine(baseDir, "launcher.json"));
            cfg = JsonSerializer.Deserialize<LauncherConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new LauncherConfig();
        }
        catch (Exception ex)
        {
            // ตั้งค่าผิดถึงจะแจ้ง นอกนั้นเงียบเพื่อให้ "หายไป" เหมือนเปิดโปรแกรมปกติ
            Warn("อ่าน launcher.json ไม่ได้: " + ex.Message);
            return;
        }

        StartTruck(cfg.TruckExe);
        StartSidecar(cfg.SidecarExe, cfg.SidecarProcess);
        StartWatchdog(cfg.WatchdogExe, cfg.WatchdogProcess);
        StartUroboros(cfg.UroborosExe, cfg.UroborosProcess);   // เปิดท้ายสุด: มัน requireAdministrator → UAC เด้ง (secure desktop) ไม่ให้ค้างตัวอื่น
        // จบทันที — ตัว launcher หายไป เหลือแต่โปรแกรมชั่ง + sidecar + watchdog + uroboros_mini
    }

    static void StartTruck(string path)
    {
        if (!TryResolve(path, out var full)) { Warn($"ไม่พบโปรแกรมชั่ง:\n{path}"); return; }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,   // VB6 อ่าน truck.ini จากโฟลเดอร์ตัวเอง
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Warn("เปิดโปรแกรมชั่งไม่ได้: " + ex.Message); }
    }

    static void StartSidecar(string path, string processName)
    {
        // sidecar มี single-instance อยู่แล้ว แต่เช็กก่อนเพื่อไม่ให้มีหน้าต่าง flash
        if (IsRunning(processName)) return;
        if (!TryResolve(path, out var full)) { Warn($"ไม่พบ sidecar:\n{path}\n\n(โปรแกรมชั่งยังเปิดได้ปกติ)"); return; }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,   // sidecar อ่าน config.json จากโฟลเดอร์ตัวเอง
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Warn("เปิด sidecar ไม่ได้: " + ex.Message); }
    }

    static void StartWatchdog(string path, string processName)
    {
        // เว้นว่าง = ไม่ใช้ watchdog; ถ้าเปิดอยู่แล้วก็ข้าม (watchdog เป็น single-instance อยู่แล้ว)
        if (string.IsNullOrWhiteSpace(path)) return;
        if (IsRunning(processName)) return;
        if (!TryResolve(path, out var full)) { Warn($"ไม่พบ watchdog:\n{path}\n\n(โปรแกรมชั่ง + sidecar ยังเปิดได้ปกติ)"); return; }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,   // watchdog อ่าน watchdog.json จากโฟลเดอร์ตัวเอง
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Warn("เปิด watchdog ไม่ได้: " + ex.Message); }
    }

    static void StartUroboros(string path, string processName)
    {
        // เว้นว่าง = ไม่เปิด uroboros_mini; ถ้าเปิดอยู่แล้วก็ข้าม (มี single-instance mutex อยู่แล้ว)
        if (string.IsNullOrWhiteSpace(path)) return;
        if (IsRunning(processName)) return;
        if (!TryResolve(path, out var full)) { Warn($"ไม่พบ uroboros_mini:\n{path}\n\n(โปรแกรมอื่นยังเปิดได้ปกติ)"); return; }
        try
        {
            // WinExe = ไม่มี cmd โผล่; UseShellExecute=true จำเป็นเพราะ uroboros requireAdministrator (ต้อง elevate ผ่าน UAC)
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,   // อ่าน config_/mini-uploader.json/credentials.json/token_store ข้างตัวเอง
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        // ผู้ใช้กด Cancel ที่ UAC → Win32Exception (1223) เงียบไว้ ไม่ต้องรบกวน (โปรแกรมอื่นเปิดไปแล้ว)
        catch (System.ComponentModel.Win32Exception) { }
        catch (Exception ex) { Warn("เปิด uroboros_mini ไม่ได้: " + ex.Message); }
    }

    static bool IsRunning(string processName)
    {
        try { return Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    static bool TryResolve(string path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(full);
    }

    static void Warn(string msg) =>
        System.Windows.Forms.MessageBox.Show(msg, "Truck Scale Software",
            System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
}
