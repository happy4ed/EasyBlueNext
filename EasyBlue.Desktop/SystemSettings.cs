using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace EasyBlue.Desktop;
public partial class MainWindow
{
    private TaskCompletionSource? storageFinished;
    private bool updatingStartup;
    private Task? startupWork;
    private void ShowStartupStatus(StartupStatus status)
    {
        StartupBox.IsChecked=status.Enabled;
        StartupStatusText.Text=status.Message;
    }
    private async Task RefreshStartupStatus(bool migrate=false)
    {
        if(updatingStartup)return;
        updatingStartup=true;StartupBox.IsEnabled=false;StartupRepairButton.IsEnabled=false;
        try
        {
            var work=StartupRegistration.Execute(()=>{
                var current=StartupRegistration.Read();
                // Migrate our Run entry once; never re-enable a task disabled outside the app.
                if(migrate&&current.Healthy&&current.Enabled&&current.Message.StartsWith("이전"))return StartupRegistration.Enable();
                return current;
            });
            startupWork=work;var status=await work;
            if(!closing)ShowStartupStatus(status);
            Record(status.Message);
        }
        catch(Exception ex){StartupStatusText.Text="자동실행 전환 실패: "+StartupRegistration.Error(ex);Record(StartupStatusText.Text);StartupRegistration.Log(StartupStatusText.Text);}
        finally{updatingStartup=false;StartupBox.IsEnabled=true;StartupRepairButton.IsEnabled=true;}
    }
    private async void StartupChanged(object sender,RoutedEventArgs e)
    {
        if(!ready||updatingStartup)return;
        await SetStartup(StartupBox.IsChecked==true);
    }
    private async void RepairStartup(object sender,RoutedEventArgs e)=>await SetStartup(true);
    private void OpenWindowsStartup(object sender,RoutedEventArgs e)
    {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true});}
    private async void CheckStartup(object sender,RoutedEventArgs e)=>await RefreshStartupStatus();
    private async Task SetStartup(bool enabled)
    {
        if(updatingStartup)return;
        updatingStartup=true;StartupBox.IsEnabled=false;StartupRepairButton.IsEnabled=false;
        try
        {
            var work=StartupRegistration.Execute(()=>enabled?StartupRegistration.Enable():StartupRegistration.Disable());startupWork=work;var status=await work;
            ShowStartupStatus(status);StatusText.Text=status.Message;Record(status.Message);
        }
        catch(Exception ex)
        {
            ShowStartupStatus(await StartupRegistration.Execute(StartupRegistration.Read));
            StatusText.Text="자동실행 설정 실패: "+StartupRegistration.Error(ex);StartupStatusText.Text=StatusText.Text;Record(StatusText.Text);StartupRegistration.Log(StatusText.Text);
        }
        finally{updatingStartup=false;StartupBox.IsEnabled=true;StartupRepairButton.IsEnabled=true;}
    }
    private async void LedChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        if(busy||pairing||pairStopping){LedBox.IsChecked=preferences.LedEnabled;StatusText.Text="장치 작업이 끝난 뒤 LED 설정을 변경해 주세요.";return;}
        bool value=LedBox.IsChecked==true;preferences=preferences with{LedEnabled=value};Save();
        if(client==null)return;
        busy=true;
        try{await client.UpdateLed(value);StatusText.Text=value?"동글 LED 켜짐":"동글 LED 꺼짐";}
        catch(Exception ex){Record("LED 설정 실패: "+ex.Message);StatusText.Text="LED 변경 실패: "+ex.Message;}
        finally{busy=false;}
    }
    private async void DongleSettingsClick(object sender,RoutedEventArgs e)
    {
        if(pairing||pairStopping||pairRequestQueued)return;
        pairRequestQueued=true;bool ownsOperation=false;
        try
        {
            if(polling&&pollFinished!=null)await pollFinished.Task;
            if(client==null||busy){StatusText.Text="동글 연결 후 다시 시도해 주세요.";return;}
            ownsOperation=true;busy=true;if(input!=null)await input.Stop();
            var dialog=NewDialog("EasyBlue 동글 이름",460,350,out var panel);
            panel.Children.Add(new TextBlock{Text="휴대폰 Bluetooth 검색 목록에 표시되는 동글 이름입니다."});
            var name=Field(panel,"동글 이름 · 영문·숫자·기호 최대31자","");name.MaxLength=31;
            var note=new TextBlock{Text="현재 동글 이름을 읽고 있습니다…",Margin=new(0,8,0,12)};panel.Children.Add(note);
            var read=new Button{Content="현재 이름 읽기",HorizontalAlignment=HorizontalAlignment.Left};
            var save=new Button{Content="동글에 이름 저장",IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};
            bool working=false,finished=false;
            async Task LoadName()
            {
                if(working||client==null)return;working=true;read.IsEnabled=false;save.IsEnabled=false;
                storageFinished=new(TaskCreationOptions.RunContinuationsAsynchronously);
                try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));name.Text=await client.DongleName(deadline.Token);note.Text="새 이름을 입력한 뒤 저장하세요. 기존 휴대폰에는 이전 이름이 캐시되어 보일 수 있습니다.";save.IsEnabled=true;}
                catch(Exception ex){note.Text="이름 조회 실패: "+ex.Message;Record(note.Text);}
                finally{storageFinished?.TrySetResult();working=false;read.IsEnabled=true;}
            }
            read.Click+=async(_,_)=>await LoadName();panel.Children.Add(read);
            save.Click+=async(_,_)=>{
                if(working||client==null)return;working=true;read.IsEnabled=false;save.IsEnabled=false;name.IsEnabled=false;
                storageFinished=new(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    string value=await client.RenameDongle(name.Text.Trim(),async backup=>{
                        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EasyBlueNext","Backups");Directory.CreateDirectory(directory);
                        string path=Path.Combine(directory,"dongle-name-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");await File.WriteAllTextAsync(path,JsonSerializer.Serialize(backup,new JsonSerializerOptions{WriteIndented=true}));
                    },deadline.Token);
                    note.Text="동글 이름을 저장하고 재확인했습니다: "+value+"\n휴대폰에서 새 이름이 바로 보이지 않으면 Bluetooth 검색을 다시 하거나 기존 등록을 지운 뒤 다시 페어링하세요.";
                    Record("동글 이름 변경 및 저장 검증 완료");
                }
                catch(Exception ex){note.Text="이름 저장 실패: "+ex.Message;Record(note.Text);}
                finally{storageFinished?.TrySetResult();working=false;read.IsEnabled=true;save.IsEnabled=true;name.IsEnabled=true;if(finished)dialog.Close();}
            };panel.Children.Add(save);
            var close=new Button{Content="닫기",HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,8,0,0)};close.Click+=(_,_)=>dialog.Close();panel.Children.Add(close);
            dialog.Closing+=(_,e)=>{if(working){e.Cancel=true;note.Text="장치 작업을 마친 뒤 닫습니다…";finished=true;}};
            dialog.Loaded+=async(_,_)=>{await LoadName();if(finished)dialog.Close();};
            dialog.ShowDialog();
        }
        catch(Exception ex){Record(ex.Message);}
        finally{if(ownsOperation)busy=false;pairRequestQueued=false;}
    }
}
