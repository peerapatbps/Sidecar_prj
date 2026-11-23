using System.Windows;
using TruckSidecar.Ui;

namespace TruckSidecar;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var cfg = AppConfig.Load(Path.Combine(baseDir, "config.json"));

        if (args.Length >= 1 && args[0] == "--aqtest")
        {
            AqSelfTest.Run(cfg, args.Length >= 2 ? args[1] : null);
            return;
        }

        // --cctest out.txt <TICKET1 ALUM ที่ยังไม่มีบันทึก> : ทดสอบ crosscheck กฎข้อ 5 กับ mdb ตาม config
        if (args.Length >= 3 && args[0] == "--cctest")
        {
            CrossCheckSelfTest.Run(cfg, args[1], args[2]);
            return;
        }

        // --storetest <dataDir> <out.txt> : เปิด RecordStore บน dataDir (นำเข้า records\*.json ถ้ามี) + ทดสอบบันทึก/เปิดใหม่
        if (args.Length >= 3 && args[0] == "--storetest")
        {
            StoreSelfTest.Run(args[1], args[2]);
            return;
        }

        // --preview <โฟลเดอร์> : เรนเดอร์ทุกแท็บเป็น tab1.png, tab2.png, ... แล้วออก (ไม่เกาะโปรแกรมเดิม ไม่บันทึกข้อมูล)
        string? preview = args.Length >= 2 && args[0] == "--preview" ? Path.GetFullPath(args[1]) : null;

        using var mutex = new Mutex(true, "TruckSidecar.SingleInstance", out bool first);
        if (!first && preview == null) return;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/TruckSidecar;component/Ui/Glass.xaml"),
        });
        app.DispatcherUnhandledException += (_, e) =>
        {
            // อย่าให้ sidecar ล้มเพราะข้อผิดพลาดชั่วคราว (ข้อมูลที่พิมพ์ค้างจะหาย) — จดลง error.log แทน
            try
            {
                var dir = Path.Combine(baseDir, "data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:O} {e.Exception}{Environment.NewLine}");
            }
            catch { }
            e.Handled = true;
        };

        var controller = new SidecarController(cfg, baseDir);
        var win = new MainWindow(controller, preview);
        if (preview != null) win.Show();
        app.Run();
    }
}
