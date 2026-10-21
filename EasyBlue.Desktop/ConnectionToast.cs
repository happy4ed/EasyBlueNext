using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace EasyBlue.Desktop;
internal sealed class ConnectionToast : Window
{
    private readonly TextBlock title=new(){FontSize=13,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock detail=new(){FontSize=11,Margin=new(0,4,0,0)};
    private readonly Border dot=new(){Width=8,Height=8,CornerRadius=new(4),Margin=new(0,4,12,0),VerticalAlignment=VerticalAlignment.Top};
    private readonly DispatcherTimer dismiss=new();
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern int GetWindowLongW(nint hwnd,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLongW(nint hwnd,int index,int value);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    public ConnectionToast()
    {
        Width=310;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;Focusable=false;IsHitTestVisible=false;
        title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var labels=new StackPanel();labels.Children.Add(title);labels.Children.Add(detail);
        var row=new DockPanel();DockPanel.SetDock(dot,Dock.Left);row.Children.Add(dot);row.Children.Add(labels);
        var bubble=new Border{Child=row,CornerRadius=new(12),Padding=new(16,13,16,14),BorderThickness=new(1)};
        bubble.SetResourceReference(Border.BackgroundProperty,"Panel");bubble.SetResourceReference(Border.BorderBrushProperty,"Border");Content=bubble;
        SourceInitialized+=(_,_)=>{nint h=new WindowInteropHelper(this).Handle;SetWindowLongW(h,-20,GetWindowLongW(h,-20)|0x08000000|0x20|0x80);};
        dismiss.Tick+=(_,_)=>{dismiss.Stop();Hide();};Closed+=(_,_)=>dismiss.Stop();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)dot.BeginAnimation(OpacityProperty,null);};
    }
    public void Update(string device,string state,string message,int milliseconds=0)
    {
        dismiss.Stop();dot.BeginAnimation(OpacityProperty,null);dot.Opacity=1;
        title.Text=state=="pending"?device+"에 연결 중…":state=="success"?device+" 연결됨":state=="cancel"?"기기 전환 취소":"기기 연결 실패";
        detail.Text=message;
        if(state=="pending")
        {
            dot.SetResourceReference(Border.BackgroundProperty,"Accent");
            dot.BeginAnimation(OpacityProperty,new DoubleAnimation(0.35,1,TimeSpan.FromSeconds(0.6)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        }
        else dot.Background=new SolidColorBrush(state=="success"?Color.FromRgb(48,176,109):state=="cancel"?Color.FromRgb(142,142,147):Color.FromRgb(230,122,54));
        if(!IsVisible)Show();UpdateLayout();
        InputEngine.GetCursorPos(out var point);var monitor=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y));
        var area=monitor.WorkingArea;nint h=new WindowInteropHelper(this).Handle;GetWindowRect(h,out var rect);
        int width=rect.Right-rect.Left;
        SetWindowPos(h,0,area.Left+Math.Max(0,(area.Width-width)/2),area.Top+24,0,0,0x0015);
        if(milliseconds>0){dismiss.Interval=TimeSpan.FromMilliseconds(milliseconds);dismiss.Start();}
    }
}
