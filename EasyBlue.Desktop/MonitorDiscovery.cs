using System.Runtime.InteropServices;
using EasyBlue.Core;
namespace EasyBlue.Desktop;
internal sealed record DisplayInfo(string Id,string Name,Box Pixels,Box Bounds,bool Primary,double Diagonal)
{
    public ScreenNode Node=>new(Id,Name,true,Bounds,(int)Pixels.Width,(int)Pixels.Height);
}
internal static class MonitorDiscovery
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Info {public int Size;public Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    private delegate bool EnumProc(nint monitor,nint dc,ref Rect rect,nint data);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(nint dc,nint clip,EnumProc callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfoW(nint monitor,ref Info info);
    public static List<DisplayInfo> Find(IReadOnlyDictionary<string,double> sizes)
    {
        var raw=new List<(string Id,Box Pixels,bool Primary)>();
        EnumProc callback=(nint h,nint dc,ref Rect r,nint data)=>{var i=new Info{Size=Marshal.SizeOf<Info>(),Device=""};if(GetMonitorInfoW(h,ref i))raw.Add((i.Device,new(i.Monitor.Left,i.Monitor.Top,i.Monitor.Right-i.Monitor.Left,i.Monitor.Bottom-i.Monitor.Top),(i.Flags&1)!=0));return true;};
        EnumDisplayMonitors(0,0,callback,0);
        if(raw.Count==0)throw new InvalidOperationException("모니터 정보를 읽지 못했습니다.");
        raw=raw.OrderByDescending(x=>x.Primary).ToList();var result=new List<DisplayInfo>();
        for(int index=0;index<raw.Count;index++)
        {
            var p=raw[index];double diagonal=sizes.GetValueOrDefault(p.Id,27);
            Box b=Geometry.Physical(0,0,diagonal,(int)p.Pixels.Width,(int)p.Pixels.Height);
            var first=result.FirstOrDefault();
            if(first!=null)b=b with{X=(p.Pixels.X-first.Pixels.X)*first.Bounds.Width/first.Pixels.Width,Y=(p.Pixels.Y-first.Pixels.Y)*first.Bounds.Height/first.Pixels.Height};
            foreach(var other in result)
            {
                Box q=other.Pixels;
                if(Math.Abs(p.Pixels.X-q.Right)<1)b=b with{X=other.Bounds.Right,Y=other.Bounds.Y+(p.Pixels.Y-q.Y)*other.Bounds.Height/q.Height};
                else if(Math.Abs(p.Pixels.Right-q.X)<1)b=b with{X=other.Bounds.X-b.Width,Y=other.Bounds.Y+(p.Pixels.Y-q.Y)*other.Bounds.Height/q.Height};
                else if(Math.Abs(p.Pixels.Y-q.Bottom)<1)b=b with{Y=other.Bounds.Bottom,X=other.Bounds.X+(p.Pixels.X-q.X)*other.Bounds.Width/q.Width};
                else if(Math.Abs(p.Pixels.Bottom-q.Y)<1)b=b with{Y=other.Bounds.Y-b.Height,X=other.Bounds.X+(p.Pixels.X-q.X)*other.Bounds.Width/q.Width};
            }
            result.Add(new(p.Id,$"모니터 {index+1}",p.Pixels,b,p.Primary,diagonal));
        }
        return result;
    }
}
