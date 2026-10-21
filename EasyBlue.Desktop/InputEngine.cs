using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using EasyBlue.Core;
namespace EasyBlue.Desktop;

internal sealed class InputEngine : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]public struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseData {public Point Position;public uint Data,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct KeyData {public uint Virtual,Scan,Flags,Time;public nuint Extra;}
    private delegate nint HookProc(int code,nuint message,nint data);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookExW(int kind,HookProc callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nuint message,nint data);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    public static bool PcDragging=>(GetAsyncKeyState(1)&0x8000)!=0||(GetAsyncKeyState(2)&0x8000)!=0||(GetAsyncKeyState(4)&0x8000)!=0;
    [DllImport("user32.dll",EntryPoint="GetPhysicalCursorPos")]public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern bool SetPhysicalCursorPos(int x,int y);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandleW(string? name);
    private readonly HookProc mouseCallback,keyCallback;
    private nint mouseHook,keyHook;
    private readonly Channel<(long Epoch,ushort[] Words)> commands=Channel.CreateBounded<(long,ushort[])>(new BoundedChannelOptions(128){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
    private readonly CancellationTokenSource lifetime=new();
    private readonly HashSet<uint> physicalKeys=[];
    private readonly KeyReleaseLedger held=new();
    private readonly SemaphoreSlim releaseGate=new(1,1);
    private readonly HashSet<uint> consumed=[];
    private readonly object sync=new();
    private readonly System.Threading.Timer watchdog;
    private DeviceClient? client;
    private volatile bool active;
    private bool releaseConfirmed;
    private long epoch,heartbeat=Environment.TickCount64,lastAck=Environment.TickCount64,queuedAt;
    private int queued,pendingX,pendingY;
    private long motionStarted;
    private Point anchor,restore;
    private double vx,vy,maxX,maxY;
    private int buttons;
    public string? TargetId {get;private set;}
    public double Speed {get;set;}=1;
    public bool Acceleration {get;set;}
    public double WheelSpeed {get;set;}=1;
    private double wheelRemainder;
    public bool KoreanCtrlSpace {get;set;}=true;
    public bool Active=>active;
    public bool Dragging=>Volatile.Read(ref buttons)!=0;
    public (double X,double Y) VirtualPosition {get {lock(sync)return(vx,vy);}}
    public event Action<string>? Released;
    public event Action<string>? DeviceRequested;
    public IReadOnlyDictionary<string,ShortcutBinding> DeviceShortcuts {get;set;}=new Dictionary<string,ShortcutBinding>();
    public event Action? ReturnRequested;
    public event Action? LastDeviceRequested;
    public ShortcutBinding ReturnShortcut {get;set;}=new(0x7b,3);
    public ShortcutBinding LastDeviceShortcut {get;set;}=new(0x7a,3);
    public bool EditingShortcut {get;set;}

    public InputEngine()
    {
        mouseCallback=Mouse;keyCallback=Keyboard;
        watchdog=new(_=>{
            long now=Environment.TickCount64;
            bool pending=Volatile.Read(ref queued)>0||Volatile.Read(ref pendingX)!=0||Volatile.Read(ref pendingY)!=0;
            long since=Volatile.Read(ref queued)>0?Interlocked.Read(ref queuedAt):now;
            long motion=Interlocked.Read(ref motionStarted);if(motion!=0)since=Math.Min(since,motion);
            if(active&&(now-Interlocked.Read(ref heartbeat)>750||(pending&&now-Math.Max(Interlocked.Read(ref lastAck),since)>750)))Emergency("입력 응답이 지연되어 PC로 복귀했습니다.",true);
        },null,100,100);
        _=Task.Run(Worker);
    }
    public static bool Warp(int x,int y)=>SetPhysicalCursorPos(x,y);
    public void Pulse()=>Interlocked.Exchange(ref heartbeat,Environment.TickCount64);
    public void StartMonitoring()
    {
        if(mouseHook!=0&&keyHook!=0)return;
        RemoveHooks();physicalKeys.Clear();consumed.Clear();nint module=GetModuleHandleW(null);
        mouseHook=SetWindowsHookExW(14,mouseCallback,module,0);keyHook=SetWindowsHookExW(13,keyCallback,module,0);
        if(mouseHook==0||keyHook==0){RemoveHooks();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"입력 감시를 시작하지 못했습니다.");}
    }
    public void Activate(DeviceClient device,string id,int width,int height,Box pixels,double entryX=0.5,double entryY=0.5)
    {
        if(Volatile.Read(ref queued)!=0)throw new InvalidOperationException("이전 입력을 정리 중입니다. 잠시 후 다시 전환하세요.");
        StartMonitoring();client=device;GetCursorPos(out restore);anchor=new(){X=(int)(pixels.X+pixels.Width/2),Y=(int)(pixels.Y+pixels.Height/2)};
        lock(sync){maxX=width-1;maxY=height-1;vx=maxX*Math.Clamp(entryX,0,1);vy=maxY*Math.Clamp(entryY,0,1);held.Reset();buttons=0;wheelRemainder=0;}
        if(!SetPhysicalCursorPos(anchor.X,anchor.Y))throw new InvalidOperationException("커서 기준점을 설정하지 못했습니다.");
        Interlocked.Increment(ref epoch);TargetId=id;lastAck=Environment.TickCount64;heartbeat=lastAck;active=true;
    }
    private bool Enqueue(ushort[] words)
    {
        if(!active)return false;
        // Retain every down seen in this capture session until the release sweep.
        // A queued key-up may be discarded by emergency cancellation before it is sent.
        held.ObserveDowns(words);
        if(Interlocked.Increment(ref queued)==1)Interlocked.Exchange(ref queuedAt,Environment.TickCount64);
        if(commands.Writer.TryWrite((Interlocked.Read(ref epoch),words)))return true;
        Interlocked.Decrement(ref queued);Emergency("입력 큐가 가득 차 PC로 복귀했습니다.",false);return false;
    }
    private async Task Worker()
    {
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                bool work=false;
                while(commands.Reader.TryRead(out var item))
                {
                    work=true;
                    try
                    {
                        if(active&&item.Epoch==Interlocked.Read(ref epoch)&&client!=null)
                        {await FlushMotion();if(active&&item.Epoch==Interlocked.Read(ref epoch)){await client.Command(item.Words,lifetime.Token);Interlocked.Exchange(ref lastAck,Environment.TickCount64);}}
                    }
                    catch(Exception ex)when(ex is not OperationCanceledException){Emergency("장치 전송 오류: "+ex.Message,false);}
                    finally{Interlocked.Decrement(ref queued);}
                }
                try{if(active)await FlushMotion();}
                catch(Exception ex)when(ex is not OperationCanceledException){Emergency("마우스 전송 오류: "+ex.Message,false);}
                if(!work)await Task.Delay(8,lifetime.Token);
            }
        }
        catch(OperationCanceledException){ }
    }
    private async Task FlushMotion()
    {
        int dx=Interlocked.Exchange(ref pendingX,0),dy=Interlocked.Exchange(ref pendingY,0);
        long began=Interlocked.Exchange(ref motionStarted,0);
        if((dx==0&&dy==0)||!active||client==null)return;
        long generation=Interlocked.Read(ref epoch);
        if(Interlocked.Increment(ref queued)==1)Interlocked.Exchange(ref queuedAt,began==0?Environment.TickCount64:began);
        try
        {
            foreach(var words in DeviceProtocol.Motion(dx,dy))
            {
                if(!active||generation!=Interlocked.Read(ref epoch))break;
                await client.Command(words,lifetime.Token);Interlocked.Exchange(ref lastAck,Environment.TickCount64);
            }
        }
        finally{Interlocked.Decrement(ref queued);}
    }
    private nint Mouse(int code,nuint message,nint pointer)
    {
        if(code<0||!active)return CallNextHookEx(mouseHook,code,message,pointer);
        try
        {
            var data=Marshal.PtrToStructure<MouseData>(pointer);
            if((data.Flags&1)!=0)return CallNextHookEx(mouseHook,code,message,pointer);
            uint m=(uint)message;
            if(m==0x200)
            {
                int x=data.Position.X-anchor.X,y=data.Position.Y-anchor.Y;
                if(x!=0||y!=0)
                {
                    double gain=Speed*(Acceleration?Math.Clamp(1+(Math.Sqrt((double)x*x+(double)y*y)-8)/20,1,2):1);
                    int dx=(int)Math.Round(x*gain),dy=(int)Math.Round(y*gain);
                    lock(sync){vx=Math.Clamp(vx+dx,0,maxX);vy=Math.Clamp(vy+dy,0,maxY);}
                    Interlocked.CompareExchange(ref motionStarted,Environment.TickCount64,0);
                    int totalX=Interlocked.Add(ref pendingX,dx),totalY=Interlocked.Add(ref pendingY,dy);
                    if(Math.Abs((long)totalX)>100000||Math.Abs((long)totalY)>100000){Emergency("마우스 전송이 밀려 PC로 복귀했습니다.",false);return CallNextHookEx(mouseHook,code,message,pointer);}
                    if(!SetPhysicalCursorPos(anchor.X,anchor.Y)){Emergency("커서 위치 갱신에 실패해 PC로 복귀했습니다.",false);return CallNextHookEx(mouseHook,code,message,pointer);}
                }
                return 1;
            }
            int mask=m is 0x201 or 0x202?1:m is 0x204 or 0x205?2:m is 0x207 or 0x208?4:m is 0x20b or 0x20c?(((data.Data>>16)&0xffff)==1?8:16):0;
            if(mask!=0)
            {
                bool up=m is 0x202 or 0x205 or 0x208 or 0x20c;
                buttons=up?buttons&~mask:buttons|mask;
                return Enqueue([(ushort)(0x2800|buttons)])?1:CallNextHookEx(mouseHook,code,message,pointer);
            }
            if(m==0x20a)
            {
                short amount=(short)(data.Data>>16);
                wheelRemainder+=amount/120d*WheelSpeed;int steps=Math.Clamp((int)wheelRemainder,-24,24);wheelRemainder-=steps;
                for(int i=0;i<Math.Abs(steps);i++)if(!Enqueue([(ushort)(steps>=0?0x2901:0x2900)]))return CallNextHookEx(mouseHook,code,message,pointer);
                return 1;
            }
        }
        catch(Exception){Emergency("마우스 입력 처리 오류로 PC로 복귀했습니다.",false);}
        return CallNextHookEx(mouseHook,code,message,pointer);
    }
    private nint Keyboard(int code,nuint message,nint pointer)
    {
        if(code<0)return CallNextHookEx(keyHook,code,message,pointer);
        try
        {
            var key=Marshal.PtrToStructure<KeyData>(pointer);
            if((key.Flags&0x10)!=0)return CallNextHookEx(keyHook,code,message,pointer);
            bool up=(key.Flags&0x80)!=0;
            bool repeated=!up&&physicalKeys.Contains(key.Virtual);
            if(up)physicalKeys.Remove(key.Virtual);else physicalKeys.Add(key.Virtual);
            if(up&&consumed.Remove(key.Virtual))return 1;
            bool ctrl=physicalKeys.Contains(0xa2)||physicalKeys.Contains(0xa3)||physicalKeys.Contains(0x11);
            bool alt=physicalKeys.Contains(0xa4)||physicalKeys.Contains(0xa5)||physicalKeys.Contains(0x12);
            bool shift=physicalKeys.Contains(0xa0)||physicalKeys.Contains(0xa1)||physicalKeys.Contains(0x10);
            bool win=physicalKeys.Contains(0x5b)||physicalKeys.Contains(0x5c);
            int modifiers=(ctrl?1:0)|(alt?2:0)|(shift?4:0)|(win?8:0);
            if(!up&&!EditingShortcut)
            {
                var shortcut=ShortcutRouter.Resolve((int)key.Virtual,modifiers,active,ReturnShortcut,LastDeviceShortcut,DeviceShortcuts);
                if(shortcut.Action!=ShortcutAction.None)
                {
                    consumed.Add(key.Virtual);if(repeated)return 1;
                    if(shortcut.Action==ShortcutAction.ReturnPc){try{ReturnRequested?.Invoke();}catch{ }Emergency("PC 입력으로 복귀했습니다.",false);}
                    else if(shortcut.Action==ShortcutAction.LastDevice)LastDeviceRequested?.Invoke();
                    else if(shortcut.DeviceId!=null)DeviceRequested?.Invoke(shortcut.DeviceId);
                    return 1;
                }
            }
            if(!active)return CallNextHookEx(keyHook,code,message,pointer);
            ushort? command=ScanCodes.Command((int)key.Scan,(key.Flags&1)!=0,up);
            if(key.Virtual==0x15&&KoreanCtrlSpace)
            {
                if(up)return 1;
                bool ctrlHeld=ctrl;
                return Enqueue(ctrlHeld?[0x242c,0x252c]:[0x2601,0x242c,0x252c,0x2701])?1:CallNextHookEx(keyHook,code,message,pointer);
            }
            if(key.Virtual==0x15)command=(ushort)((up?0x2500:0x2400)|0x90);
            if(key.Virtual==0x19)command=(ushort)((up?0x2500:0x2400)|0x91);
            if(command==null)return CallNextHookEx(keyHook,code,message,pointer);
            return Enqueue([command.Value])?1:CallNextHookEx(keyHook,code,message,pointer);
        }
        catch(Exception){Emergency("키 입력 처리 오류로 PC로 복귀했습니다.",false);return CallNextHookEx(keyHook,code,message,pointer);}
    }
    public void Emergency(string reason,bool removeHooks)
    {
        bool wasActive=active;active=false;Interlocked.Increment(ref epoch);Interlocked.Exchange(ref pendingX,0);Interlocked.Exchange(ref pendingY,0);Interlocked.Exchange(ref motionStarted,0);TargetId=null;buttons=0;
        if(wasActive){SetPhysicalCursorPos(restore.X,restore.Y);_=ReleaseRemote();}
        if(removeHooks)RemoveHooks();
        if(wasActive)try{Released?.Invoke(reason);}catch{ }
    }
    public async Task Stop()
    {
        Emergency("PC 입력으로 복귀했습니다.",false);
        await ReleaseRemote();
    }
    public async Task AttachClient(DeviceClient next)
    {
        if(active)throw new InvalidOperationException("먼저 PC로 복귀해야 합니다.");
        client=next;await ReleaseRemote();
        if(!releaseConfirmed)throw new InvalidOperationException("키·버튼 해제 응답을 확인하지 못했습니다. 새로고침 후 다시 전환하세요.");
    }
    private async Task ReleaseRemote()
    {
        await releaseGate.WaitAsync();
        try
        {
        releaseConfirmed=false;
        ushort[] releases;
        releases=held.SnapshotReleases();
        if(client==null)return;
        try
        {
            foreach(var batch in releases.Chunk(12))await client.Command(batch).WaitAsync(TimeSpan.FromSeconds(3));
            await client.EndInput().WaitAsync(TimeSpan.FromSeconds(3));
            held.ConfirmReleased(releases);releaseConfirmed=true;
        }catch { }
        }finally{releaseGate.Release();}
    }
    private void RemoveHooks(){nint m=Interlocked.Exchange(ref mouseHook,0),k=Interlocked.Exchange(ref keyHook,0);if(m!=0)UnhookWindowsHookEx(m);if(k!=0)UnhookWindowsHookEx(k);}
    public void Dispose(){Emergency("앱 종료",true);watchdog.Dispose();commands.Writer.TryComplete();lifetime.Cancel();}
}
