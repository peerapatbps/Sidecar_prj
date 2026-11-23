// cmd-mock.exe — ตัวเปิด cmd-mock.bat ที่มีไอคอนเหมือนโปรแกรมชั่ง (TRUCK93A\1.exe)
// .bat ใส่ไอคอนเองไม่ได้ จึงใช้ exe ตัวเล็กนี้แทน: เรียก elevate.ps1 (ซ่อนหน้าต่าง) ให้เปิด cmd-mock.bat ด้วยสิทธิ์ admin
// build: launcher\build.bat (csc ของ .NET Framework 4 ที่มากับ Windows — ไม่ต้องลงอะไรเพิ่ม)
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class CmdMockLauncher
{
    [STAThread]
    static void Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string ps1 = Path.Combine(dir, "elevate.ps1");
        if (!File.Exists(ps1))
        {
            MessageBox.Show("ไม่พบ " + ps1, "cmd-mock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        // elevate.ps1 ใช้บัญชี Administrator ที่จำไว้ (ถามรหัสครั้งแรกครั้งเดียว) → ไม่เด้ง UAC ทุกครั้ง
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir
        });
    }
}
