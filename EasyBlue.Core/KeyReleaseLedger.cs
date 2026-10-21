namespace EasyBlue.Core;

// Keep down commands until a confirmed release sweep, even if key-up was queued.
// Emergency cancellation can discard that key-up while the earlier down is in flight.
public sealed class KeyReleaseLedger
{
    private readonly HashSet<ushort> downs=[];
    private readonly object sync=new();
    public void ObserveDowns(IEnumerable<ushort> words)
    {lock(sync)foreach(ushort word in words)if((word&0xff00) is 0x2400 or 0x2600 or 0x2c00)downs.Add(word);}
    public ushort[] SnapshotReleases()
    {lock(sync)return downs.OrderBy(x=>x).Select(x=>(ushort)(x|0x100)).Append((ushort)0x2800).ToArray();}
    public void ConfirmReleased(IEnumerable<ushort> words)
    {lock(sync)foreach(ushort word in words)if((word&0xff00) is 0x2500 or 0x2700 or 0x2d00)downs.Remove((ushort)(word&0xfeff));}
    public void Reset(){lock(sync)downs.Clear();}
}
