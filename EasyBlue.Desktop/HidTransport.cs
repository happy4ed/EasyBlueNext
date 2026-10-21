using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using EasyBlue.Core;
namespace EasyBlue.Desktop;

internal sealed class HidTransport : IReportExchange, IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly FileStream stream;
    private readonly int inputLength;
    private readonly SemaphoreSlim gate=new(1,1);
    private int failed;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("hid.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] data,int size);
    [DllImport("hid.dll")]
    [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out nint data);
    [DllImport("hid.dll")]
    [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint preparsed,nint caps);
    [DllImport("hid.dll")]
    [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FlushQueue(SafeFileHandle handle);
    public Action<string>? Diagnostic {get;set;}
    private void Trace(string value){try{Diagnostic?.Invoke(value);}catch{ }}
    public string Path {get;}
    public HidTransport(string path)
    {
        if(!path.Contains("vid_1d54&pid_b002",StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("현재 독립 통신은 B002 장치를 지원합니다. B001은 기존 앱을 사용해 주세요.");
        Path=path;handle=CreateFileW(path,0xc0000000,3,0,3,0x40000000,0);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if(!HidD_GetPreparsedData(handle,out nint data))throw new IOException("HID 정보를 읽지 못했습니다.");
            try
            {
                nint caps=Marshal.AllocHGlobal(64);
                try
                {
                    if(HidP_GetCaps(data,caps)!=0x110000)throw new IOException("HID 보고서 규격을 읽지 못했습니다.");
                    inputLength=(ushort)Marshal.ReadInt16(caps,4);
                    int usagePage=(ushort)Marshal.ReadInt16(caps,2);
                    if(usagePage<0xff00||inputLength is <17 or >513)throw new IOException("EasyBlue 제어 인터페이스가 아닙니다.");
                }
                finally {Marshal.FreeHGlobal(caps);}
            }
            finally {HidD_FreePreparsedData(data);}
            HidD_FlushQueue(handle);
            stream=new FileStream(handle,FileAccess.ReadWrite,1,true);
        }
        catch {handle.Dispose();throw;}
    }
    public async Task<byte[]> ExchangeAsync(byte[] frame,CancellationToken ct)
    {
        if(Volatile.Read(ref failed)!=0)throw new IOException("통신이 중단되었습니다. 장치 새로고침을 눌러 다시 연결하세요.");
        // The inner operation retains the semaphore and the SafeHandle while a native
        // feature write is pending. A UI timeout never reuses an in-flight buffer.
        var operation=ExchangeCore(frame);
        try{return await operation.WaitAsync(TimeSpan.FromSeconds(3),ct);}
        catch {Interlocked.Exchange(ref failed,1);_=operation.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw;}
    }
    private async Task<byte[]> ExchangeCore(byte[] frame)
    {
        await gate.WaitAsync();
        try
        {
            if(Volatile.Read(ref failed)!=0)throw new IOException("통신이 중단되었습니다.");
            bool management=frame.Length<=33&&DeviceProtocol.Word(frame.AsSpan(1),1)!=0;
            if(management)Trace($"{(DeviceProtocol.Word(frame.AsSpan(1),1)==0xffff?"상태 조회":"관리 명령")} TX report={frame[0]} bytes={frame.Length} header={Convert.ToHexString(frame.AsSpan(1,8))}");
            await Task.Run(()=>{if(!HidD_SetFeature(handle,frame,frame.Length))throw new Win32Exception(Marshal.GetLastWin32Error());});
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            byte[] response=new byte[inputLength];
            int length=await stream.ReadAsync(response,timeout.Token);
            bool rawConfiguration=management&&DeviceProtocol.Word(frame.AsSpan(1),1)==0x8000&&DeviceProtocol.Word(frame.AsSpan(1),0) is 0xeb02 or 0xeb04;
            if(management)Trace($"{(DeviceProtocol.Word(frame.AsSpan(1),1)==0xffff?"상태 조회":"관리 명령")} RX report={(length>0?response[0]:0)} bytes={length} header={(rawConfiguration?"configuration payload omitted":length>=9?Convert.ToHexString(response.AsSpan(1,8)):"short")}");
            return DeviceProtocol.Payload(response.AsSpan(0,length));
        }
        finally {gate.Release();}
    }
    public void Dispose(){Interlocked.Exchange(ref failed,1);stream.Dispose();handle.Dispose();}
}
