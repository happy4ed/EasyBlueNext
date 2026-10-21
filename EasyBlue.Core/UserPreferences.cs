using System.Text.Json;
namespace EasyBlue.Core;
public sealed record UserPreferences(double FontSize=13, string Theme="System", bool AutoSwitch=false, double DwellSeconds=0.5, bool AvoidDrag=true, int Version=2, double MouseSpeed=1, bool KoreanCtrlSpace=true)
{
    public bool SpecialKeysEnabled {get;init;}
    public int ActiveSpecialGroup {get;init;}
    public List<SpecialKeyGroup> SpecialKeyGroups {get;init;}=Enumerable.Range(1,4).Select(i=>new SpecialKeyGroup("그룹 "+i,new int?[12])).ToList();
    public ClipboardOptions Clipboard {get;init;}=new();
    public bool LedEnabled {get;init;}=true;
    public bool MouseAcceleration {get;init;}=false;
    public double WheelSpeed {get;init;}=1;
    public ShortcutBinding ReturnShortcut {get;init;}=new(0x7b,3);
    public ShortcutBinding LastDeviceShortcut {get;init;}=new(0x7a,3);
    public Dictionary<string,ShortcutBinding> DeviceShortcuts {get;init;}=[];
    public string? LastDeviceId {get;init;}
    public List<RemoteLayout> Devices {get;init;}=[];
    public Dictionary<string,double> MonitorSizes {get;init;}=[];
    public UserPreferences Validate()
    {
        var returning=ShortcutBinding.IsAllowed(ReturnShortcut)?ReturnShortcut:new(0x7b,3);
        var last=ShortcutBinding.IsAllowed(LastDeviceShortcut)?LastDeviceShortcut:new(0x7a,3);
        return this with {
        Clipboard=(Clipboard??new()).Validate(),
        ActiveSpecialGroup=Math.Clamp(ActiveSpecialGroup,0,3),
        SpecialKeyGroups=Enumerable.Range(0,4).Select(i=>{var g=SpecialKeyGroups?.ElementAtOrDefault(i);return new SpecialKeyGroup(string.IsNullOrWhiteSpace(g?.Name)?"그룹 "+(i+1):g.Name[..Math.Min(32,g.Name.Length)],AdvancedInput.ValidKeys(g?.Keys));}).ToList(),
        DeviceShortcuts=(DeviceShortcuts??[]).Where(x=>ShortcutBinding.IsAllowed(x.Value)&&x.Value!=returning&&x.Value!=last).DistinctBy(x=>x.Value).ToDictionary(x=>x.Key,x=>x.Value),
        ReturnShortcut=returning,
        LastDeviceShortcut=last,
        FontSize=double.IsFinite(FontSize)?Math.Clamp(FontSize,12,18):13,
        WheelSpeed=double.IsFinite(WheelSpeed)?Math.Clamp(WheelSpeed,0.25,3):1,
        MouseSpeed=double.IsFinite(MouseSpeed)?Math.Clamp(MouseSpeed,0.25,2):1,
        Theme=Theme is "Light" or "Dark"?Theme:"System",
        DwellSeconds=double.IsFinite(DwellSeconds)?Math.Clamp(DwellSeconds,0.2,3):0.5,
        Devices=(Devices??[]).Where(x=>x!=null&&double.IsFinite(x.X)&&double.IsFinite(x.Y)&&double.IsFinite(x.Diagonal)&&x.Diagonal is >=3 and <=40&&x.PixelWidth is >=240 and <=16384&&x.PixelHeight is >=240 and <=16384).DistinctBy(x=>x.Id).Take(32).ToList(),
        MonitorSizes=(MonitorSizes??[]).Where(x=>double.IsFinite(x.Value)&&x.Value is >=10 and <=100).ToDictionary(x=>x.Key,x=>x.Value),Version=2};
    }
    public static UserPreferences Load(string path)
    {
        if(!File.Exists(path))return new();
        string json=File.ReadAllText(path);
        var p=JsonSerializer.Deserialize<UserPreferences>(json)??new();
        using var document=JsonDocument.Parse(json);
        if(!document.RootElement.TryGetProperty("Version",out _))p=p with{FontSize=13};
        return p.Validate();
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(Validate(),new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temporary,path,true);
    }
}
