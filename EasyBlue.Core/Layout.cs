namespace EasyBlue.Core;

public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x>=X && x<=Right && y>=Y && y<=Bottom;
    public bool Intersects(Box b) => X<b.Right-0.001 && b.X<Right-0.001 && Y<b.Bottom-0.001 && b.Y<Bottom-0.001;
}
public sealed record ScreenNode(string Id, string Name, bool IsMonitor, Box Bounds, int PixelWidth, int PixelHeight);
public sealed record Attachment(string DeviceId, string MonitorId, string Edge, double Start, double End);
public sealed record RemoteLayout(string Id, string Name, double X, double Y, double Diagonal=7, int PixelWidth=1080, int PixelHeight=2400)
{
    public Box Bounds => Geometry.Physical(X,Y,Diagonal,PixelWidth,PixelHeight);
}
public static class Geometry
{
    public static double? PcEdgeAxis(Attachment route,Box pixels,Box physical,double x,double y)
    {
        if(x<pixels.X||x>=pixels.Right||y<pixels.Y||y>=pixels.Bottom)return null;
        bool edge=route.Edge switch{"Left"=>x<=pixels.X+3,"Right"=>x>=pixels.Right-4,"Top"=>y<=pixels.Y+3,"Bottom"=>y>=pixels.Bottom-4,_=>false};
        if(!edge)return null;
        double axis=route.Edge is "Left" or "Right"?physical.Y+(y-pixels.Y)/Math.Max(1,pixels.Height-1)*physical.Height:physical.X+(x-pixels.X)/Math.Max(1,pixels.Width-1)*physical.Width;
        return axis>=route.Start&&axis<=route.End?axis:null;
    }
    public static Box Physical(double x,double y,double diagonal,int width,int height)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(diagonal)||diagonal<3||diagonal>100||width<1||height<1)
            throw new ArgumentOutOfRangeException(nameof(diagonal));
        double unit=diagonal/Math.Sqrt((double)width*width+(double)height*height);
        return new(x,y,width*unit,height*unit);
    }
    public static Attachment? Contact(ScreenNode device,IEnumerable<ScreenNode> monitors)
    => Contacts(device,monitors).FirstOrDefault();
    public static IReadOnlyList<Attachment> Contacts(ScreenNode device,IEnumerable<ScreenNode> screens)
    {
        var contacts=new List<Attachment>();
        Box a=device.Bounds;
        foreach(var m in screens.Where(x=>x.Id!=device.Id))
        {
            Box b=m.Bounds;
            double top=Math.Max(a.Y,b.Y),bottom=Math.Min(a.Bottom,b.Bottom),left=Math.Max(a.X,b.X),right=Math.Min(a.Right,b.Right);
            if(bottom-top>0.05 && Math.Abs(a.X-b.Right)<0.001)contacts.Add(new(device.Id,m.Id,"Right",top,bottom));
            if(bottom-top>0.05 && Math.Abs(a.Right-b.X)<0.001)contacts.Add(new(device.Id,m.Id,"Left",top,bottom));
            if(right-left>0.05 && Math.Abs(a.Y-b.Bottom)<0.001)contacts.Add(new(device.Id,m.Id,"Bottom",left,right));
            if(right-left>0.05 && Math.Abs(a.Bottom-b.Y)<0.001)contacts.Add(new(device.Id,m.Id,"Top",left,right));
        }
        return contacts;
    }
    public static RemoteLayout Snap(RemoteLayout device,IReadOnlyList<ScreenNode> monitors,IReadOnlyList<RemoteLayout> others,double distance=0.45)
    {
        Box a=device.Bounds;
        var candidates=new List<RemoteLayout>{device};
        foreach(var m in monitors)
        {
            Box b=m.Bounds;
            if(Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Y,b.Y)>0.05)
            {
                if(Math.Abs(a.X-b.Right)<=distance)candidates.Add(device with{X=b.Right});
                if(Math.Abs(a.Right-b.X)<=distance)candidates.Add(device with{X=b.X-a.Width});
            }
            if(Math.Min(a.Right,b.Right)-Math.Max(a.X,b.X)>0.05)
            {
                if(Math.Abs(a.Y-b.Bottom)<=distance)candidates.Add(device with{Y=b.Bottom});
                if(Math.Abs(a.Bottom-b.Y)<=distance)candidates.Add(device with{Y=b.Y-a.Height});
            }
        }
        // Prefer actual edge attachments over the unsnapped candidate.
        return candidates.Where(c=>!monitors.Any(m=>c.Bounds.Intersects(m.Bounds))&&!others.Any(o=>o.Id!=c.Id&&c.Bounds.Intersects(o.Bounds)))
            .OrderByDescending(c=>Contact(new(c.Id,c.Name,false,c.Bounds,c.PixelWidth,c.PixelHeight),monitors)!=null)
            .ThenBy(c=>Math.Abs(c.X-device.X)+Math.Abs(c.Y-device.Y)).FirstOrDefault() ?? device;
    }
}
public sealed class DwellTracker
{
    private string? target;
    private long entered;
    private bool fired;
    public string? Update(string? next,long now,double seconds,bool canFire=true)
    {
        if(next!=target){target=next;entered=now;fired=false;return null;}
        if(next==null||fired||!canFire||now-entered<seconds*1000)return null;
        fired=true;return next;
    }
    public double RemainingSeconds(long now,double seconds)=>target==null||fired?0:Math.Clamp(seconds-(now-entered)/1000d,0,seconds);
    public void Reset(){target=null;fired=false;}
}
