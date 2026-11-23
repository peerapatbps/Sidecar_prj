using System.Runtime.InteropServices;

namespace TruckSidecar.Ui;

/// <summary>
/// เบลอพื้นหลังหน้าต่างบน Windows 10/11 ด้วย SetWindowCompositionAttribute (undocumented แต่ใช้กันทั่วไป)
///   blur    = ACCENT_ENABLE_BLURBEHIND  — ลื่นสุดบน Win10 ตอนหน้าต่างขยับตามโปรแกรมชั่ง (ค่าเริ่มต้น)
///   acrylic = ACCENT_ENABLE_ACRYLICBLURBEHIND — สวยกว่าแต่ Win10 หน่วงเวลาลาก/ย้ายหน้าต่าง
///   off     = ไม่เบลอ (พื้นทึบไล่สี) ใช้เมื่อเครื่องไม่รองรับหรือช้า
/// </summary>
internal static class GlassBackdrop
{
    enum AccentState { Disabled = 0, BlurBehind = 3, AcrylicBlurBehind = 4 }

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy { public AccentState State; public int Flags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttribData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    /// <returns>true = เปิดเบลอสำเร็จ (พื้นหลังควรโปร่ง), false = ใช้พื้นทึบ</returns>
    public static bool Apply(IntPtr hwnd, string mode)
    {
        var state = mode.Trim().ToLowerInvariant() switch
        {
            "acrylic" => AccentState.AcrylicBlurBehind,
            "blur" => AccentState.BlurBehind,
            _ => AccentState.Disabled,
        };
        if (state == AccentState.Disabled) return false;

        // สี tint ของ acrylic เป็น AABBGGRR — ขาวอมฟ้าโปร่ง
        var policy = new AccentPolicy { State = state, Flags = 0, GradientColor = 0x66FAF5F2 };
        int size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttribData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
