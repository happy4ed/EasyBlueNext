using System.Xml.Linq;
namespace EasyBlue.Core;

// Pure XML construction keeps paths and account IDs out of shell command strings.
public static class StartupTaskDefinition
{
    public static string Create(string executable,string sid)
    {
        if(string.IsNullOrWhiteSpace(executable)||string.IsNullOrWhiteSpace(sid))throw new ArgumentException("실행 경로와 사용자 SID가 필요합니다.");
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name,object value)=>new(ns+name,value);
        return new XElement(ns+"Task",new XAttribute("version","1.3"),
            E("RegistrationInfo",E("Description","EasyBlue Next: current-user tray startup")),
            E("Triggers",E("LogonTrigger",new object[]{E("Enabled",true),E("UserId",sid),E("Delay","PT10S")})),
            E("Principals",new XElement(ns+"Principal",new XAttribute("id","User"),E("UserId",sid),E("LogonType","InteractiveToken"),E("RunLevel","LeastPrivilege"))),
            E("Settings",new object[]{E("MultipleInstancesPolicy","IgnoreNew"),E("DisallowStartIfOnBatteries",false),E("StopIfGoingOnBatteries",false),
                E("AllowHardTerminate",false),E("StartWhenAvailable",true),E("RunOnlyIfNetworkAvailable",false),
                E("IdleSettings",new object[]{E("StopOnIdleEnd",false),E("RestartOnIdle",false)}),E("AllowStartOnDemand",true),E("Enabled",true),E("Hidden",false),
                E("RunOnlyIfIdle",false),E("WakeToRun",false),E("ExecutionTimeLimit","PT0S"),E("Priority",5),
                E("RestartOnFailure",new object[]{E("Interval","PT1M"),E("Count",3)})}),
            new XElement(ns+"Actions",new XAttribute("Context","User"),E("Exec",new object[]{E("Command",executable),E("Arguments","--background"),E("WorkingDirectory",Path.GetDirectoryName(executable)??"")}))).ToString();
    }
}
