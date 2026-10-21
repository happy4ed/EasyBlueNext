using System.Windows;
using System.Windows.Controls;
using EasyBlue.Core;
namespace EasyBlue.Desktop;
public partial class MainWindow
{
    private bool clipboardSending;
    private void ApplyAdvancedInput()
    {
        if(input==null)return;
        input.ClipboardCtrlV=preferences.Clipboard.CtrlVEnabled;
        input.SpecialKeyMap=preferences.SpecialKeysEnabled?preferences.SpecialKeyGroups[preferences.ActiveSpecialGroup].Keys:new int?[12];
    }
    private void SpecialKeySettings(object sender,RoutedEventArgs e)
    {
        var dialog=NewDialog("특수키 그룹",490,610,out var panel);bool loading=true;
        var enabled=new CheckBox{Content="입력 중 F1~F12를 특수키로 변환",IsChecked=preferences.SpecialKeysEnabled};panel.Children.Add(enabled);
        var groups=new ComboBox{ItemsSource=Enumerable.Range(1,4).Select(i=>"그룹 "+i).ToArray(),SelectedIndex=preferences.ActiveSpecialGroup,Margin=new(0,8,0,8)};panel.Children.Add(groups);
        var name=Field(panel,"그룹 이름",preferences.SpecialKeyGroups[groups.SelectedIndex].Name);name.MaxLength=32;
        var list=new StackPanel();var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=330,Margin=new(0,8,0,8)};panel.Children.Add(scroll);
        var options=new[]{new KeyValuePair<int,string>(0,"기본 F키")}.Concat(AdvancedInput.SpecialKeys).ToArray();
        void LoadGroup()
        {
            loading=true;int group=groups.SelectedIndex;name.Text=preferences.SpecialKeyGroups[group].Name;list.Children.Clear();
            for(int i=0;i<12;i++)
            {
                int slot=i;var row=new DockPanel{Margin=new(0,3,0,3)};row.Children.Add(new TextBlock{Text="F"+(i+1),Width=45,VerticalAlignment=VerticalAlignment.Center});
                var combo=new ComboBox{ItemsSource=options,DisplayMemberPath="Value",SelectedValuePath="Key",SelectedValue=preferences.SpecialKeyGroups[group].Keys[i]??0};
                combo.SelectionChanged+=(_,_)=>{if(loading)return;var old=preferences.SpecialKeyGroups[group];var keys=old.Keys.ToArray();keys[slot]=combo.SelectedValue is int value&&value!=0?value:null;preferences.SpecialKeyGroups[group]=old with{Keys=keys};Save();ApplyAdvancedInput();};
                row.Children.Add(combo);list.Children.Add(row);
            }
            loading=false;
        }
        groups.SelectionChanged+=(_,_)=>{if(loading)return;preferences=preferences with{ActiveSpecialGroup=groups.SelectedIndex};LoadGroup();Save();ApplyAdvancedInput();};
        name.LostFocus+=(_,_)=>{int group=groups.SelectedIndex;preferences.SpecialKeyGroups[group]=preferences.SpecialKeyGroups[group] with{Name=string.IsNullOrWhiteSpace(name.Text)?"그룹 "+(group+1):name.Text.Trim()};Save();};
        enabled.Click+=(_,_)=>{preferences=preferences with{SpecialKeysEnabled=enabled.IsChecked==true};Save();ApplyAdvancedInput();};
        panel.Children.Add(new TextBlock{Text="전환 단축키가 우선합니다. 홈·뒤로·미디어 키의 지원 여부는 연결 기기에 따라 다릅니다. 전원 키는 화면을 끌 수 있습니다.",FontSize=12,Margin=new(0,4,0,8)});
        var close=new Button{Content="완료",HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>dialog.Close();panel.Children.Add(close);LoadGroup();dialog.ShowDialog();
    }
    private void ClipboardSettings(object sender,RoutedEventArgs e)
    {
        var dialog=NewDialog("클립보드 입력 설정",480,570,out var panel);bool loading=true;
        var current=preferences.Clipboard;
        var ctrl=new CheckBox{Content="기기 입력 중 Ctrl+V로 PC 클립보드 텍스트 전송",IsChecked=current.CtrlVEnabled};panel.Children.Add(ctrl);
        ComboBox Choice(string title,string[] values,string selected)
        {panel.Children.Add(new TextBlock{Text=title,Margin=new(0,8,0,4)});var box=new ComboBox{ItemsSource=values,SelectedItem=selected};panel.Children.Add(box);return box;}
        var os=Choice("대상 운영체제",["Android","iOS","Windows","macOS"],current.TargetOs);
        var locale=Choice("대상 기기의 하드웨어 키보드 배열",["Korean","US"],current.Layout);
        var language=Choice("한/영 전환 키 · 대상 기기의 설정과 맞춰 주세요",["CtrlSpace","ShiftSpace","Hangul","AltShift"],current.LanguageSwitch);
        var initial=new CheckBox{Content="전송 시작 시 대상 키보드가 한글 상태",IsChecked=current.InitiallyKorean,Margin=new(0,12,0,8)};panel.Children.Add(initial);
        panel.Children.Add(new TextBlock{Text="문자 사이 대기 시간 · 빠르면 누락될 수 있습니다."});
        var delay=new Slider{Minimum=10,Maximum=200,Value=current.DelayMs,TickFrequency=10,IsSnapToTickEnabled=true,Margin=new(0,8,0,4)};panel.Children.Add(delay);
        var label=new TextBlock{Text=current.DelayMs+" ms"};panel.Children.Add(label);
        panel.Children.Add(new TextBlock{Text="텍스트를 Bluetooth 키 입력으로 변환합니다. US 영문·기호와 한국어 두벌식을 지원합니다. 대상 기기도 같은 배열로 설정하세요. 이모지·이미지·서식은 전송하지 않습니다. 미지원 문자가 있으면 전송 전에 중단합니다. Esc 또는 PC 복귀 단축키로 취소할 수 있습니다.",FontSize=12,Margin=new(0,10,0,10)});
        void SaveOptions()
        {
            if(loading)return;
            preferences=preferences with{Clipboard=new ClipboardOptions(ctrl.IsChecked==true,(string)os.SelectedItem,(string)locale.SelectedItem,(string)language.SelectedItem,initial.IsChecked==true,(int)delay.Value).Validate()};
            label.Text=(int)delay.Value+" ms";Save();ApplyAdvancedInput();
        }
        ctrl.Click+=(_,_)=>SaveOptions();initial.Click+=(_,_)=>SaveOptions();locale.SelectionChanged+=(_,_)=>SaveOptions();language.SelectionChanged+=(_,_)=>SaveOptions();delay.ValueChanged+=(_,_)=>SaveOptions();
        os.SelectionChanged+=(_,_)=>{if(loading)return;language.SelectedItem=(string)os.SelectedItem switch{"Android"=>"ShiftSpace","Windows"=>"Hangul",_=>"CtrlSpace"};SaveOptions();};
        var close=new Button{Content="완료",HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>dialog.Close();panel.Children.Add(close);loading=false;dialog.ShowDialog();
    }
    private async Task<string?> ReadClipboardText()
    {
        for(int i=0;i<3;i++)
        {
            try{return Clipboard.ContainsText()?Clipboard.GetText():null;}
            catch(System.Runtime.InteropServices.COMException)when(i<2){await Task.Delay(60);}
        }
        return null;
    }
    private async void ClipboardRequested()
    {
        if(clipboardSending||input?.Active!=true)return;
        try{var text=await ReadClipboardText();if(!string.IsNullOrEmpty(text))await SendText(text,input.TargetId!);else StatusText.Text="PC 클립보드에 텍스트가 없습니다.";}
        catch(Exception ex){input?.Emergency("클립보드 전송을 시작하지 못해 PC로 복귀했습니다.",false);StatusText.Text=ex.Message;}
    }
    private async void PasteTextClick(object sender,RoutedEventArgs e)
    {
        if(busy||pairing||pairStopping||clipboardSending)return;
        if(input!=null)await input.Stop();
        var dialog=NewDialog("기기에 텍스트 보내기",520,510,out var panel);
        panel.Children.Add(new TextBlock{Text="대상 기기의 입력 칸에 커서를 놓으세요. 내용을 확인한 뒤 전송합니다."});
        var devices=paired.Select(d=>new{d.Id,Name=preferences.Devices.FirstOrDefault(l=>l.Id==d.Id)?.Name??d.Label}).ToArray();
        var target=new ComboBox{ItemsSource=devices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=lastTarget,Margin=new(0,10,0,8)};if(target.SelectedIndex<0&&devices.Length>0)target.SelectedIndex=0;panel.Children.Add(target);
        var text=new TextBox{AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,Height=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(text);
        var message=new TextBlock{Margin=new(0,8,0,8)};panel.Children.Add(message);
        var read=new Button{Content="PC 클립보드 읽기",HorizontalAlignment=HorizontalAlignment.Left};
        read.Click+=async(_,_)=>{try{text.Text=await ReadClipboardText()??"";message.Text="텍스트를 확인한 뒤 전송을 눌러 주세요.";}catch(Exception ex){message.Text=ex.Message;}};panel.Children.Add(read);
        var send=new Button{Content="전송",HorizontalAlignment=HorizontalAlignment.Right,IsEnabled=devices.Length>0};
        send.Click+=async(_,_)=>{
            try{AdvancedInput.ClipboardPlan(text.Text,preferences.Clipboard);}catch(Exception ex){message.Text=ex.Message;return;}
            if(target.SelectedValue is not string id||string.IsNullOrEmpty(text.Text))return;
            string value=text.Text;dialog.Close();await SendText(value,id);
        };panel.Children.Add(send);dialog.ShowDialog();
    }
    private async Task SendText(string text,string id)
    {
        if(clipboardSending||busy||pairing||pairStopping||client==null||input==null)return;
        clipboardSending=true;bool ownsBusy=false;
        try
        {
            var plan=AdvancedInput.ClipboardPlan(text,preferences.Clipboard);
            if(!input.Active||input.TargetId!=id)await Switch(id);
            if(!input.Active||input.TargetId!=id)return;
            busy=true;ownsBusy=true;StatusText.Text="텍스트 전송 중 · Esc 또는 PC 복귀 단축키로 취소";
            await input.RunScript(plan);
            StatusText.Text=$"텍스트 {text.Length}자 전송 완료";Record("클립보드 텍스트 전송 완료 · 내용 기록 안 함");
        }
        catch(Exception ex){input.Emergency("텍스트 전송이 중단되어 PC로 복귀했습니다.",false);StatusText.Text="텍스트 전송 중단: "+ex.Message;Record(StatusText.Text);}
        finally{if(ownsBusy)busy=false;clipboardSending=false;}
    }
}
