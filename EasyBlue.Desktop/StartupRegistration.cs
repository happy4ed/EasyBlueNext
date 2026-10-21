using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using EasyBlue.Core;
using Microsoft.Win32;
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
    public static StartupStatus Read()
    {
        object? service=null,folder=null,task=null;
        try
        {
            service=Service();folder=((dynamic)service).GetFolder(@"\");
            try{task=((dynamic)folder).GetTask(TaskName);}catch(COMException ex)when(Missing(ex)){return new(false,true,LegacyEnabled()?"이전 자동실행 방식 · 복구를 눌러 전환하세요":"자동실행 꺼짐");}
            dynamic registered=task;
            bool enabled=registered.Enabled;
            var xml=XDocument.Parse((string)registered.Xml);XNamespace ns=xml.Root!.Name.Namespace;
            var exec=xml.Descendants(ns+"Exec").SingleOrDefault();
            bool path=string.Equals((string?)exec?.Element(ns+"Command"),Executable,StringComparison.OrdinalIgnoreCase)&&File.Exists(Executable);
            bool arguments=(string?)exec?.Element(ns+"Arguments")=="--background";
            bool trigger=xml.Descendants(ns+"LogonTrigger").Any(t=>(string?)t.Element(ns+"UserId")==Sid&&(string?)t.Element(ns+"Enabled")!="false");
            int state=registered.State;
            int result=registered.LastTaskResult;
            bool healthy=path&&arguments&&trigger&&state!=0;
            string details=!enabled?"자동실행 꺼짐 · 예약 작업 사용 안 함":!healthy?"자동실행 등록 확인 필요 · 복구를 누르세요":"로그인 10초 후 트레이 실행 · 사용자 예약 작업";
            // 0x41301 is running; 0x41303 has not run yet. Show other results for diagnosis.
            if(enabled&&result!=0&&result!=0x41301&&result!=0x41303)details+=$" · 최근 실행 결과 0x{result:X8}";
            return new(enabled,healthy,details);
        }
        catch(Exception ex){return new(false,false,"자동실행 상태 확인 실패: "+ex.Message);}
        finally{Release(task);Release(folder);Release(service);}
    }
    public static bool LegacyEnabled(){using var key=Registry.CurrentUser.OpenSubKey(RunKey);return key?.GetValue("EasyBlueNext") is string;}
    public static StartupStatus Enable()
    {
        object? service=null,folder=null,task=null;
        try
        {
            if(!File.Exists(Executable))throw new FileNotFoundException("앱 실행파일을 찾지 못했습니다.",Executable);
            service=Service();folder=((dynamic)service).GetFolder(@"\");
            // TASK_CREATE_OR_UPDATE=6; TASK_LOGON_INTERACTIVE_TOKEN=3. No elevation or password.
            task=((dynamic)folder).RegisterTask(TaskName,StartupTaskDefinition.Create(Executable,Sid),6,Sid,null,3,null);
        }
        finally{Release(task);Release(folder);Release(service);}
        var status=Read();
        if(!status.Enabled||!status.Healthy)throw new IOException("예약 작업 등록을 확인하지 못했습니다. "+status.Message);
        // Remove only our old registration, and only after the replacement has been verified.
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))key?.DeleteValue("EasyBlueNext",false);
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey))key.SetValue("StartupEnabled",1,RegistryValueKind.DWord);
        Log(status.Message);return status;
    }
    public static StartupStatus Disable()
    {
        object? service=null,folder=null;
        try
        {
            service=Service();folder=((dynamic)service).GetFolder(@"\");
            try{((dynamic)folder).DeleteTask(TaskName,0);}catch(COMException ex)when(Missing(ex)){ }
        }
        finally{Release(folder);Release(service);}
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))key?.DeleteValue("EasyBlueNext",false);
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey))key.SetValue("StartupEnabled",0,RegistryValueKind.DWord);
        var status=Read();if(status.Enabled||!status.Healthy)throw new IOException("자동실행 해제 상태를 확인하지 못했습니다. "+status.Message);
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
