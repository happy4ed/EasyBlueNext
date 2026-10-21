using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EasyBlue.Desktop;

public sealed record DiscoveredInterface(string Name, string Identity, string Path);

// Enumerates present interfaces only: does not open device handles or send commands.
internal static class HidDiscovery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid ClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern nint SetupDiGetClassDevsW(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint info);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint set);

    public static IReadOnlyList<DiscoveredInterface> Find()
    {
        HidD_GetHidGuid(out var guid);
        var set=SetupDiGetClassDevsW(ref guid,null,0,0x12);
        if(set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        List<DiscoveredInterface> found=[];
        try
        {
            for(uint index=0;;index++)
            {
                var data=new InterfaceData {Size=Marshal.SizeOf<InterfaceData>()};
                if(!SetupDiEnumDeviceInterfaces(set,0,ref guid,index,ref data))
                {
                    int error=Marshal.GetLastWin32Error();
                    if(error==259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set,ref data,0,0,out uint required,0);
                int sizeError=Marshal.GetLastWin32Error();
                if(sizeError!=122 || required<6 || required>1024*1024)
                    throw new Win32Exception(sizeError,"장치 인터페이스 크기를 읽지 못했습니다.");
                nint detail=Marshal.AllocHGlobal(checked((int)required));
                try
                {
                    // cbSize includes native alignment; DevicePath always starts at byte 4.
                    Marshal.WriteInt32(detail,nint.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetailW(set,ref data,detail,required,out _,0))
                    {
                        int error=Marshal.GetLastWin32Error();
                        if(error==1167 || error==433)continue; // unplugged during enumeration
                        throw new Win32Exception(error);
                    }
                    string path=Marshal.PtrToStringUni(detail+4) ?? "";
                    string lower=path.ToLowerInvariant();
                    if(!lower.Contains("vid_1d54&pid_b001") && !lower.Contains("vid_1d54&pid_b002")) continue;
                    string product=lower.Contains("pid_b002")?"B002":"B001";
                    found.Add(new("EasyBlue USB 인터페이스",$"VID 1D54 · PID {product}",path));
                }
                finally {Marshal.FreeHGlobal(detail);}
            }
        }
        finally {SetupDiDestroyDeviceInfoList(set);}
        return found;
    }
}
