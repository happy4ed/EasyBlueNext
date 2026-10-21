using System.IO;
using EasyBlue.Core;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace EasyBlue.Desktop;
internal static class StartupShortcut
{
    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),StartupShortcutSpec.FileName);
    private static object Create()=>Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"),true)!)!;
    public static void Write(string executable)
    {
        string directory=Path.GetDirectoryName(FilePath)!;Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,"EasyBlue-"+Guid.NewGuid().ToString("N")+".tmp");
        object? value=null;
        try
        {
            value=Create();var link=(IShellLinkW)value;link.SetPath(executable);link.SetArguments(StartupShortcutSpec.Arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);link.SetDescription("EasyBlue Next · 로그인 시 트레이에서 실행");link.SetIconLocation(executable,0);link.SetShowCmd(7);
            ((IPersistFile)value).Save(temporary,true);File.Move(temporary,FilePath,true);
        }
        finally{if(value!=null)Marshal.FinalReleaseComObject(value);if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static (string Target,string Arguments,string Directory) Read()
    {
        object? value=null;
        try
        {
            value=Create();((IPersistFile)value).Load(FilePath,0);var link=(IShellLinkW)value;
            var target=new StringBuilder(32768);var arguments=new StringBuilder(32768);var directory=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,0,4);link.GetArguments(arguments,arguments.Capacity);link.GetWorkingDirectory(directory,directory.Capacity);
            return(target.ToString(),arguments.ToString(),directory.ToString());
        }
        finally{if(value!=null)Marshal.FinalReleaseComObject(value);}
    }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,nint findData,uint flags);
        void GetIDList(out nint pidl);void SetIDList(nint pidl);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);void SetHotkey(short hotkey);void GetShowCmd(out int command);void SetShowCmd(int command);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,uint reserved);void Resolve(nint window,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
