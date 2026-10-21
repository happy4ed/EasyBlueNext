using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EasyBlue.Core;
namespace EasyBlue.Desktop;
internal sealed class EdgeCountdown : Window
{
    private readonly TextBlock label=new(){FontSize=12,Foreground=Brushes.White,Margin=new(0,0,0,6)};
    private readonly ProgressBar progress=new(){Height=3,Minimum=0,Maximum=1,Foreground=new SolidColorBrush(System.Windows.Media.Color.FromRgb(90,195,255)),Background=new SolidColorBrush(System.Windows.Media.Color.FromRgb(70,76,85))};
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern int GetWindowLongW(nint hwnd,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLongW(nint hwnd,int index,int value);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect rect);
    public EdgeCountdown()
    {
        Width=248;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;Focusable=false;IsHitTestVisible=false;
        var stack=new StackPanel();stack.Children.Add(label);stack.Children.Add(progress);
        var bubble=new Border{Child=stack,Padding=new(12,9,12,10),CornerRadius=new(8),Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(240,29,34,43)),BorderBrush=new SolidColorBrush(System.Windows.Media.Color.FromRgb(83,157,220)),BorderThickness=new(1)};
        Content=bubble;
        IsVisibleChanged+=(_,_)=>{
            if(IsVisible)bubble.BeginAnimation(OpacityProperty,new DoubleAnimation(0.75,1,TimeSpan.FromSeconds(0.65)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            else bubble.BeginAnimation(OpacityProperty,null);
        };
        SourceInitialized+=(_,_)=>{nint h=new WindowInteropHelper(this).Handle;SetWindowLongW(h,-20,GetWindowLongW(h,-20)|0x08000000|0x20|0x80);};
    }
    public void Update(string target,double remaining,double duration,Box monitor,int pointerX,int pointerY)
    {
        label.Text=remaining<=0?$"{target}로 전환 준비 중…":$"{Math.Ceiling(Math.Max(0,remaining)*10)/10:0.0}초 후 {target}로 전환";
        progress.Value=Math.Clamp(1-remaining/duration,0,1);
        if(!IsVisible)Show();
        nint h=new WindowInteropHelper(this).Handle;GetWindowRect(h,out var rect);
        int width=Math.Max(1,rect.Right-rect.Left),height=Math.Max(1,rect.Bottom-rect.Top);
        int x=(int)Math.Clamp(pointerX+14,monitor.X+8,Math.Max(monitor.X+8,monitor.Right-width-8));
        int y=(int)Math.Clamp(pointerY+18,monitor.Y+8,Math.Max(monitor.Y+8,monitor.Bottom-height-8));
        SetWindowPos(h,0,x,y,0,0,0x0015); // no size, z-order or activation changes
    }
}
