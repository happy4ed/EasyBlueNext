using System.Buffers.Binary;
using System.Text;
namespace EasyBlue.Core;
public sealed record DongleNameField(int Address,int Capacity);
public sealed record DongleNameBackup(DateTime CreatedAt,int Address,int Capacity,Dictionary<int,byte[]> Pages);
public static class DongleNames
{
    public static int FlashAddress(byte[] header)
    {
        if(header.Length!=512)throw new InvalidDataException("동글 flash 헤더 크기가 다릅니다.");
        Require(header,"ZMT SERIAL FLASH");
        uint raw=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x1f0));
        return Bound((raw<<16)|(raw>>16));
    }
    public static int BluetoothAddress(byte[] users,int baseAddress)
    {
        Require(users,"ZARAM-USERSAVER");
        string version=Encoding.ASCII.GetString(users,0x18,8);
        if(version is not ("001.1000" or "001.0000"))throw new NotSupportedException("지원하지 않는 동글 설정 구조입니다.");
        uint raw=BinaryPrimitives.ReadUInt32LittleEndian(users.AsSpan(0x30));
        return Bound(checked((uint)baseAddress+((raw<<16)|(raw>>16))));
    }
    public static DongleNameField NameField(byte[] bluetooth)
    {
        Require(bluetooth,"ZARAM BLUETOOTHS");
        if(Encoding.ASCII.GetString(bluetooth,0x18,8)!="00010000")throw new NotSupportedException("지원하지 않는 Bluetooth 설정 구조입니다.");
        int address=BinaryPrimitives.ReadUInt16LittleEndian(bluetooth.AsSpan(0x42));
        int capacity=BinaryPrimitives.ReadUInt16LittleEndian(bluetooth.AsSpan(0x46));
        if(capacity is <2 or >32||address+capacity>65536)throw new InvalidDataException("동글 이름 저장 범위가 올바르지 않습니다.");
        return new(address,capacity);
    }
    public static byte[] Encode(string name,int capacity)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>Math.Min(31,capacity)||name.Any(c=>c<' '||c>'~'))
            throw new ArgumentException($"이름은 영문·숫자·기호로 1~{Math.Min(31,capacity)}자 입력하세요.");
        return Encoding.ASCII.GetBytes(name);
    }
    private static int Bound(uint address)=>address<=33554432-512?(int)address:throw new InvalidDataException("동글 설정 주소가 범위를 벗어났습니다.");
    private static void Require(byte[] bytes,string signature)
    {if(bytes.Length<128||!bytes.AsSpan(0,signature.Length).SequenceEqual(Encoding.ASCII.GetBytes(signature)))throw new InvalidDataException("동글 설정 서명이 올바르지 않습니다. 저장하지 않았습니다.");}
    public static byte[] Request(ushort magic,ushort command,int offset=0,ushort[]? words=null)
    {
        if(offset is <0 or >65535)throw new ArgumentOutOfRangeException(nameof(offset));
        var frame=DeviceProtocol.Frame(magic,command,words??[]);BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5),(ushort)offset);return frame;
    }
}
public sealed partial class DeviceClient
{
    private async Task<byte[]> FlashPage(int page,CancellationToken ct)
    {
        if(page is <0 or >65535)throw new ArgumentOutOfRangeException(nameof(page));
        var p=await transport.ExchangeAsync(DeviceProtocol.Frame(0xeb02,0x8000,[(ushort)(page>16?1:0),(ushort)page]),ct);
        if(p.Length!=512)throw new InvalidDataException("동글 설정 읽기 크기가 다릅니다.");return p;
    }
    private async Task<byte[]> FlashRange(int address,int count,CancellationToken ct)
    {
        var first=await FlashPage(address/512,ct);int offset=address%512;
        if(offset+count<=512)return first.AsSpan(offset,count).ToArray();
        var second=await FlashPage(address/512+1,ct);return first.Concat(second).Skip(offset).Take(count).ToArray();
    }
    private async Task<DongleNameField> FindName(CancellationToken ct)
    {
        int baseAddress=DongleNames.FlashAddress(await FlashPage(0,ct));
        if(baseAddress%512!=0)throw new NotSupportedException("지원하지 않는 동글 설정 정렬입니다.");
        var users=await FlashPage(baseAddress/512,ct);
        int bluetooth=DongleNames.BluetoothAddress(users,baseAddress);
        return DongleNames.NameField(await FlashRange(bluetooth,128,ct));
    }
    private async Task EepromMode(bool enabled,CancellationToken ct)
    {
        var reply=await transport.ExchangeAsync(DongleNames.Request(0xeb04,0x7000,enabled?1:0),ct);
        CheckMemoryReply(reply,0x7000);
    }
    private static void CheckMemoryReply(byte[] reply,ushort command)
    {if(DeviceProtocol.Word(reply,0)!=0xeb04||DeviceProtocol.Word(reply,1)!=command)throw new InvalidDataException("동글 저장 응답이 다릅니다.");}
    private async Task<byte[]> EepromPage(int address,CancellationToken ct)
    {
        var page=await transport.ExchangeAsync(DongleNames.Request(0xeb04,0x8000,address&0xffc0),ct);
        if(page.Length!=64)throw new InvalidDataException("동글 저장 페이지 크기가 다릅니다.");return page;
    }
    public async Task<string> DongleName(CancellationToken ct=default)=>await AccessDongleName(null,null,ct);
    public async Task<string> RenameDongle(string name,Func<DongleNameBackup,Task> backup,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(backup);DongleNames.Encode(name,32);
        return await AccessDongleName(name,backup,ct);
    }
    private async Task<string> AccessDongleName(string? replacement,Func<DongleNameBackup,Task>? backup,CancellationToken ct)
    {
        await Initialize(ct);await CloseSession(ct);
        try
        {
            await Task.Delay(50,ct);await EepromMode(true,ct);await Task.Delay(50,ct);
            var field=await FindName(ct);var originals=new Dictionary<int,byte[]>();
            for(int page=field.Address&~63;page<field.Address+field.Capacity;page+=64)originals[page]=await EepromPage(page,ct);
            var bytes=originals.OrderBy(x=>x.Key).SelectMany(x=>x.Value).Skip(field.Address%64).Take(field.Capacity).ToArray();
            if(replacement==null)return System.Text.Encoding.ASCII.GetString(bytes.TakeWhile(b=>b!=0).ToArray());
            byte[] encoded=DongleNames.Encode(replacement,field.Capacity);
            await backup!(new(DateTime.Now,field.Address,field.Capacity,originals.ToDictionary(x=>x.Key,x=>x.Value.ToArray())));
            var changed=originals.ToDictionary(x=>x.Key,x=>x.Value.ToArray());
            for(int i=0;i<field.Capacity;i++){int a=field.Address+i;changed[a&~63][a%64]=i<encoded.Length?encoded[i]:(byte)0;}
            foreach(var pair in changed)
            {
                if(pair.Value.SequenceEqual(originals[pair.Key]))continue;
                var ack=await transport.ExchangeAsync(DongleNames.Request(0xeb04,0,pair.Key),ct);CheckMemoryReply(ack,0);
                byte[] raw=new byte[65];raw[0]=3;pair.Value.CopyTo(raw,1);
                CheckMemoryReply(await transport.ExchangeAsync(raw,ct),0);
                if(!(await EepromPage(pair.Key,ct)).SequenceEqual(pair.Value))throw new IOException("이름 저장 후 검증에 실패했습니다. 백업은 보관되어 있습니다.");
            }
            return replacement;
        }
        finally
        {
            using var restore=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await EepromMode(false,restore.Token);await Task.Delay(50,restore.Token);await Initialize(restore.Token,true);
        }
    }
}
