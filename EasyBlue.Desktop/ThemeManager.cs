using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace EasyBlue.Desktop;
internal static class ThemeManager
{
    public static bool Dark {get;private set;}
    public static void Apply(string preference)
    {
        bool systemDark=false;
        try {systemDark=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0;}catch { }
        Dark=preference=="Dark"||(preference=="System"&&systemDark);
        string[] names=["Background","Panel","Sidebar","Ink","Muted","Border","Accent","Selected","Screen","SoftButton"];
        string[] light=["#FAFAFA","#FFFFFF","#EFEFF1","#222226","#65656D","#E2E2E6","#007AFF","#DFEAFC","#F1F4F8","#F0F0F3"];
        string[] dark=["#202022","#29292C","#252527","#ECECEF","#AAAAAF","#414146","#0A84FF","#273E57","#30363F","#363639"];
        for(int i=0;i<names.Length;i++)Application.Current.Resources[names[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString((Dark?dark:light)[i]));
        if(SystemParameters.HighContrast)
        {
            foreach(var key in new[]{"Background","Panel","Sidebar","Screen","SoftButton"})Application.Current.Resources[key]=SystemColors.WindowBrush;
            foreach(var key in new[]{"Ink","Muted","Border"})Application.Current.Resources[key]=SystemColors.WindowTextBrush;
            Application.Current.Resources["Accent"]=SystemColors.HighlightBrush;
            Application.Current.Resources["Selected"]=SystemColors.ControlBrush;
        }
    }
}
