using System.Windows;
namespace EasyBlue.Desktop;
public partial class MainWindow
{
    private bool diagnosticsEnabled;
    private long diagnosticSession;
    private void CaptureDiagnostic(string text)
    {
        long session=Interlocked.Read(ref diagnosticSession);
        Dispatcher.BeginInvoke(()=>{if(diagnosticsEnabled&&session==Interlocked.Read(ref diagnosticSession))Record(text);});
    }
    private async void StartDiagnostics(object sender,RoutedEventArgs e)
    {
        if(diagnosticsEnabled)return;
        log.Clear();Interlocked.Increment(ref diagnosticSession);diagnosticsEnabled=true;StartupRegistration.DiagnosticsEnabled=true;
        if(transport!=null)transport.Diagnostic=CaptureDiagnostic;
        if(client!=null)client.Diagnostic=CaptureDiagnostic;
        DiagnosticStartButton.IsEnabled=false;DiagnosticStopButton.IsEnabled=true;
        DiagnosticStateText.Text="● 진단 수집 중 · 진단 종료를 누르면 중단합니다.";
        Record("진단 시작 · EasyBlue "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        Record(UsbLabel.Text);Record(StatusText.Text);Record(AutoStatus.Text);
        Record($"입력 감시 준비={automaticPrepared}; 엔진 준비={input?.ReadyForAutomatic}; 원격 키 해제={input?.ReleaseConfirmed}; 경로 수={VisibleLayouts().Sum(d=>ContactsOf(d).Count)}");
        if(InputEngine.GetCursorPos(out var pointer))Record($"PC 실제 커서={pointer.X},{pointer.Y}");
        await RefreshStartupStatus();
    }
    private void StopDiagnostics(object sender,RoutedEventArgs e)
    {
        if(!diagnosticsEnabled)return;
        Record("진단 종료");diagnosticsEnabled=false;Interlocked.Increment(ref diagnosticSession);StartupRegistration.DiagnosticsEnabled=false;
        if(transport!=null)transport.Diagnostic=null;
        if(client!=null)client.Diagnostic=null;
        DiagnosticStartButton.IsEnabled=true;DiagnosticStopButton.IsEnabled=false;
        DiagnosticStateText.Text="진단 종료됨 · 수집한 로그는 저장할 수 있습니다.";
    }
}
