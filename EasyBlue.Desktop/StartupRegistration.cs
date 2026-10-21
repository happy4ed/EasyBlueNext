using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using EasyBlue.Core;
namespace EasyBlue.Desktop;

internal sealed record StartupStatus(bool Enabled,bool Healthy,string Message);
internal static class StartupRegistration
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string SettingsKey=@"Software\EasyBlueNext";
    private static string Sid {get{using var identity=WindowsIdentity.GetCurrent();return identity.User?.Value??throw new IOException("사용자 SID를 읽지 못했습니다.");}}
    private static string TaskName=>"EasyBlueNext-"+Sid;
    private static string Executable=>Environment.ProcessPath??throw new IOException("앱 실행 경로를 찾지 못했습니다.");
    private static bool Missing(COMException ex)=>ex.HResult==unchecked((int)0x80070002)||ex.HResult==unchecked((int)0x8004130F);
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
    private static object Service()
    {
        var type=Type.GetTypeFromProgID("Schedule.Service")??throw new IOException("Windows 작업 스케줄러를 찾지 못했습니다.");
        dynamic service=Activator.CreateInstance(type)!;
        try{service.Connect();return service;}catch{Release(service);throw;}
    }
    private static bool PreviousTaskEnabled()
    {
        object? service=null,folder=null,task=null;
        try
        {
            service=Service();folder=((dynamic)service).GetFolder(@"\");task=((dynamic)folder).GetTask(TaskName);
            return (bool)((dynamic)task).Enabled;
        }
        catch{return false;}
        finally{Release(task);Release(folder);Release(service);}
    }
    private static void DeletePreviousTask()
    {
        using(var state=Registry.CurrentUser.OpenSubKey(SettingsKey))if((string?)state?.GetValue("StartupMode")=="Shortcut"&&state?.GetValue("StartupCleanupPending") is int pending&&pending==0)return;
        object? service=null,folder=null;
        try
        {
            service=Service();folder=((dynamic)service).GetFolder(@"\");
            try{((dynamic)folder).DeleteTask(TaskName,0);}catch(COMException ex)when(Missing(ex)){ }
        }
        finally{Release(folder);Release(service);}
    }
    public static Task<StartupStatus> Execute(Func<StartupStatus> action)
    {
        var result=new TaskCompletionSource<StartupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{result.TrySetResult(action());}catch(Exception ex){result.TrySetException(ex);}}){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    public static StartupStatus Read()
    {
        try
        {
            if(!File.Exists(StartupShortcut.FilePath))
            {
                bool previous=LegacyEnabled()||PreviousTaskEnabled();
                return new(previous,true,previous?"이전 자동실행 등록 · 복구를 눌러 시작프로그램 방식으로 변경하세요":"자동실행 꺼짐");
            }
            var entry=StartupShortcut.Read();
            bool valid=File.Exists(entry.Target)&&StartupShortcutSpec.Matches(Executable,entry.Target,entry.Arguments,entry.Directory);
            string message=valid?"시작프로그램 바로가기 등록됨 · 로그인 시 트레이 실행":"시작프로그램 실행 경로 또는 옵션 확인 필요 · 복구를 누르세요";
            using var state=Registry.CurrentUser.OpenSubKey(SettingsKey);
            if(state?.GetValue("StartupCleanupPending") is int pending&&pending==1)message+=" · 이전 예약 작업 정리 필요 · 복구를 눌러 주세요";
            return new(true,valid,message);
        }
        catch(Exception ex){return new(false,false,"시작프로그램 확인 실패: "+Error(ex));}
    }
    public static string Error(Exception ex)=>$"{ex.Message} (0x{ex.HResult:X8})";
    public static bool LegacyEnabled(){using var key=Registry.CurrentUser.OpenSubKey(RunKey);return key?.GetValue("EasyBlueNext") is string;}
    public static StartupStatus Enable()
    {
        if(!File.Exists(Executable))throw new FileNotFoundException("앱 실행파일을 찾지 못했습니다.",Executable);
        StartupShortcut.Write(Executable);
        var status=Read();if(!status.Enabled||!status.Healthy)throw new IOException("시작프로그램 바로가기를 검증하지 못했습니다. "+status.Message);
        string? cleanup=null;
        try{DeletePreviousTask();}catch(Exception ex){cleanup=" · 이전 예약 작업 정리 확인 필요: "+Error(ex);}
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))key?.DeleteValue("EasyBlueNext",false);
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey))
        {key.SetValue("StartupEnabled",1,RegistryValueKind.DWord);key.SetValue("StartupMode","Shortcut");key.SetValue("StartupCleanupPending",cleanup==null?0:1,RegistryValueKind.DWord);}
        if(cleanup!=null)status=status with{Message=status.Message+cleanup};
        Log(status.Message);return status;
    }
    public static StartupStatus Disable()
    {
        if(File.Exists(StartupShortcut.FilePath))File.Delete(StartupShortcut.FilePath);
        try{DeletePreviousTask();}catch(Exception ex){throw new IOException("바로가기는 제거했지만 이전 예약 작업의 해제를 확인하지 못했습니다: "+Error(ex),ex);}
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))key?.DeleteValue("EasyBlueNext",false);
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey))key.SetValue("StartupEnabled",0,RegistryValueKind.DWord);
        var status=Read();if(status.Enabled||!status.Healthy)throw new IOException("자동실행 해제 확인 실패: "+status.Message);
        Log(status.Message);return status;
    }
    public static volatile bool DiagnosticsEnabled;
    public static void Log(string message)
    {
        if(!DiagnosticsEnabled)return;
        try
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EasyBlueNext");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"startup.log");if(File.Exists(path)&&new FileInfo(path).Length>256*1024)File.Move(path,path+".previous",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch(IOException){ }catch(UnauthorizedAccessException){ }
    }
}
