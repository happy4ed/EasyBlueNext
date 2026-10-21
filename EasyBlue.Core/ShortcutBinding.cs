namespace EasyBlue.Core;
// Modifier bits: Ctrl=1, Alt=2, Shift=4, Win=8; virtual key codes are Windows VK values.
public sealed record ShortcutBinding(int Key,int Modifiers)
{
    public static bool IsAllowed(ShortcutBinding? b)=>b!=null&&b.Modifiers is >=0 and <16&&((b.Modifiers&11)!=0||b.Key is >=0x70 and <=0x87)&&b.Key is >=0x20 and <=0xfe&&b.Key is not (0x5b or 0x5c or >=0xa0 and <=0xa5);
    public bool Matches(int key,int modifiers)=>Key==key&&Modifiers==modifiers;
}

public enum ShortcutAction { None, ReturnPc, LastDevice, Device }
public sealed record ShortcutResult(ShortcutAction Action,string? DeviceId=null);
public static class ShortcutRouter
{
    public static ShortcutResult Resolve(int key,int modifiers,bool active,ShortcutBinding returning,ShortcutBinding last,IReadOnlyDictionary<string,ShortcutBinding> devices)
    {
        bool back=returning.Matches(key,modifiers),connect=last.Matches(key,modifiers);
        if(back&&connect)return new(active?ShortcutAction.ReturnPc:ShortcutAction.LastDevice);
        if(back)return new(ShortcutAction.ReturnPc);
        if(connect)return new(ShortcutAction.LastDevice);
        foreach(var pair in devices)if(pair.Value.Matches(key,modifiers))return new(ShortcutAction.Device,pair.Key);
        return new(ShortcutAction.None);
    }
}
