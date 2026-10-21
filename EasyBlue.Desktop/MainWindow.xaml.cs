using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyBlue.Core;
using Geometry = EasyBlue.Core.Geometry;
using Microsoft.Win32;
namespace EasyBlue.Desktop;

public partial class MainWindow : Window
{
    private static readonly string PreferencesPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EasyBlueNext","preferences.json");
    private UserPreferences preferences=new();
    private readonly List<string> log=[];
    private List<DisplayInfo> monitors=[];
    private List<PairedDevice> paired=[];
    private HidTransport? transport;
    private DeviceClient? client;
    private InputEngine? input;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly DwellTracker dwell=new();
    private bool ready,busy,closing,shutdownComplete;
    private bool previousCapture,polling,pairRequestQueued;
    private TaskCompletionSource? pollFinished;
    private Task pairStopTask=Task.CompletedTask;
    private string? lastAutoCandidate;
    private EdgeCountdown? edgeCountdown;
    private void HideCountdown(){if(edgeCountdown?.IsVisible==true)edgeCountdown.Hide();}
    private void ShowCountdown(string target,Attachment? route,double axis)
    {
        if(!InputEngine.GetCursorPos(out var pointer))return;
        var monitor=monitors.FirstOrDefault(m=>m.Pixels.Contains(pointer.X,pointer.Y));
        if(input?.Active==true&&route!=null)
        {
            var boundary=monitors.FirstOrDefault(m=>m.Id==route.MonitorId);
            if(boundary!=null)
            {
                monitor=boundary;
                if(route.Edge is "Left" or "Right"){pointer.X=(int)(route.Edge=="Right"?monitor.Pixels.Right-1:monitor.Pixels.X);pointer.Y=(int)(monitor.Pixels.Y+(axis-monitor.Bounds.Y)/monitor.Bounds.Height*(monitor.Pixels.Height-1));}
                else{pointer.Y=(int)(route.Edge=="Bottom"?monitor.Pixels.Bottom-1:monitor.Pixels.Y);pointer.X=(int)(monitor.Pixels.X+(axis-monitor.Bounds.X)/monitor.Bounds.Width*(monitor.Pixels.Width-1));}
            }
        }
        if(monitor==null)return;
        edgeCountdown??=new EdgeCountdown();
        edgeCountdown.Update(target=="PC"?"PC":ScreenName(target),dwell.RemainingSeconds(Environment.TickCount64,preferences.DwellSeconds),preferences.DwellSeconds,monitor.Pixels,pointer.X,pointer.Y);
    }
    private void UpdateAutoStatus()
    {
        if(AutoStatus==null)return;
        int routes=VisibleLayouts().Count(x=>ContactsOf(x).Any());
        AutoStatus.Text=!preferences.AutoSwitch?"자동 전환 꺼짐":client==null?"자동 전환 대기 · USB 연결을 확인하세요":pairFailurePaused?"오류 후 일시 중지 · 자동 전환을 껐다 켜거나 새로고침하세요":routes==0?"자동 전환 경로 없음 · 기기를 모니터에 붙여 파란 경계를 만드세요":$"자동 전환 켜짐 · 연결 경로 {routes}개 · 파란 경계에서 {preferences.DwellSeconds:0.0}초 대기";
    }
    private long nextPoll,switchVersion;
    private string? blockedRoute,lastTarget;
    private string? dragging;
    private RemoteLayout? dragOriginal;
    private Point dragStart;
    private double scale=7,originX,originY;
    private Window? pairWindow;
    private TextBlock? pairText;
    private long pairDeadline;
    private HashSet<string> oldPairIds=[];
    private bool pairing,pairFailurePaused,exitRequested,pairStopping;
    private long pairGeneration;
    private System.Windows.Forms.NotifyIcon? tray;
    private sealed record DeviceRow(string Id,string Name,string Position,string Mode,string Connection,string Shortcut);
    public MainWindow()
    {
        InitializeComponent();
        try{preferences=UserPreferences.Load(PreferencesPath);}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){Record("설정을 읽지 못해 기본 설정을 사용합니다: "+ex.Message);}
        var work=SystemParameters.WorkArea;
        MinHeight=Math.Min(MinHeight,Math.Max(260,work.Height-24));Height=Math.Min(Height,Math.Max(MinHeight,work.Height-24));
        MinWidth=Math.Min(MinWidth,Math.Max(360,work.Width-24));Width=Math.Min(Width,Math.Max(MinWidth,work.Width-24));
        FontSize=preferences.FontSize;ThemeManager.Apply(preferences.Theme);
        SystemTheme.IsChecked=preferences.Theme=="System";LightTheme.IsChecked=preferences.Theme=="Light";DarkTheme.IsChecked=preferences.Theme=="Dark";
        LedBox.IsChecked=preferences.LedEnabled;StartupBox.IsChecked=ReadsStartup();
        AccelerationBox.IsChecked=preferences.MouseAcceleration;WheelSlider.Value=preferences.WheelSpeed;
        SpeedSlider.Value=preferences.MouseSpeed;KoreanBox.IsChecked=preferences.KoreanCtrlSpace;
        AutoToggle.IsChecked=preferences.AutoSwitch;AvoidDragBox.IsChecked=preferences.AvoidDrag;DwellBox.Text=preferences.DwellSeconds.ToString("0.0",CultureInfo.InvariantCulture);
        SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;
        SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
        SourceInitialized+=(_,_)=>EnableFirstClick(this);
        ready=true;timer.Tick+=Tick;RefreshShortcutLabels();
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)HideToTray();};
    }
    private void Navigate(object sender,SelectionChangedEventArgs e)
    {
        if(DevicePage==null)return;
        DevicePage.Visibility=Navigation.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        InputPage.Visibility=Navigation.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        GeneralPage.Visibility=Navigation.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;
        DiagnosticsPage.Visibility=Navigation.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        Record("EasyBlue Next "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version+" · 제어 세션 초기화 적용");
        RefreshMonitors();input=new InputEngine();ApplyShortcuts();InitializeTray();
        lastTarget=preferences.LastDeviceId;
        input.LastDeviceRequested+=()=>Dispatcher.BeginInvoke(async()=>{if(lastTarget!=null&&paired.Any(d=>d.Id==lastTarget))await Switch(lastTarget);else StatusText.Text="마지막으로 연결한 기기가 없습니다. 기기 목록에서 먼저 전환해 주세요.";});
        input.Released+=reason=>Dispatcher.BeginInvoke(()=>{blockedRoute=lastTarget;dwell.Reset();StatusText.Text=reason;Record(reason);});
        input.DeviceRequested+=id=>Dispatcher.BeginInvoke(async()=>await Switch(id));
        input.ReturnRequested+=()=>Interlocked.Increment(ref switchVersion);
        if(App.StartInBackground)HideToTray();
        timer.Start();await Scan();
    }
    private void RefreshMonitors()
    {try{monitors=MonitorDiscovery.Find(preferences.MonitorSizes);Draw();}catch(Exception ex){Record(ex.Message);}}
    private void SystemPreferenceChanged(object sender,UserPreferenceChangedEventArgs e)=>Dispatcher.BeginInvoke(()=>{if(preferences.Theme=="System"){ThemeManager.Apply("System");Draw();}});
    private void DisplaysChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(()=>{input?.Emergency("모니터 구성이 바뀌어 PC로 복귀했습니다.",false);RefreshMonitors();});
    private void ThemeClick(object sender,RoutedEventArgs e)
    {if(!ready)return;preferences=preferences with{Theme=(string)((RadioButton)sender).Tag};ThemeManager.Apply(preferences.Theme);Save();Draw();}
    private void FontClick(object sender,RoutedEventArgs e)
    {preferences=preferences with{FontSize=double.Parse((string)((Button)sender).Tag,CultureInfo.InvariantCulture)};FontSize=preferences.FontSize;Save();}
    private void AutoChanged(object sender,RoutedEventArgs e)
    {preferences=preferences with{AutoSwitch=AutoToggle.IsChecked==true};Save();dwell.Reset();blockedRoute=null;if(preferences.AutoSwitch){pairFailurePaused=false;Record("사용자가 자동 전환을 켰습니다. 상태 조회와 경계 감시를 재개합니다.");}UpdateAutoStatus();}
    private void AvoidDragChanged(object sender,RoutedEventArgs e)
    {preferences=preferences with{AvoidDrag=AvoidDragBox.IsChecked==true};Save();}
    private void DwellChanged(object sender,RoutedEventArgs e)
    {
        if(double.TryParse(DwellBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value))preferences=(preferences with{DwellSeconds=value}).Validate();
        DwellBox.Text=preferences.DwellSeconds.ToString("0.0",CultureInfo.InvariantCulture);Save();
    }
    private void InputSettingsChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        preferences=preferences with{MouseSpeed=SpeedSlider.Value,KoreanCtrlSpace=KoreanBox.IsChecked==true,MouseAcceleration=AccelerationBox.IsChecked==true,WheelSpeed=WheelSlider.Value};
        if(input!=null){input.KoreanCtrlSpace=preferences.KoreanCtrlSpace;input.Acceleration=preferences.MouseAcceleration;input.WheelSpeed=preferences.WheelSpeed;}Save();
    }
    private void Save(){try{preferences.Save(PreferencesPath);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Record("설정 저장 실패: "+ex.Message);}}
    private void Record(string text)
    {log.Add($"{DateTime.Now:HH:mm:ss} {text}");if(log.Count>150)log.RemoveAt(0);if(DiagnosticText!=null)DiagnosticText.Text=string.Join("\n",log);}
    private async void ScanClick(object sender,RoutedEventArgs e)=>await Scan();
    private async Task Scan()
    {
        if(busy||pairing||pairStopping)return;pairFailurePaused=false;busy=true;ScanButton.IsEnabled=false;PairButton.IsEnabled=false;
        StatusText.Text="USB와 저장된 기기를 확인하고 있습니다…";
        try
        {
            if(input!=null)await input.Stop();
            if(client!=null)try{await client.CloseSession();}catch(Exception ex){Record("기존 세션 정리: "+ex.Message);}
            transport?.Dispose();transport=null;client=null;paired=[];RefreshRows();
            var legacy=Process.GetProcessesByName("EBTiny");
            bool legacyRunning=legacy.Length>0;foreach(var process in legacy)process.Dispose();
            if(legacyRunning){input?.Emergency("기존 앱으로 입력 제어를 넘깁니다.",true);UsbLabel.Text="○ 기존 앱 실행 중";StatusText.Text="기존 EBTiny.exe를 종료한 뒤 새로고침해 주세요.";Record(StatusText.Text);return;}
            var found=await Task.Run(HidDiscovery.Find);Record($"USB 인터페이스 {found.Count}개 발견");
            Exception? lastError=null;
            foreach(var candidate in found)
            {
                HidTransport? connection=null;
                try
                {
                    connection=await Task.Run(()=>new HidTransport(candidate.Path));
                    connection.Diagnostic=text=>Dispatcher.BeginInvoke(()=>Record(text));
                    var device=new DeviceClient(connection){LedEnabled=preferences.LedEnabled};
                    device.Diagnostic=text=>Dispatcher.BeginInvoke(()=>Record(text));
                    await device.Initialize();
                    var rows=await device.Devices();
                    try{await device.UpdateLed(preferences.LedEnabled);}catch(DeviceCommandRejectedException ex){Record("LED 초기 설정 거부: "+ex.Message);}
                    transport=connection;client=device;paired=rows.ToList();Record("장치 프로토콜 확인: "+candidate.Identity);break;
                }
                catch(Exception ex){connection?.Dispose();lastError=ex;Record(candidate.Identity+": "+ex.Message);}
            }
            if(client==null)
            {
                UsbLabel.Text=found.Count==0?"○ USB 연결 안 됨":"○ 장치 통신 실패";
                StatusText.Text=lastError?.Message??"EasyBlue USB를 연결하고 새로고침해 주세요.";
            }
            else{try{input?.StartMonitoring();}catch(Exception ex){Record(ex.Message);}UsbLabel.Text="● EasyBlue 연결됨";StatusText.Text=$"저장된 기기 {paired.Count}개 · 기기 관리에서 단축키 지정";}
            EnsureLayouts();RefreshRows();Draw();
        }
        catch(Exception ex){Record(ex.Message);StatusText.Text="장치 검색 실패: "+ex.Message;}
        finally{busy=false;ScanButton.IsEnabled=true;PairButton.IsEnabled=client!=null;nextPoll=Environment.TickCount64+2500;}
    }
    private void EnsureLayouts()
    {
        double right=monitors.Count==0?25:monitors.Max(x=>x.Bounds.Right);
        foreach(var device in paired)
            if(!preferences.Devices.Any(x=>x.Id==device.Id))preferences.Devices.Add(new(device.Id,device.Label,right+2+(device.Slot%2)*5,device.Slot/2*8));
        Save();
    }
    private IReadOnlyList<ScreenNode> MonitorNodes()=>monitors.Select(x=>x.Node).ToArray();
    private List<RemoteLayout> VisibleLayouts()=>preferences.Devices.Where(x=>paired.Any(d=>d.Id==x.Id)).ToList();
    private IReadOnlyList<ScreenNode> AllScreens()=>MonitorNodes().Concat(VisibleLayouts().Select(d=>new ScreenNode(d.Id,d.Name,false,d.Bounds,d.PixelWidth,d.PixelHeight))).ToArray();
    private IReadOnlyList<Attachment> ContactsOf(RemoteLayout d)=>Geometry.Contacts(new(d.Id,d.Name,false,d.Bounds,d.PixelWidth,d.PixelHeight),AllScreens());
    private Attachment? AttachmentOf(RemoteLayout d)=>ContactsOf(d).FirstOrDefault();
    private string ScreenName(string id)=>monitors.FirstOrDefault(m=>m.Id==id)?.Name??preferences.Devices.FirstOrDefault(d=>d.Id==id)?.Name??id;
    private string Opposite(string edge)=>edge switch{"Left"=>"Right","Right"=>"Left","Top"=>"Bottom",_=>"Top"};
    private string EdgeText(string edge)=>edge switch{"Left"=>"왼쪽","Right"=>"오른쪽","Top"=>"위","Bottom"=>"아래",_=>edge};
    private void RefreshRows()
    {
        DeviceList.ItemsSource=paired.Select(d=>{
            var l=preferences.Devices.FirstOrDefault(x=>x.Id==d.Id);var a=l==null?null:AttachmentOf(l);
            string monitor=a==null?"":ScreenName(a.MonitorId);
            return new DeviceRow(d.Id,l?.Name??d.Label,a==null?"배치도에서 분리":monitor+" · "+EdgeText(a.Edge),a==null?"수동 전환":"자동 전환",d.Connected?"● 연결됨":"○ 등록됨",preferences.DeviceShortcuts.TryGetValue(d.Id,out var shortcut)?ShortcutText(shortcut):"단축키 설정");}).ToArray();
        EmptyLabel.Visibility=paired.Count==0?Visibility.Visible:Visibility.Collapsed;UpdateAutoStatus();
    }
    private async void SwitchClick(object sender,RoutedEventArgs e)=>await Switch((string)((Button)sender).Tag);
    private async Task Switch(string id,Attachment? entry=null,double axis=0)
    {
        if(busy||pairing||pairStopping||client==null||input==null)return;
        var device=paired.FirstOrDefault(x=>x.Id==id);var layout=preferences.Devices.FirstOrDefault(x=>x.Id==id);
        if(device==null||layout==null)return;
        busy=true;PairButton.IsEnabled=false;
        long request=Interlocked.Increment(ref switchVersion);
        try
        {
            await input.Stop();StatusText.Text=layout.Name+"에 연결 중…";
            if(!device.Connected)await client.Connect(device.Address,paired.Any(d=>d.Connected));
            bool connected=false;
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while(!deadline.IsCancellationRequested)
            {
                if(closing||request!=Interlocked.Read(ref switchVersion))return;
                var state=await client.Devices(deadline.Token);paired=state.ToList();
                if(paired.Any(x=>x.Id==id&&x.Connected)){connected=true;break;}
                await Task.Delay(250,deadline.Token);
            }
            if(!connected)throw new IOException("선택한 기기의 연결이 확인되지 않았습니다.");
            InputEngine.GetCursorPos(out var point);var monitor=monitors.FirstOrDefault(x=>x.Pixels.Contains(point.X,point.Y))??monitors.First();
            double ex=0.5,ey=0.5;
            if(entry!=null)
            {
                if(entry.Edge is "Left" or "Right"){ex=entry.Edge=="Right"?12d/layout.PixelWidth:1-12d/layout.PixelWidth;ey=(axis-layout.Bounds.Y)/layout.Bounds.Height;}
                else{ey=entry.Edge=="Bottom"?12d/layout.PixelHeight:1-12d/layout.PixelHeight;ex=(axis-layout.Bounds.X)/layout.Bounds.Width;}
            }
            if(closing||request!=Interlocked.Read(ref switchVersion))return;
            await input.AttachClient(client);
            await client.BeginInput();
            if(closing||request!=Interlocked.Read(ref switchVersion))return;
            input.Speed=SpeedSlider.Value;input.Acceleration=preferences.MouseAcceleration;input.WheelSpeed=preferences.WheelSpeed;input.KoreanCtrlSpace=preferences.KoreanCtrlSpace;input.Activate(client,id,layout.PixelWidth,layout.PixelHeight,monitor.Pixels,ex,ey);
            pairFailurePaused=false;lastTarget=id;preferences=preferences with{LastDeviceId=id};Save();dwell.Reset();StatusText.Text=layout.Name+"로 입력 중 · "+ShortcutText(preferences.ReturnShortcut)+"로 PC 복귀";Record(StatusText.Text);RefreshRows();Draw();
        }
        catch(Exception ex){input.Emergency("전환하지 못해 PC 입력을 유지합니다.",false);blockedRoute=id;StatusText.Text="전환 실패: "+ex.Message;Record(StatusText.Text);}
        finally{busy=false;PairButton.IsEnabled=client!=null;}
    }
    private async void ReturnClick(object sender,RoutedEventArgs e)
    {Interlocked.Increment(ref switchVersion);if(input?.Active==true)await input.Stop();blockedRoute=lastTarget;dwell.Reset();StatusText.Text="PC 입력으로 복귀했습니다.";Draw();}
    private async void Tick(object? sender,EventArgs e)
    {
        if(closing||input==null){HideCountdown();return;}input.Pulse();
        if(previousCapture!=input.Active){previousCapture=input.Active;Draw();}
        if(input.Active)input.Speed=SpeedSlider.Value;
        if(!pairFailurePaused&&!busy&&client!=null&&!input.Active&&Environment.TickCount64>=nextPoll){nextPoll=Environment.TickCount64+2500;await Poll();}
        if(pairing||pairStopping||pairFailurePaused||dragging!=null||input.EditingShortcut||!preferences.AutoSwitch||client==null){dwell.Reset();HideCountdown();return;}
        if(pairRequestQueued||busy&&!polling){dwell.Reset();HideCountdown();return;}
        if(preferences.AvoidDrag&&(input.Dragging||InputEngine.PcDragging)){dwell.Reset();HideCountdown();return;}
        string? target=null;Attachment? route=null;double axis=0;
        if(!input.Active&&InputEngine.GetCursorPos(out var p))
        {
            foreach(var l in VisibleLayouts())
            {
                foreach(var a in ContactsOf(l))
                {
                var m=monitors.FirstOrDefault(x=>x.Id==a.MonitorId);if(m==null)continue;
                var at=Geometry.PcEdgeAxis(a,m.Pixels,m.Bounds,p.X,p.Y);
                if(at.HasValue){target=l.Id;route=a;axis=at.Value;break;}
                }
                if(target!=null)break;
            }
            if(target==null)blockedRoute=null;
            if(target==blockedRoute)target=null;
        }
        else if(input.Active)
        {
            var l=VisibleLayouts().FirstOrDefault(x=>x.Id==input.TargetId);
            if(l!=null)foreach(var a in ContactsOf(l))
            {
                var v=input.VirtualPosition;
                bool edge=a.Edge switch{"Right"=>v.X<=1,"Left"=>v.X>=l.PixelWidth-2,"Bottom"=>v.Y<=1,"Top"=>v.Y>=l.PixelHeight-2,_=>false};
                double at=a.Edge is "Left" or "Right"?l.Bounds.Y+v.Y/Math.Max(1,l.PixelHeight-1)*l.Bounds.Height:l.Bounds.X+v.X/Math.Max(1,l.PixelWidth-1)*l.Bounds.Width;
                if(edge&&at>=a.Start&&at<=a.End)
                {
                    axis=at;
                    if(monitors.Any(m=>m.Id==a.MonitorId)){target="PC";route=a;}
                    else{target=a.MonitorId;route=new(a.MonitorId,l.Id,Opposite(a.Edge),a.Start,a.End);}
                    break;
                }
            }
        }
        if(target!=null&&target!=lastAutoCandidate){Record("자동 전환 경계 감지: "+(target=="PC"?"PC":ScreenName(target)));}lastAutoCandidate=target;
        string? fired=dwell.Update(target,Environment.TickCount64,preferences.DwellSeconds,!polling);
        if(target!=null&&fired==null)ShowCountdown(target,route,axis);else HideCountdown();
        if(fired=="PC")
        {
            await input.Stop();blockedRoute=lastTarget;
            var m=monitors.FirstOrDefault(x=>x.Id==route?.MonitorId);
            if(m!=null&&route!=null)
            {
                int x=(int)(m.Pixels.X+m.Pixels.Width/2),y=(int)(m.Pixels.Y+m.Pixels.Height/2);
                if(route.Edge is "Left" or "Right"){x=(int)(route.Edge=="Right"?m.Pixels.Right-12:m.Pixels.X+32);y=(int)(m.Pixels.Y+(axis-m.Bounds.Y)/m.Bounds.Height*(m.Pixels.Height-1));}
                else{y=(int)(route.Edge=="Bottom"?m.Pixels.Bottom-12:m.Pixels.Y+32);x=(int)(m.Pixels.X+(axis-m.Bounds.X)/m.Bounds.Width*(m.Pixels.Width-1));}
                InputEngine.Warp(x,y);
            }
            Draw();
        }
        else if(fired!=null){Record("자동 전환 대기 완료: "+ScreenName(fired));await Switch(fired,route,axis);}
    }
    private async Task Poll()
    {
        if(client==null||busy||pairFailurePaused||pairStopping||pairRequestQueued)return;busy=true;polling=true;pollFinished=new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            paired=(await client.Devices()).ToList();EnsureLayouts();RefreshRows();Draw();
            if(pairing&&paired.Any(x=>!oldPairIds.Contains(x.Id)&&x.Connected))
            {pairing=false;if(pairText!=null)pairText.Text="새 기기가 페어링 목록에 추가되었습니다. 창을 닫고 기기를 배치해 주세요.";StatusText.Text="새 기기 페어링 완료";Record(StatusText.Text);}
            else if(pairing&&Environment.TickCount64>=pairDeadline){pairing=false;pairFailurePaused=true;Record("페어링 대기 종료 · 자동 재시도와 상태 조회 중단");await client.StopPairing();if(pairText!=null)pairText.Text="페어링 대기 시간이 끝났습니다. 다시 시작하려면 상단 버튼을 누르세요.";}
        }
        catch(Exception ex)
        {
            input?.Emergency("장치 연결이 끊겨 PC로 복귀했습니다.",false);
            paired=paired.Select(d=>d with{Connected=false}).ToList();RefreshRows();
            pairing=false;if(pairText!=null)pairText.Text="장치 통신이 끊겼습니다. 창을 닫고 새로고침해 주세요.";
            client=null;transport?.Dispose();transport=null;PairButton.IsEnabled=false;UsbLabel.Text="○ 통신 끊김";
            Record(ex.Message);StatusText.Text="장치 응답 실패: "+ex.Message;
        }
        finally{polling=false;busy=false;pollFinished?.TrySetResult();}
    }
    private async void PairClick(object sender,RoutedEventArgs e)
    {
        if(pairRequestQueued||closing)return;
        pairRequestQueued=true;PairButton.IsEnabled=false;
        try
        {
            // Retain this click while a periodic read or cancellation is finishing.
            if(polling&&pollFinished!=null)await pollFinished.Task;
            try{await pairStopTask;}catch(Exception ex){Record("이전 페어링 취소 결과: "+ex.Message);}pairStopTask=Task.CompletedTask;
            if(!closing)await StartPairingDialog();
        }
        catch(Exception ex){Record("페어링 준비 실패: "+ex.Message);StatusText.Text=ex.Message;}
        finally{pairRequestQueued=false;PairButton.IsEnabled=client!=null&&!closing;}
    }
    private async Task StartPairingDialog()
    {
        if(pairWindow!=null){pairWindow.Activate();return;}
        if(client==null||busy||pairStopping)return;
        if(paired.Count>=4){MessageBox.Show(this,"페어링 목록이 가득 찼습니다. 기기의 설정 메뉴에서 사용하지 않는 페어링을 삭제한 뒤 추가하세요.","새 기기 페어링");return;}
        pairFailurePaused=false;busy=true;PairButton.IsEnabled=false;
        long generation=++pairGeneration;
        try
        {
            Record("사용자 요청으로 새 페어링 시작 · 자동 재시도 없음");
            if(input!=null)await input.Stop();oldPairIds=paired.Select(x=>x.Id).ToHashSet();
            await client.StartPairing();if(closing||generation!=pairGeneration){pairFailurePaused=true;await client.StopPairing();return;}pairing=true;pairDeadline=Environment.TickCount64+60000;
            var pairingDialog=NewDialog("새 기기 페어링",420,440,out var panel);pairWindow=pairingDialog;
            panel.Children.Add(new TextBlock{Text="휴대폰에서 Bluetooth 설정을 열어 주세요",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
            pairText=new TextBlock{Text="1. 휴대폰·태블릿의 Bluetooth를 켜세요.\n2. 검색된 EasyBlue를 선택하세요.\n3. 연결 요청을 수락하세요.\n\n최대 60초 동안 기다립니다. 목록은 자동으로 새로고침됩니다.",Margin=new(0,0,0,12)};panel.Children.Add(pairText);
            panel.Children.Add(new TextBlock{Text="휴대폰에서 PIN을 요청할 때만 입력하세요.",FontSize=12,Margin=new(0,8,0,4)});
            var pin=new PasswordBox{MaxLength=16,Padding=new(6),Margin=new(0,0,0,6)};
            pin.SetResourceReference(Control.BackgroundProperty,"Panel");pin.SetResourceReference(Control.ForegroundProperty,"Ink");panel.Children.Add(pin);
            var sendPin=new Button{Content="PIN 전송",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,12)};
            sendPin.Click+=async(_,_)=>{if(client==null||busy||!pairing)return;busy=true;sendPin.IsEnabled=false;try{string value=pin.Password;pin.Clear();await client.SendPairingPin(value);if(pairText!=null&&pairing)pairText.Text="PIN을 전송했습니다. 휴대폰의 연결 결과를 기다립니다.";}catch(Exception ex){pairing=false;pairFailurePaused=true;if(pairText!=null)pairText.Text="PIN 전송 실패: "+ex.Message+"\n요청을 중단했습니다. 창을 닫고 다시 시작하세요.";Record("PIN 전송 실패 · 자동 재시도와 상태 조회 중단");try{await client.StopPairing();}catch(Exception stop){Record("페어링 종료 실패: "+stop.Message);}}finally{busy=false;sendPin.IsEnabled=true;nextPoll=0;}};panel.Children.Add(sendPin);
            var close=new Button{Content="닫기",HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>pairingDialog.Close();panel.Children.Add(close);
            pairWindow.Closed+=async(_,_)=>{pairWindow=null;pairText=null;if(pairing){pairing=false;pairStopping=true;pairFailurePaused=true;Record("사용자가 페어링 대기를 취소했습니다.");try{if(client!=null){pairStopTask=client.StopPairing();await pairStopTask;}}catch(Exception ex){Record("페어링 취소: "+ex.Message);}finally{pairStopping=false;}}};
            pairWindow.Show();Record("페어링 대기 명령에 장치가 응답했습니다.");StatusText.Text="휴대폰 Bluetooth 설정에서 EasyBlue를 선택하세요.";nextPoll=0;
        }
        catch(Exception ex){pairing=false;pairFailurePaused=true;dwell.Reset();StatusText.Text="페어링 시작 실패: "+ex.Message;Record(StatusText.Text);Record("페어링과 주기적 상태 조회 중단 · 다시 누르거나 새로고침할 때만 재개합니다.");}
        finally{busy=false;PairButton.IsEnabled=client!=null;}
    }
    private Window NewDialog(string title,double width,double height,out StackPanel panel)
    {
        var window=new Window{Owner=this,Title=title,Width=width,Height=height,MinWidth=320,MinHeight=240,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontSize=13,Background=(Brush)FindResource("Background"),Foreground=(Brush)FindResource("Ink")};
        window.SetResourceReference(BackgroundProperty,"Background");window.SetResourceReference(ForegroundProperty,"Ink");
        window.SourceInitialized+=(_,_)=>EnableFirstClick(window);
        panel=new StackPanel{Margin=new Thickness(20)};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return window;
    }
    private TextBox Field(StackPanel panel,string label,string value)
    {panel.Children.Add(new TextBlock{Text=label,Margin=new(0,8,0,4)});var box=new TextBox{Text=value};panel.Children.Add(box);return box;}
    private void SizesClick(object sender,RoutedEventArgs e)
    {
        var dialog=NewDialog("모니터 화면 크기",420,380,out var panel);panel.Children.Add(new TextBlock{Text="실제 모니터의 인치 크기를 입력하세요. 위치와 해상도는 Windows에서 읽습니다.",Margin=new(0,0,0,8)});
        var fields=monitors.Select(m=>(Monitor:m,Field:Field(panel,m.Name+" · "+(int)m.Pixels.Width+"×"+(int)m.Pixels.Height,m.Diagonal.ToString(CultureInfo.InvariantCulture)))).ToList();
        var apply=new Button{Content="저장",HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        apply.Click+=(_,_)=>{var sizes=new Dictionary<string,double>(preferences.MonitorSizes);foreach(var item in fields){if(!double.TryParse(item.Field.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n is <10 or >100){MessageBox.Show(dialog,"10~100인치 사이의 숫자를 입력하세요.");return;}sizes[item.Monitor.Id]=n;}preferences=preferences with{MonitorSizes=sizes};Save();RefreshMonitors();RefreshRows();dialog.Close();};panel.Children.Add(apply);dialog.ShowDialog();
    }
    private void EditDeviceClick(object sender,RoutedEventArgs e)
    {
        string id=(string)((Button)sender).Tag;var l=preferences.Devices.First(x=>x.Id==id);
        var dialog=NewDialog("기기 설정",420,530,out var panel);
        var name=Field(panel,"이 PC에서 표시할 기기 이름",l.Name);
        var readName=new Button{Content="Windows에 저장된 기기 이름 가져오기",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,6,0,0)};
        readName.Click+=async(_,_)=>{readName.IsEnabled=false;try{var names=await Task.Run(BluetoothNames.ReadCached);if(names.TryGetValue(id,out var value))name.Text=value;else MessageBox.Show(dialog,"Windows에 이 기기의 이름이 저장되어 있지 않습니다. 아래 이름을 직접 입력해 주세요. EasyBlue 동글의 이름 조회 명령은 아직 확인되지 않았습니다.","기기 이름");}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"이름 조회 실패");}finally{readName.IsEnabled=true;}};panel.Children.Add(readName);
        var diagonal=Field(panel,"화면 크기(인치) · 폰 7 / 태블릿 12",l.Diagonal.ToString(CultureInfo.InvariantCulture));
        var width=Field(panel,"화면 너비(px)",l.PixelWidth.ToString());var height=Field(panel,"화면 높이(px)",l.PixelHeight.ToString());
        var flip=new Button{Content="가로·세로 바꾸기",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,0)};flip.Click+=(_,_)=>{(width.Text,height.Text)=(height.Text,width.Text);};panel.Children.Add(flip);
        var apply=new Button{Content="저장",HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};
        apply.Click+=(_,_)=>{
            if(string.IsNullOrWhiteSpace(name.Text)||name.Text.Trim().Length>40||!double.TryParse(diagonal.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double d)||!double.IsFinite(d)||d is <3 or >40||!int.TryParse(width.Text,out int w)||!int.TryParse(height.Text,out int h)||w is <240 or >16384||h is <240 or >16384){MessageBox.Show(dialog,"이름과 화면 크기를 확인하세요. 인치 3~40, 해상도 240~16384 범위입니다.");return;}
            var updated=l with{Name=name.Text.Trim(),Diagonal=d,PixelWidth=w,PixelHeight=h};
            if(monitors.Any(m=>updated.Bounds.Intersects(m.Bounds))||VisibleLayouts().Any(o=>o.Id!=id&&updated.Bounds.Intersects(o.Bounds))){updated=updated with{X=AllScreens().Where(s=>s.Id!=id).Max(s=>s.Bounds.Right)+2,Y=0};}
            Replace(updated);Save();RefreshRows();Draw();dialog.Close();};panel.Children.Add(apply);
        var detach=new Button{Content="자동 전환에서 분리",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,0,0)};detach.Click+=(_,_)=>{Replace(l with{X=AllScreens().Where(s=>s.Id!=id).Max(s=>s.Bounds.Right)+3,Y=0});Save();RefreshRows();Draw();dialog.Close();};panel.Children.Add(detach);
        var delete=new Button{Content="페어링 삭제…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,0)};
        delete.Click+=async(_,_)=>
        {
            if(client==null||busy)return;
            if(MessageBox.Show(dialog,"동글에서 이 기기의 페어링 정보를 삭제할까요?","페어링 삭제",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            busy=true;
            try
            {
                if(input!=null)await input.Stop();
                var device=paired.First(x=>x.Id==id);
                if(paired.Any(x=>x.Connected))await client.Disconnect();
                await client.DeletePairing(device.Address);
                dialog.Close();busy=false;await Poll();
            }
            catch(Exception ex){if(!closing)MessageBox.Show(dialog,ex.Message,"페어링 삭제 실패");}
            finally{busy=false;}
        };panel.Children.Add(delete);
        dialog.ShowDialog();
    }
    private void Replace(RemoteLayout layout){int i=preferences.Devices.FindIndex(x=>x.Id==layout.Id);if(i>=0)preferences.Devices[i]=layout;}
    private void CanvasResized(object sender,SizeChangedEventArgs e){if(ready)Draw();}
    private Brush Color(string name)=>(Brush)FindResource(name);
    private void Draw(bool fit=true)
    {
        if(LayoutCanvas==null||LayoutCanvas.ActualWidth<1||monitors.Count==0)return;
        var remotes=VisibleLayouts();var all=monitors.Select(x=>x.Bounds).Concat(remotes.Select(x=>x.Bounds)).ToList();
        if(fit&&dragging==null)
        {
            double minX=all.Min(x=>x.X)-3,minY=all.Min(x=>x.Y)-2,maxX=all.Max(x=>x.Right)+6,maxY=all.Max(x=>x.Bottom)+4;
            scale=Math.Max(0.1,Math.Min((LayoutCanvas.ActualWidth-26)/(maxX-minX),(LayoutCanvas.ActualHeight-45)/(maxY-minY)));
            originX=13-minX*scale;originY=24-minY*scale;
        }
        LayoutCanvas.Children.Clear();
        for(double x=12;x<LayoutCanvas.ActualWidth;x+=16)for(double y=14;y<LayoutCanvas.ActualHeight-18;y+=16){var dot=new Ellipse{Width=1,Height=1,Fill=Color("Border"),IsHitTestVisible=false};Canvas.SetLeft(dot,x);Canvas.SetTop(dot,y);LayoutCanvas.Children.Add(dot);}
        foreach(var m in monitors)
        {
            var border=Screen(m.Bounds,Color("Screen"),m.Primary?Color("Accent"):Color("Muted"));
            var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(new TextBlock{Text=m.Name,FontSize=10,TextAlignment=TextAlignment.Center});labels.Children.Add(new TextBlock{Text=m.Diagonal+"인치"+(m.Primary?" · 주 모니터":""),FontSize=9,Foreground=Color("Muted"),TextAlignment=TextAlignment.Center});border.Child=labels;
            border.ToolTip=$"{m.Name} · {(int)m.Pixels.Width}×{(int)m.Pixels.Height} · {m.Diagonal}인치";
        }
        foreach(var l in remotes)
        {
            var a=AttachmentOf(l);var border=Screen(l.Bounds,Color("Screen"),input?.TargetId==l.Id?Color("Accent"):Color("Muted"));border.Cursor=Cursors.SizeAll;border.Tag=l.Id;border.ToolTip=l.Name+" · "+l.Diagonal+"인치";border.MouseLeftButtonDown+=DeviceDown;
            double x=l.Bounds.X*scale+originX,y=l.Bounds.Y*scale+originY;
            Label(l.Name+" · "+l.Diagonal+"인치",x+l.Bounds.Width*scale+5,y,10,Color("Ink"),l.Id);Label(a==null?"수동":"자동",x+l.Bounds.Width*scale+5,y+14,9,a==null?Color("Muted"):Color("Accent"),l.Id);
            foreach(var contact in ContactsOf(l))
            {
                var m=AllScreens().First(x=>x.Id==contact.MonitorId);bool vertical=contact.Edge is "Left" or "Right";
                a=contact;
                double c=a.Edge switch{"Right"=>m.Bounds.Right,"Left"=>m.Bounds.X,"Bottom"=>m.Bounds.Bottom,_=>m.Bounds.Y};
                var line=new Line{X1=(vertical?c:a.Start)*scale+originX,X2=(vertical?c:a.End)*scale+originX,Y1=(vertical?a.Start:c)*scale+originY,Y2=(vertical?a.End:c)*scale+originY,Stroke=Color("Accent"),StrokeThickness=3,IsHitTestVisible=false};LayoutCanvas.Children.Add(line);
            }
        }
    }
    private Border Screen(Box box,Brush fill,Brush outline)
    {var b=new Border{Width=Math.Max(4,box.Width*scale),Height=Math.Max(4,box.Height*scale),Background=fill,BorderBrush=outline,BorderThickness=new(1.5),CornerRadius=new(3)};Canvas.SetLeft(b,box.X*scale+originX);Canvas.SetTop(b,box.Y*scale+originY);LayoutCanvas.Children.Add(b);return b;}
    private void Label(string text,double x,double y,double size,Brush color,string? deviceId=null)
    {var t=new TextBlock{Text=text,FontSize=size,Foreground=color,IsHitTestVisible=deviceId!=null,Background=Brushes.Transparent};if(deviceId!=null){t.Tag=deviceId;t.Cursor=Cursors.SizeAll;t.MouseLeftButtonDown+=DeviceDown;}Canvas.SetLeft(t,x);Canvas.SetTop(t,y);LayoutCanvas.Children.Add(t);}
    private void DeviceDown(object sender,MouseButtonEventArgs e)
    {if(input?.Active==true||busy)return;dragging=(string)((FrameworkElement)sender).Tag;dragOriginal=preferences.Devices.First(x=>x.Id==dragging);dragStart=e.GetPosition(LayoutCanvas);LayoutCanvas.CaptureMouse();e.Handled=true;dwell.Reset();}
    private void CanvasMove(object sender,MouseEventArgs e)
    {if(dragging==null||dragOriginal==null)return;Point p=e.GetPosition(LayoutCanvas);var changed=dragOriginal with{X=dragOriginal.X+(p.X-dragStart.X)/scale,Y=dragOriginal.Y+(p.Y-dragStart.Y)/scale};Replace(changed);Draw(false);}
    private void CanvasUp(object sender,MouseButtonEventArgs e)
    {
        if(dragging==null||dragOriginal==null)return;
        var l=preferences.Devices.First(x=>x.Id==dragging);var snapped=Geometry.Snap(l,AllScreens().Where(s=>s.Id!=l.Id).ToArray(),VisibleLayouts(),Math.Clamp(10/scale,0.45,2));
        if(monitors.Any(x=>snapped.Bounds.Intersects(x.Bounds))||VisibleLayouts().Any(x=>x.Id!=l.Id&&snapped.Bounds.Intersects(x.Bounds)))snapped=dragOriginal;
        Replace(snapped);dragging=null;dragOriginal=null;LayoutCanvas.ReleaseMouseCapture();Save();RefreshRows();Draw();
    }
    private void CanvasCaptureLost(object sender,MouseEventArgs e)
    {if(dragOriginal!=null){Replace(dragOriginal);dragOriginal=null;dragging=null;Draw();}}
    private void SaveDiagnostics(object sender,RoutedEventArgs e)
    {var d=new SaveFileDialog{FileName="EasyBlue-diagnostics.txt",Filter="텍스트 파일|*.txt"};if(d.ShowDialog(this)==true)try{File.WriteAllText(d.FileName,string.Join("\n",log));}catch(Exception ex){MessageBox.Show(this,ex.Message,"저장 실패");}}
    private async void OpenLegacyClick(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Title="기존 EBTiny.exe 선택",Filter="EasyBlue 실행파일|EBTiny.exe"};if(d.ShowDialog(this)!=true)return;
        if(input!=null){await input.Stop();input.Emergency("기존 앱으로 전환",true);}if(client!=null)try{await client.CloseSession();}catch(Exception ex){Record("세션 종료: "+ex.Message);}
        transport?.Dispose();transport=null;client=null;PairButton.IsEnabled=false;UsbLabel.Text="○ 기존 앱으로 전환";
        try{Process.Start(new ProcessStartInfo(d.FileName){UseShellExecute=true,WorkingDirectory=System.IO.Path.GetDirectoryName(d.FileName)!});}catch(Exception ex){MessageBox.Show(this,ex.Message,"기존 앱 실행 실패");}
    }
    private async void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(shutdownComplete)return;
        e.Cancel=true;if(!exitRequested){pairGeneration++;Interlocked.Increment(ref switchVersion);pairWindow?.Close();HideToTray();return;}if(closing)return;closing=true;pairGeneration++;Interlocked.Increment(ref switchVersion);
        Hide();if(tray!=null)tray.Visible=false;
        edgeCountdown?.Close();timer.Stop();SystemEvents.UserPreferenceChanged-=SystemPreferenceChanged;SystemEvents.DisplaySettingsChanged-=DisplaysChanged;pairWindow?.Close();
        try{if(storageFinished!=null)await storageFinished.Task;using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(3));if(input!=null)await input.Stop().WaitAsync(stop.Token);if(client!=null)await client.CloseSession(stop.Token);}catch{ }
        finally{tray?.Dispose();input?.Dispose();transport?.Dispose();Save();shutdownComplete=true;Close();}
    }
}
