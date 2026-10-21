using System.Runtime.InteropServices;
using EasyBlue.Core;
namespace EasyBlue.Desktop;
internal static class BluetoothNames
{
    [StructLayout(LayoutKind.Sequential)] private struct Search
    {public uint Size;public int Authenticated,Remembered,Unknown,Connected,Inquiry;public byte Timeout;public nint Radio;}
    [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=560)] private struct Device
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(8)] public ulong Address;
        [FieldOffset(64),MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)] public string Name;
    }
    [DllImport("bthprops.cpl",CharSet=CharSet.Unicode)]private static extern nint BluetoothFindFirstDevice(ref Search search,ref Device device);
    [DllImport("bthprops.cpl",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool BluetoothFindNextDevice(nint find,ref Device device);
    [DllImport("bthprops.cpl")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool BluetoothFindDeviceClose(nint find);
    public static Dictionary<string,string> ReadCached()
    {
        // Cache only: never starts an inquiry or pairs through the PC's Bluetooth radio.
        var search=new Search{Size=(uint)Marshal.SizeOf<Search>(),Authenticated=1,Remembered=1,Unknown=1,Connected=1};
        var device=new Device{Size=560,Name=""};var result=new Dictionary<string,string>();
        nint handle=BluetoothFindFirstDevice(ref search,ref device);if(handle==0)return result;
        try{do{if(!string.IsNullOrWhiteSpace(device.Name))result[DeviceProtocol.AddressId(BitConverter.GetBytes(device.Address).Take(6).ToArray())]=device.Name.Trim();device.Size=560;}while(BluetoothFindNextDevice(handle,ref device));}
        finally{BluetoothFindDeviceClose(handle);}return result;
    }
}
