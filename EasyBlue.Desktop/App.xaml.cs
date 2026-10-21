using System.Windows;
namespace EasyBlue.Desktop;
public partial class App : Application
{
    public static bool StartInBackground {get;private set;}
    private Mutex? owner;
    private EventWaitHandle? showRequest;
    private RegisteredWaitHandle? listener;
    protected override void OnStartup(StartupEventArgs e)
    {
        StartInBackground=e.Args.Contains("--background");
        showRequest=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\EasyBlueNext.Show");
        owner=new Mutex(true,@"Local\EasyBlueNext.DeviceOwner",out bool acquired);
        if(!acquired){showRequest.Set();Shutdown();return;}
        listener=ThreadPool.RegisterWaitForSingleObject(showRequest,(_,_)=>Dispatcher.BeginInvoke(()=>{if(MainWindow is EasyBlue.Desktop.MainWindow window)window.ShowFromTray();}),null,Timeout.Infinite,false);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null);showRequest?.Dispose();
        try{owner?.ReleaseMutex();}catch(ApplicationException){ }
        owner?.Dispose();base.OnExit(e);
    }
}
