namespace EasyBlue.Core;
// Original input-only interface retained for the existing frame checks.
public static class FeatureFrame
{
    public static byte[] Encode(ReadOnlySpan<ushort> words)
    {
        if(words.Length is <1 or >12)throw new ArgumentOutOfRangeException(nameof(words));
        return DeviceProtocol.Frame(0xeb01,0,words);
    }
}
