using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyBlue.Core;
namespace EasyBlue.Desktop;
public partial class MainWindow
{
    private static void EnableFirstClick(Window window)
    {
        var source=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        source?.AddHook((nint hwnd,int message,nint wParam,nint lParam,ref bool handled)=>{
            if(message==0x21){handled=true;return 1;} // WM_MOUSEACTIVATE / MA_ACTIVATE: retain the click
            return 0;
        });
    }
    private void ApplyShortcuts()
    {if(input!=null){input.ReturnShortcut=preferences.ReturnShortcut;input.LastDeviceShortcut=preferences.LastDeviceShortcut;input.DeviceShortcuts=new Dictionary<string,ShortcutBinding>(preferences.DeviceShortcuts);}}
    private string ShortcutText(ShortcutBinding binding)
    {
        string text=((binding.Modifiers&1)!=0?"Ctrl+":"")+((binding.Modifiers&2)!=0?"Alt+":"")+((binding.Modifiers&4)!=0?"Shift+":"")+((binding.Modifiers&8)!=0?"Win+":"");
        return text+new KeyConverter().ConvertToString(KeyInterop.KeyFromVirtualKey(binding.Key));
    }
    private void RefreshShortcutLabels()
    {
        ReturnShortcutBox.Text=ShortcutText(preferences.ReturnShortcut);LastShortcutBox.Text=ShortcutText(preferences.LastDeviceShortcut);
        ReturnButton.Content="PC 복귀 · "+ShortcutText(preferences.ReturnShortcut);
    }
    private FrameworkElement? shortcutEditor;
    private void UpdateShortcutCapture()
    {
        if(input!=null)input.EditingShortcut=shortcutEditor?.IsKeyboardFocusWithin==true&&Window.GetWindow(shortcutEditor)?.IsActive==true;
    }
    private async void ShortcutFocus(object sender,KeyboardFocusChangedEventArgs e)
    {shortcutEditor=sender as FrameworkElement;UpdateShortcutCapture();if(input!=null)await input.Stop();}
    private void ShortcutBlur(object sender,KeyboardFocusChangedEventArgs e)
    {if(ReferenceEquals(shortcutEditor,sender))shortcutEditor=null;UpdateShortcutCapture();}
    private void ShortcutKey(object sender,KeyEventArgs e)
    {
        e.Handled=true;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
        if(key==Key.Escape){Keyboard.ClearFocus();return;}
        var m=Keyboard.Modifiers;int bits=((m&ModifierKeys.Control)!=0?1:0)|((m&ModifierKeys.Alt)!=0?2:0)|((m&ModifierKeys.Shift)!=0?4:0)|((m&ModifierKeys.Windows)!=0?8:0);
        var binding=new ShortcutBinding(KeyInterop.VirtualKeyFromKey(key),bits);
        bool returning=ReferenceEquals(sender,ReturnShortcutBox);
        if(!ShortcutBinding.IsAllowed(binding)){StatusText.Text="Ctrl / Alt / Win 조합 또는 F1~F24를 누르세요.";return;}
        if(preferences.DeviceShortcuts.Any(x=>x.Value==binding)){StatusText.Text="이 조합은 기기별 단축키에서 사용 중입니다. 기기 설정에서 먼저 변경해 주세요.";return;}
        preferences=returning?preferences with{ReturnShortcut=binding}:preferences with{LastDeviceShortcut=binding};
        ApplyShortcuts();RefreshShortcutLabels();Save();StatusText.Text="단축키 저장됨 · "+ShortcutText(binding);
    }
    private void ResetShortcuts(object sender,RoutedEventArgs e)
    {if(preferences.DeviceShortcuts.Values.Any(x=>x==new ShortcutBinding(0x7b,3)||x==new ShortcutBinding(0x7a,3))){StatusText.Text="기기별 단축키와 기본값이 겹칩니다. 기기 단축키를 먼저 변경해 주세요.";return;}preferences=preferences with{ReturnShortcut=new(0x7b,3),LastDeviceShortcut=new(0x7a,3)};ApplyShortcuts();RefreshShortcutLabels();Save();}
    private void EditDeviceShortcut(object sender,RoutedEventArgs e)
    {
        string id=(string)((Button)sender).Tag;
        var dialog=NewDialog("기기 전환 단축키",420,260,out var panel);
        panel.Children.Add(new TextBlock{Text=preferences.Devices.First(x=>x.Id==id).Name,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="아래 칸을 클릭하고 원하는 키 조합을 누르세요. 저장을 눌러 적용합니다.",Margin=new(0,8,0,8)});
        ShortcutBinding? selected=preferences.DeviceShortcuts.GetValueOrDefault(id);
        var field=new TextBox{IsReadOnly=true,Text=selected==null?"지정 안 함":ShortcutText(selected)};
        field.GotKeyboardFocus+=ShortcutFocus;field.LostKeyboardFocus+=ShortcutBlur;
        var message=new TextBlock{Margin=new(0,8,0,8)};
        field.PreviewKeyDown+=(_,keyEvent)=>{
            keyEvent.Handled=true;var key=keyEvent.Key==Key.System?keyEvent.SystemKey:keyEvent.Key;
            if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
            var m=Keyboard.Modifiers;var binding=new ShortcutBinding(KeyInterop.VirtualKeyFromKey(key),((m&ModifierKeys.Control)!=0?1:0)|((m&ModifierKeys.Alt)!=0?2:0)|((m&ModifierKeys.Shift)!=0?4:0)|((m&ModifierKeys.Windows)!=0?8:0));
            if(!ShortcutBinding.IsAllowed(binding)){message.Text="Ctrl / Alt / Win 조합 또는 F1~F24를 누르세요.";return;}
            if(binding==preferences.ReturnShortcut||binding==preferences.LastDeviceShortcut||preferences.DeviceShortcuts.Any(x=>x.Key!=id&&x.Value==binding)){message.Text="다른 동작에서 사용하는 조합입니다.";return;}
            selected=binding;field.Text=ShortcutText(binding);message.Text="저장하면 이 기기로 전환합니다.";
        };panel.Children.Add(field);panel.Children.Add(message);
        var clear=new Button{Content="지정 해제",HorizontalAlignment=HorizontalAlignment.Left};clear.Click+=(_,_)=>{selected=null;field.Text="지정 안 함";message.Text="저장을 눌러 적용하세요.";};panel.Children.Add(clear);
        var save=new Button{Content="저장",HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(_,_)=>{var map=new Dictionary<string,ShortcutBinding>(preferences.DeviceShortcuts);if(selected==null)map.Remove(id);else map[id]=selected;preferences=preferences with{DeviceShortcuts=map};ApplyShortcuts();Save();RefreshRows();dialog.Close();};panel.Children.Add(save);
        dialog.Closed+=(_,_)=>{if(input!=null)input.EditingShortcut=false;};dialog.ShowDialog();
    }
    private void InitializeTray()
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/EasyBlue.ico"))!.Stream;
        using var icon=new System.Drawing.Icon(stream);
        tray=new System.Windows.Forms.NotifyIcon{Text="EasyBlue",Icon=(System.Drawing.Icon)icon.Clone(),Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("EasyBlue 열기",null,(_,_)=>Dispatcher.BeginInvoke(ShowFromTray));
        menu.Items.Add("PC 입력으로 복귀",null,(_,_)=>Dispatcher.BeginInvoke(()=>input?.Emergency("PC 입력으로 복귀했습니다.",false)));
        menu.Opening+=(_,_)=>PopulateTrayMenu(menu);
        PopulateTrayMenu(menu);
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowFromTray);
    }
    private bool traySwitchQueued;
    private void PopulateTrayMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        // Keep the two permanent actions; rebuild from the same cached device list as the main window.
        while(menu.Items.Count>2){var old=menu.Items[2];menu.Items.RemoveAt(2);old.Dispose();}
        bool available=client!=null&&!closing&&!pairing&&!pairStopping&&!pairRequestQueued&&!traySwitchQueued&&(!busy||polling);
        var last=menu.Items.Add("마지막 기기 연결",null,(_,_)=>Dispatcher.BeginInvoke(async()=>{if(lastTarget!=null)await SwitchFromTray(lastTarget);}));
        last.Enabled=available&&paired.Any(d=>d.Id==lastTarget);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("등록된 기기"){Enabled=false});
        foreach(var device in paired.OrderBy(d=>d.Slot))
        {
            string id=device.Id;
            string name=preferences.Devices.FirstOrDefault(d=>d.Id==id)?.Name??device.Label;
            var item=new System.Windows.Forms.ToolStripMenuItem(name.Replace("&","&&")+" · "+(device.Connected?"연결됨":"등록됨")){
                Checked=device.Connected,Enabled=available,
                ShortcutKeyDisplayString=preferences.DeviceShortcuts.TryGetValue(id,out var shortcut)?ShortcutText(shortcut):""
            };
            item.Click+=(_,_)=>Dispatcher.BeginInvoke(async()=>await SwitchFromTray(id));
            menu.Items.Add(item);
        }
        if(paired.Count==0)menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(client==null?"EasyBlue USB를 연결해 주세요":"등록된 기기가 없습니다"){Enabled=false});
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("종료",null,(_,_)=>Dispatcher.BeginInvoke(()=>{exitRequested=true;Close();}));
    }
    private async Task SwitchFromTray(string id)
    {
        if(traySwitchQueued||closing||pairing||pairStopping||pairRequestQueued||client==null||busy&&!polling)return;
        traySwitchQueued=true;
        try
        {
            // Reserve this click before waiting so the next status poll cannot steal it.
            if(polling&&pollFinished is { } pending)await pending.Task;
            if(!closing&&!pairing&&!pairStopping&&!pairRequestQueued)await Switch(id);
        }
        finally{traySwitchQueued=false;}
    }
    internal void QuitForUpdate(){exitRequested=true;Close();}
    internal void ShowFromTray(){Show();WindowState=WindowState.Normal;Activate();}
    private void HideToTray()
    {HideCountdown();if(tray==null){WindowState=WindowState.Normal;return;}if(input!=null){input.EditingShortcut=false;input.Emergency("창을 트레이로 보내고 PC 입력으로 복귀했습니다.",false);}Hide();}
}
