using System.Windows;
namespace EasyBlue.Desktop;
public partial class App : Application
{
    public static bool StartInBackground {get;private set;}
    private Mutex? owner;
    private EventWaitHandle? showRequest,quitRequest;
    private RegisteredWaitHandle? quitListener;
    private RegisteredWaitHandle? listener;
    protected override void OnStartup(StartupEventArgs e)
    {
        int quitIndex=Array.IndexOf(e.Args,"--quit-existing");
        if(quitIndex>=0)
        {
            int result=2;
            try{if(quitIndex+1<e.Args.Length)result=UpdateHandoff.StopExisting(e.Args[quitIndex+1],e.Args.Contains("--force-legacy"));}catch{ }
            Shutdown(result);return;
        }
        if(e.Args.Any(arg=>arg is "--startup-enable" or "--startup-disable" or "--startup-status"))
        {
            int result;
            try
            {
                var status=e.Args.Contains("--startup-enable")?StartupRegistration.Enable():e.Args.Contains("--startup-disable")?StartupRegistration.Disable():StartupRegistration.Read();
                StartupRegistration.Log(status.Message);result=!status.Healthy?2:status.Enabled?0:1;
                // Disable is successful when no registration remains.
                if(e.Args.Contains("--startup-disable")&&status.Healthy&&!status.Enabled)result=0;
            }
            catch(Exception ex){StartupRegistration.Log("자동실행 설정 실패: "+ex);result=2;}
            Shutdown(result);return;
        }
        StartInBackground=e.Args.Contains("--background");
        StartupRegistration.Log(StartInBackground?"트레이 자동실행 진입":"수동 실행 진입");
        showRequest=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\EasyBlueNext.Show");
        owner=new Mutex(true,@"Local\EasyBlueNext.DeviceOwner",out bool acquired);
        if(!acquired){if(!StartInBackground)showRequest.Set();Shutdown();return;}
        quitRequest=new EventWaitHandle(false,EventResetMode.AutoReset,UpdateHandoff.QuitEvent);
        quitListener=ThreadPool.RegisterWaitForSingleObject(quitRequest,(_,_)=>Dispatcher.BeginInvoke(()=>{if(MainWindow is EasyBlue.Desktop.MainWindow window)window.QuitForUpdate();}),null,Timeout.Infinite,false);
        listener=ThreadPool.RegisterWaitForSingleObject(showRequest,(_,_)=>Dispatcher.BeginInvoke(()=>{if(MainWindow is EasyBlue.Desktop.MainWindow window)window.ShowFromTray();}),null,Timeout.Infinite,false);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null);quitListener?.Unregister(null);quitRequest?.Dispose();showRequest?.Dispose();
        try{owner?.ReleaseMutex();}catch(ApplicationException){ }
        owner?.Dispose();base.OnExit(e);
    }
}
