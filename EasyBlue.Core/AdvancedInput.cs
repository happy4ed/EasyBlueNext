namespace EasyBlue.Core;
public sealed record SpecialKeyGroup(string Name,int?[] Keys);
public sealed record ClipboardOptions(bool CtrlVEnabled=false,string TargetOs="Android",string Layout="Korean",string LanguageSwitch="CtrlSpace",bool InitiallyKorean=false,int DelayMs=30)
{
    public ClipboardOptions Validate()=>this with{TargetOs=TargetOs is "iOS" or "Windows" or "macOS"?TargetOs:"Android",Layout=Layout=="US"?"US":"Korean",LanguageSwitch=LanguageSwitch is "ShiftSpace" or "Hangul" or "AltShift"?LanguageSwitch:"CtrlSpace",DelayMs=Math.Clamp(DelayMs,10,200)};
}
public sealed record InputStep(ushort[] Words,int DelayMs=10);
public static class AdvancedInput
{
    // Recovered from original 0x4B82E0; 0x2C/0x2D are press/release, not two usage pages.
    public static readonly IReadOnlyDictionary<int,string> SpecialKeys=new Dictionary<int,string>{
        [1]="전원",[2]="메뉴",[3]="빨리 감기",[4]="되감기",[5]="다음 곡",[6]="이전 곡",[7]="정지",[8]="꺼내기",[9]="재생 / 일시정지",[10]="음소거",[11]="볼륨 높이기",[12]="볼륨 낮추기",[13]="이메일",[14]="브라우저",[15]="화면 보호기",[16]="검색",[17]="홈",[18]="뒤로"};
    public static int?[] ValidKeys(int?[]? keys)=>Enumerable.Range(0,12).Select(i=>keys!=null&&i<keys.Length&&keys[i] is int v&&SpecialKeys.ContainsKey(v)?keys[i]:null).ToArray();
    public static ushort SpecialCommand(int code,bool up)
    {if(!SpecialKeys.ContainsKey(code))throw new ArgumentOutOfRangeException(nameof(code));return (ushort)((up?0x2d00:0x2c00)|code);}
    public static ushort[] Stroke(int usage,int modifiers=0)
    {
        var words=new List<ushort>();
        if(modifiers!=0)words.Add((ushort)(0x2600|modifiers));
        words.Add((ushort)(0x2400|usage));words.Add((ushort)(0x2500|usage));
        if(modifiers!=0)words.Add((ushort)(0x2700|modifiers));return words.ToArray();
    }
    public static (int Usage,int Modifiers)? Ascii(char c)
    {
        if(c is >= 'a' and <= 'z')return(c-'a'+4,0);
        if(c is >= 'A' and <= 'Z')return(c-'A'+4,2);
        if(c is >= '1' and <= '9')return(c-'1'+30,0);
        if(c=='0')return(39,0);
        string plain="-=[]\\;\'`,./",shift="_+{}|:\"~<>?";
        int[] ids=[45,46,47,48,49,51,52,53,54,55,56];
        int i=plain.IndexOf(c);if(i>=0)return(ids[i],0);i=shift.IndexOf(c);if(i>=0)return(ids[i],2);
        i="!@#$%^&*()".IndexOf(c);if(i>=0)return(i==9?39:30+i,2);
        return c switch{' ' =>(44,0),'\n'=>(40,0),'\t'=>(43,0),_=>null};
    }
    private static string? Korean(char c)
    {
        string[] leading=["r","R","s","e","E","f","a","q","Q","t","T","d","w","W","c","z","x","v","g"];
        string[] vowels=["k","o","i","O","j","p","u","P","h","hk","ho","hl","y","n","nj","np","nl","b","m","ml","l"];
        string[] trailing=["","r","R","rt","s","sw","sg","e","f","fr","fa","fq","ft","fx","fv","fg","a","q","qt","t","T","d","w","c","z","x","v","g"];
        string[] consonants=["r","R","rt","s","sw","sg","e","E","f","fr","fa","fq","ft","fx","fv","fg","a","q","Q","qt","t","T","d","w","W","c","z","x","v","g"];
        if(c is >= '\uac00' and <= '\ud7a3'){int n=c-0xac00;return leading[n/588]+vowels[n/28%21]+trailing[n%28];}
        if(c is >= '\u3131' and <= '\u314e')return consonants[c-0x3131];
        if(c is >= '\u314f' and <= '\u3163')return vowels[c-0x314f];
        if(c is >= '\u1100' and <= '\u1112')return leading[c-0x1100];
        if(c is >= '\u1161' and <= '\u1175')return vowels[c-0x1161];
        if(c is >= '\u11a8' and <= '\u11c2')return trailing[c-0x11a7];
        return null;
    }
    // Build the entire plan before sending: unsupported Unicode never results in a partial paste.
    public static IReadOnlyList<InputStep> ClipboardPlan(string text,ClipboardOptions options)
    {
        options=options.Validate();if(text.Length>20000)throw new ArgumentException("한 번에 최대 20,000자까지 전송할 수 있습니다.");
        text=text.Replace("\r\n","\n").Replace('\r','\n');
        var result=new List<InputStep>();bool korean=options.InitiallyKorean&&options.Layout=="Korean";
        void Switch(bool next)
        {
            if(next==korean)return;
            var stroke=options.LanguageSwitch switch{"ShiftSpace"=>Stroke(44,2),"Hangul"=>Stroke(0x90),"AltShift"=>new ushort[]{0x2604,0x2602,0x2702,0x2704},_=>Stroke(44,1)};
            result.Add(new(stroke,150));korean=next;
        }
        foreach(char c in text)
        {
            var ascii=Ascii(c);
            if(ascii.HasValue)
            {
                if(char.IsLetter(c))Switch(false);
                result.Add(new(Stroke(ascii.Value.Usage,ascii.Value.Modifiers),options.DelayMs));continue;
            }
            string? keys=options.Layout=="Korean"?Korean(c):null;
            if(keys==null)throw new ArgumentException($"이 키보드 방식으로 전송할 수 없는 문자(U+{(int)c:X4})가 있습니다. 영어/한국어 두벌식 텍스트를 사용하세요. 이모지·이미지는 지원하지 않습니다.");
            Switch(true);foreach(char key in keys){var a=Ascii(key)!.Value;result.Add(new(Stroke(a.Usage,a.Modifiers),options.DelayMs));}
        }
        Switch(options.InitiallyKorean&&options.Layout=="Korean");return result;
    }
}
