using System.Buffers.Binary;
namespace EasyBlue.Core;

public static class DeviceProtocol
{
    public static ushort Word(ReadOnlySpan<byte> data,int index)
    {if(index<0||data.Length<index*2+2)throw new InvalidDataException("장치 응답이 너무 짧습니다.");return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(index*2,2));}
    public static byte[] Frame(ushort magic,ushort command,ReadOnlySpan<ushort> words)
    {
        if(words.Length>12)throw new ArgumentOutOfRangeException(nameof(words));
        byte[] frame=new byte[words.Length<=4?17:33];frame[0]=(byte)(frame.Length==17?1:2);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1),magic);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3),command);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7),(ushort)words.Length);
        for(int i=0;i<words.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(9+2*i),words[i]);
        return frame;
    }
    public static byte[] Payload(ReadOnlySpan<byte> report)
    {
        if(report.Length<1||report[0] is <1 or >6)throw new InvalidDataException("알 수 없는 HID 보고서입니다.");
        int size=16<<(report[0]-1);
        if(report.Length<size+1)throw new InvalidDataException("잘린 HID 응답입니다.");
        return report.Slice(1,size).ToArray();
    }
    public static byte[] StatusBody(byte[] payload)
    {
        if(Word(payload,0)!=0xeb01||Word(payload,1)!=0xffff)throw new InvalidDataException("상태 응답 헤더가 다릅니다.");
        int size=Word(payload,3);
        if(size<10||size>payload.Length-8)throw new InvalidDataException("장치 상태 크기가 잘못되었습니다.");
        var body=payload.AsSpan(8,size).ToArray();
        if(Word(body,4)>4)throw new InvalidDataException("페어링 개수가 지원 범위를 벗어났습니다.");
        return body;
    }
    public static bool IsConnected(ushort flags)=>(flags&0x83)==3;
    public static string InterfaceVersion(byte[] payload)
    {
        if(payload.Length<16||Word(payload,0)!=0xeb00||Word(payload,1)!=0x8000||Word(payload,3)!=8)
            throw new InvalidDataException("인터페이스 버전 응답이 올바르지 않습니다.");
        string version=System.Text.Encoding.ASCII.GetString(payload,8,8);
        if(version is not ("EB V 2.0" or "EB V 2.1"))throw new NotSupportedException("지원하지 않는 EasyBlue 인터페이스 버전입니다.");
        return version;
    }
    public static byte[] AddressBody(byte[] payload)
    {
        if(payload.Length<24||Word(payload,0)!=0xeb01||Word(payload,1)!=0x8000||Word(payload,2)==0)
            throw new InvalidDataException("기기 주소 응답이 올바르지 않습니다.");
        return payload.AsSpan(18,6).ToArray();
    }
    public static string AddressId(byte[] address)=>Convert.ToHexString(address);
    public static ushort[] SelectAddress(byte[] address)
    {
        if(address.Length!=6)throw new ArgumentOutOfRangeException(nameof(address));
        return Enumerable.Range(0,6).Select(i=>(ushort)(0x4000+(i<<8)+address[i])).Append((ushort)0xd001).ToArray();
    }
    public static IEnumerable<ushort[]> Motion(int x,int y)
    {
        while(x!=0||y!=0){int dx=Math.Clamp(x,-2047,2047),dy=Math.Clamp(y,-2047,2047);yield return [(ushort)(dx&4095),(ushort)(0x1000|(dy&4095))];x-=dx;y-=dy;}
    }
}
public interface IReportExchange {Task<byte[]> ExchangeAsync(byte[] frame,CancellationToken cancellationToken);}
public sealed record PairedDevice(int Slot,byte[] Address,bool Connected)
{
    public string Id=>DeviceProtocol.AddressId(Address);
    public string Label=>$"기기 {Slot+1} · {Id[^4..]}";
}
public sealed class DeviceCommandRejectedException(string message) : IOException(message);
public sealed partial class DeviceClient(IReportExchange transport)
{
    private readonly SemaphoreSlim sessionGate=new(1,1);
    public bool LedEnabled {get;set;}=true;
    private bool initialized;
    private bool identified;
    private ushort stateFlags;
    public Action<string>? Diagnostic {get;set;}
    public async Task Initialize(CancellationToken ct=default,bool reset=false)
    {
        await sessionGate.WaitAsync(ct);
        try
        {
            if(initialized&&!reset)return;
            initialized=false;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));var token=deadline.Token;
            try
            {
                if(!identified)
                {
                    string version=DeviceProtocol.InterfaceVersion(await Query(0xeb00,0x8000,[],token));
                    identified=true;Diagnostic?.Invoke("인터페이스 확인: "+version);
                }
                Diagnostic?.Invoke("제어 세션 초기화 · 7FFF/0 → 7FFF/1");
                // Original 41DC30 validates the envelope only. Unlike command0,
                // this control handshake does not use word2 as a success boolean.
                await Query(0xeb01,0x7fff,[0],token);
                await Task.Delay(10,token);
                await Query(0xeb01,0x7fff,[1],token);
                ushort flags=0;bool ready=false;
                long until=Environment.TickCount64+3000;
                for(int i=0;i<61;i++)
                {
                    await Task.Delay(i==0?10:50,token);
                    var state=DeviceProtocol.StatusBody(await State(token));flags=DeviceProtocol.Word(state,0);
                    if((flags&0xc000)==0x8000){ready=true;break;}
                    if(Environment.TickCount64>=until)break;
                }
                stateFlags=flags;Diagnostic?.Invoke($"제어 세션 상태 flags={flags:X4}");
                // Original 4106D0 also proceeds for 8001000D/E (status0800/0400).
                if(!ready&&(flags&0x0c00)==0)throw new IOException($"제어 세션이 준비되지 않았습니다 (flags={flags:X4}). USB를 다시 연결한 뒤 새로고침해 주세요.");
                await ConfigureInput(token);
                initialized=true;Diagnostic?.Invoke("제어 세션과 기본 입력 설정 초기화 완료");
            }
            catch(OperationCanceledException ex)when(!ct.IsCancellationRequested)
            {throw new IOException("제어 세션 초기화가 8초 안에 완료되지 않았습니다. 새로고침해 주세요.",ex);}
            catch(Exception ex)when(ex is not OperationCanceledException)
            {throw new IOException("제어 세션 초기화 실패: "+ex.Message,ex);}
        }
        finally{sessionGate.Release();}
    }
    public async Task CloseSession(CancellationToken ct=default)
    {
        await sessionGate.WaitAsync(ct);
        try{if(!initialized)return;initialized=false;await Query(0xeb01,0x7fff,[0],ct);}
        finally{sessionGate.Release();}
    }
    private async Task ConfigureInput(CancellationToken ct)
    {
        foreach(ushort[] words in new ushort[][]{[0x30ff,0x3400],[0x3100,0x35ff],[0x3202,0x3602],[0x330c,0x3701]})
        {
            try{await Command(words,ct);}
            catch(DeviceCommandRejectedException ex){throw new DeviceCommandRejectedException($"기본 입력 설정 {words[0]:X4}: "+ex.Message);}
        }
    }
    public async Task<byte[]> Query(ushort magic,ushort command,ushort[] words,CancellationToken ct=default)
    {
        byte[] p=await transport.ExchangeAsync(DeviceProtocol.Frame(magic,command,words),ct);
        if(DeviceProtocol.Word(p,0)!=magic||DeviceProtocol.Word(p,1)!=command)throw new InvalidDataException("요청과 다른 장치 응답입니다.");
        return p;
    }
    public async Task Command(ushort[] words,CancellationToken ct=default,ushort command=0)
    {
        var p=await Query(0xeb01,command,words,ct);
        if(DeviceProtocol.Word(p,2)==0){if(DeviceProtocol.Word(p,3)==0xffff)throw new DeviceCommandRejectedException("장치가 명령을 거부했습니다.");throw new IOException("장치 응답이 완료되지 않았습니다. 입력을 PC로 복구합니다.");}
    }
    public Task<byte[]> State(CancellationToken ct=default)=>Query(0xeb01,0xffff,[],ct);
    public async Task<IReadOnlyList<PairedDevice>> Devices(CancellationToken ct=default)
    {
        byte[] state=DeviceProtocol.StatusBody(await State(ct));
        int count=DeviceProtocol.Word(state,4);
        byte[]? current=null;
        if(DeviceProtocol.IsConnected(DeviceProtocol.Word(state,0)))current=DeviceProtocol.AddressBody(await Query(0xeb01,0x8000,[0xe000],ct));
        var devices=new List<PairedDevice>();
        for(int i=0;i<count;i++)
        {
            var address=DeviceProtocol.AddressBody(await Query(0xeb01,0x8000,[(ushort)(0xe008|i)],ct));
            devices.Add(new(i,address,current!=null&&address.SequenceEqual(current)));
        }
        return devices;
    }
    public async Task StartPairing(CancellationToken ct=default)
    {
        await Initialize(ct);
        // Original worker 443590 ignores the LED-mode result from 442D60,
        // then invokes the pairing pulse independently. LED rejection is not pairing rejection.
        try{await PairStep(LedEnabled?"표시 모드 3889":"표시 끄기 3800",LedEnabled?(ushort)0x3889:(ushort)0x3800,ct);}
        catch(DeviceCommandRejectedException){Diagnostic?.Invoke("표시 모드가 거부됨 · 원본과 같이 페어링 펄스를 한 번 진행합니다.");}
        await PairStep("페어링 준비 23C0",0x23c0,ct);
        await Task.Delay(200,ct);
        await PairStep("페어링 시작 23C1",0x23c1,ct);
    }
    private async Task PairStep(string label,ushort word,CancellationToken ct)
    {
        Diagnostic?.Invoke(label+" · 1회 요청");
        try{await Command([word],ct);Diagnostic?.Invoke(label+" · 완료");}
        catch(DeviceCommandRejectedException ex){throw new DeviceCommandRejectedException(label+": "+ex.Message);}
        catch(Exception ex)when(ex is not OperationCanceledException){throw new IOException(label+": "+ex.Message,ex);}
    }
    public async Task SendPairingPin(string pin,CancellationToken ct=default)
    {
        if(pin.Length is <4 or >16||pin.Any(c=>c<'0'||c>'9'))throw new ArgumentException("휴대폰에 표시된 숫자 PIN을 입력하세요.");
        foreach(char c in pin+"\n")
        {
            await Command([(ushort)(0x2400|c)],ct);await Task.Delay(10,ct);
            await Command([(ushort)(0x2500|c)],ct);await Task.Delay(10,ct);
        }
    }
    public async Task StopPairing(CancellationToken ct=default)
    {
        // Original canceled PIN dialog 443600 ->4106D0 resets the control session;
        // changing indicator word3888 alone does not reproduce cancellation.
        Diagnostic?.Invoke("페어링 대기 종료 · 제어 세션 1회 재초기화");
        await Initialize(ct,true);
        ushort mode=(stateFlags&3) switch{1=>0x38a8,3=>0x388b,_=>0x3888};
        await Command([LedEnabled?mode:(ushort)0x3800],ct);
    }
    public async Task Connect(byte[] address,bool disconnectExisting=false,CancellationToken ct=default)
    {
        await Initialize(ct);
        if(disconnectExisting)await Disconnect(ct);
        await Command(DeviceProtocol.SelectAddress(address),ct,0x8000);
        await Command([0xc001],ct,0x8000);
    }
    public async Task DeletePairing(byte[] address,CancellationToken ct=default)
    {
        await Initialize(ct);
        var words=DeviceProtocol.SelectAddress(address);words[^1]=0xd002;
        await Command(words,ct,0x8000);
    }
    public async Task BeginInput(CancellationToken ct=default)
    {
        await Initialize(ct);await ConfigureInput(ct);
        await Command([LedEnabled?(ushort)0x388b:(ushort)0x3800],ct);
    }
    public async Task UpdateLed(bool enabled,CancellationToken ct=default)
    {
        LedEnabled=enabled;var state=DeviceProtocol.StatusBody(await State(ct));
        ushort mode=(DeviceProtocol.Word(state,0)&3) switch{1=>0x38a8,3=>0x388b,_=>0x3888};
        await Command([enabled?mode:(ushort)0x3800],ct);
    }
    public Task EndInput(CancellationToken ct=default)=>Command([LedEnabled?(ushort)0x3888:(ushort)0x3800],ct);
    public Task Disconnect(CancellationToken ct=default)=>Command([0xc000],ct,0x8000);
}
