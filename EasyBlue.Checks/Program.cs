using EasyBlue.Core;
using System.Buffers.Binary;

static void Require(bool condition,string description)
{
    if(!condition)throw new Exception(description);
    Console.WriteLine("PASS "+description);
}
var startupXml=System.Xml.Linq.XDocument.Parse(StartupTaskDefinition.Create(Path.Combine(Path.GetTempPath(),"EasyBlue & test","EasyBlue.Desktop.exe"),"S-1-5-21-123-456-789-1001"));
System.Xml.Linq.XNamespace startupNs="http://schemas.microsoft.com/windows/2004/02/mit/task";
string? StartupValue(string name)=>startupXml.Descendants(startupNs+name).FirstOrDefault()?.Value;
Require(StartupValue("Command")==Path.Combine(Path.GetTempPath(),"EasyBlue & test","EasyBlue.Desktop.exe"),"startup paths with spaces and XML characters round trip");
Require(StartupValue("WorkingDirectory")==Path.Combine(Path.GetTempPath(),"EasyBlue & test"),"startup working directory is explicit");
Require(StartupValue("Arguments")=="--background","startup launches in tray mode");
Require(startupXml.Descendants(startupNs+"UserId").All(e=>e.Value=="S-1-5-21-123-456-789-1001")&&startupXml.Descendants(startupNs+"UserId").Count()==2,"startup trigger and principal are scoped to the same user");
Require(StartupValue("LogonType")=="InteractiveToken"&&StartupValue("RunLevel")=="LeastPrivilege","startup uses interactive user without password or elevation");
Require(StartupValue("Delay")=="PT10S","startup allows desktop initialization before launch");
Require(startupXml.Descendants(startupNs+"LogonTrigger").Single().Elements().Select(e=>e.Name.LocalName).SequenceEqual(new[]{"Enabled","UserId","Delay"}),"startup logon trigger follows Windows schema order");
Require(StartupValue("DisallowStartIfOnBatteries")=="false"&&StartupValue("StopIfGoingOnBatteries")=="false","startup continues on battery power");
Require(StartupValue("RunOnlyIfIdle")=="false"&&StartupValue("RunOnlyIfNetworkAvailable")=="false","startup does not depend on idle time or network");
Require(StartupValue("ExecutionTimeLimit")=="PT0S"&&StartupValue("AllowHardTerminate")=="false","startup does not kill the tray app after 72 hours");
Require(StartupValue("MultipleInstancesPolicy")=="IgnoreNew","startup ignores duplicate task launches");
Require(StartupValue("Interval")=="PT1M"&&StartupValue("Count")=="3","startup crash retries are bounded");
if(args.Length==2&&args[0]=="--startup-xml")File.WriteAllText(args[1],startupXml.ToString());

Require(Enumerable.Range(32,95).All(c=>AdvancedInput.Ascii((char)c)!=null),"clipboard covers every printable US ASCII character");
Require(AdvancedInput.Ascii('@')==(31,2)&&AdvancedInput.Ascii('0')==(39,0),"clipboard shifted number symbols preserve HID positions");
Require(AdvancedInput.ClipboardPlan("a\r\nb\rc",new(Layout:"US")).Count==5,"clipboard normalizes CRLF and CR to one newline");
var hangulPlan=AdvancedInput.ClipboardPlan("한",new());
Require(hangulPlan.Count==5&&hangulPlan[1].Words.Contains((ushort)0x240a)&&hangulPlan[2].Words.Contains((ushort)0x240e)&&hangulPlan[3].Words.Contains((ushort)0x2416),"clipboard Hangul syllables decompose into two-set keystrokes");
Require(hangulPlan.First().Words.SequenceEqual(AdvancedInput.Stroke(44,1))&&hangulPlan.Last().Words.SequenceEqual(AdvancedInput.Stroke(44,1)),"clipboard restores the declared initial language state");
Require(AdvancedInput.ClipboardPlan("ㄱㅏ",new()).Count==4,"clipboard supports Korean compatibility jamo");
Require(AdvancedInput.ClipboardPlan("한A",new(LanguageSwitch:"Hangul")).Count==6,"clipboard switches language between Korean and Latin text");
foreach(var options in new[]{new ClipboardOptions(),new ClipboardOptions(Layout:"US")})
{bool rejected=false;try{AdvancedInput.ClipboardPlan("hello😀",options);}catch(ArgumentException){rejected=true;}Require(rejected,"clipboard rejects unsupported Unicode before any transmission");}
{bool rejected=false;try{AdvancedInput.ClipboardPlan(new string('a',20001),new());}catch(ArgumentException){rejected=true;}Require(rejected,"clipboard bounds paste length");}
Require(AdvancedInput.SpecialCommand(17,false)==0x2c11&&AdvancedInput.SpecialCommand(17,true)==0x2d11,"special home key uses original press and release protocol");
{var release=new KeyReleaseLedger();release.ObserveDowns([0x2c11]);Require(release.SnapshotReleases().Contains((ushort)0x2d11),"special keys are released after emergency cancellation");release.ConfirmReleased([0x2d11]);Require(!release.SnapshotReleases().Contains((ushort)0x2d11),"special key release acknowledgements clear the ledger");}
var advancedPreferences=(new UserPreferences{SpecialKeyGroups=[new("custom",[17,999])],ActiveSpecialGroup=99,Clipboard=new(DelayMs:0)}).Validate();
Require(advancedPreferences.SpecialKeyGroups.Count==4&&advancedPreferences.SpecialKeyGroups.All(g=>g.Keys.Length==12)&&advancedPreferences.SpecialKeyGroups[0].Keys[1]==null,"special group validation fills four groups and rejects unknown device codes");
Require(advancedPreferences.ActiveSpecialGroup==3&&advancedPreferences.Clipboard.DelayMs==10,"advanced input settings bound group selection and pacing");
Require(StartupValue("Priority")=="5","scheduled input app retains normal interactive priority");

foreach(int count in new[]{1,4,5,12})
{
    ushort[] words=Enumerable.Range(0,count).Select(x=>(ushort)(0x2400+x)).ToArray();
    byte[] frame=FeatureFrame.Encode(words);
    Require(frame.Length==(count<=4?17:33),$"{count} words: report length");
    Require(frame[0]==(count<=4?1:2),$"{count} words: report ID");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0xeb01,"little endian magic");
    Require(frame.AsSpan(3,4).ToArray().All(x=>x==0),"reserved envelope words zero");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7))==count,"word count");
    for(int i=0;i<count;i++)Require(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9+2*i))==words[i],"payload order");
    Require(frame.AsSpan(9+2*count).ToArray().All(x=>x==0),"zero padding");
}
foreach(int count in new[]{0,13})
{
    bool rejected=false;
    try{FeatureFrame.Encode(new ushort[count]);}catch(ArgumentOutOfRangeException){rejected=true;}
    Require(rejected,"invalid frame length rejected");
}
Require(new UserPreferences(double.NaN).Validate().FontSize==13,"invalid font size reset");
Require(new UserPreferences(100).Validate().FontSize==18,"font size capped");
string path=Path.Combine(Path.GetTempPath(),"easyblue-check-"+Guid.NewGuid(),"preferences.json");
try
{
    new UserPreferences(18).Save(path);
    Require(UserPreferences.Load(path).FontSize==18,"settings round trip");
    new UserPreferences(14).Save(path);
    Require(UserPreferences.Load(path).FontSize==14,"settings replace");
}
finally{Directory.Delete(Path.GetDirectoryName(path)!,true);}

var monitors=new[]{new ScreenNode("m1","Main",true,new Box(-20,0,20,12),1920,1080),new ScreenNode("m2","Right",true,new Box(0,0,20,12),2560,1440),new ScreenNode("m3","Above",true,new Box(-20,-12,20,12),1920,1080)};
foreach(var test in new[]{(new Box(20,2,3,6),"m2","Right"),(new Box(-23,2,3,6),"m1","Left"),(new Box(-15,-18,6,6),"m3","Top"),(new Box(-15,12,6,4),"m1","Bottom")})
{
    var a=Geometry.Contact(new("phone","phone",false,test.Item1,1080,2400),monitors);
    Require(a?.MonitorId==test.Item2&&a.Edge==test.Item3,"four-sided attachment and multiple monitors: "+test.Item3);
}
Require(Geometry.Contact(new("p","p",false,new(20,12,2,4),1080,2400),monitors)==null,"corner contact is manual");
Require(Geometry.Contact(new("p","p",false,new(21,2,2,4),1080,2400),monitors)==null,"detached connected device is manual");
var phone=new RemoteLayout("p","p",20.2,2,7,1080,2400);
var snapped=Geometry.Snap(phone,monitors,[]);
Require(snapped.X==20,"near edge magnetically snaps");
Require(Geometry.Snap(phone with{X=22},monitors,[]).X==22,"far device stays detached");
var taken=new RemoteLayout("other","other",20,2,7,1080,2400);
var occupied=Geometry.Snap(phone,monitors,[taken]);
Require(Geometry.Contact(new("p","p",false,occupied.Bounds,1080,2400),monitors)==null,"occupied boundary does not create automatic route");
var monitor27=Geometry.Physical(0,0,27,16,9);var phone7=Geometry.Physical(0,0,7,9,20);var tablet12=Geometry.Physical(0,0,12,16,10);
Require(phone7.Width/monitor27.Width<0.15&&phone7.Height/monitor27.Height<0.5,"phone physical ratio is small");
Require(tablet12.Width/monitor27.Width>0.4&&tablet12.Width/monitor27.Width<0.45,"12 inch tablet physical ratio");
var dwell=new DwellTracker();
Require(dwell.Update("p",1000,0.5)==null&&dwell.Update("p",1499,0.5)==null&&dwell.Update("p",1500,0.5)=="p","dwell threshold");
Require(dwell.Update("p",3000,0.5)==null,"dwell fires once until exit");
dwell.Update(null,3100,0.5);dwell.Update("p",3200,0.5);
Require(dwell.Update("p",3700,0.5)=="p","dwell rearm after exit");
Require(ScanCodes.Command(0x1e,false,false)==0x2404&&ScanCodes.Command(0x1e,false,true)==0x2504,"original A key press and release");
Require(ScanCodes.Command(0x2a,false,false)==0x2602&&ScanCodes.Command(0x2a,false,true)==0x2702,"original left shift mapping");
Require(ScanCodes.Command(0x1d,true,false)==0x2610,"right ctrl mapping");
Require(ScanCodes.Command(0x4d,true,false)==0x244f,"extended right arrow");
var fragments=DeviceProtocol.Motion(5000,-5000).ToArray();
static int Signed12(int v)=>(v&0x800)!=0?(v&0xfff)-4096:v&0xfff;
Require(fragments.Sum(f=>Signed12(f[0]))==5000&&fragments.Sum(f=>Signed12(f[1]))==-5000,"motion fragments preserve displacement");
Require(fragments.All(f=>Math.Abs(Signed12(f[0]))<=2047&&Math.Abs(Signed12(f[1]))<=2047),"no signed12 overflow");
Require(Convert.ToHexString(DeviceProtocol.Frame(0xeb01,0,[0x23c1]))=="0101EB000000000100C123000000000000","pairing command exact original wire bytes");
Require(Convert.ToHexString(DeviceProtocol.Frame(0xeb01,0xffff,[]))=="0101EBFFFF000000000000000000000000","state request wire bytes");
foreach(byte[] invalid in new[]{new byte[0],new byte[]{0},new byte[]{7},new byte[]{1,0}})
{bool rejected=false;try{DeviceProtocol.Payload(invalid);}catch(InvalidDataException){rejected=true;}Require(rejected,"invalid or truncated HID report rejected");}
var exchange=new ScriptedExchange();
var client=new DeviceClient(exchange);
await client.Initialize();
Require(exchange.Requests.Count==8,"session handshake and status precede four input configuration frames");
Require(DeviceProtocol.Word(exchange.Requests[1].AsSpan(1),1)==0x7fff&&DeviceProtocol.Word(exchange.Requests[1].AsSpan(1),4)==0&&DeviceProtocol.Word(exchange.Requests[2].AsSpan(1),1)==0x7fff&&DeviceProtocol.Word(exchange.Requests[2].AsSpan(1),4)==1,"original disable then enable control session order");
Require(DeviceProtocol.Word(exchange.Requests[3].AsSpan(1),1)==0xffff&&DeviceProtocol.Word(exchange.Requests[4].AsSpan(1),4)==0x30ff&&DeviceProtocol.Word(exchange.Requests[7].AsSpan(1),4)==0x330c,"ready status checked before original input configuration");
await client.Initialize();Require(exchange.Requests.Count==8,"successful session initialization is idempotent");
exchange.Requests.Clear();
await client.StartPairing();
Require(exchange.Requests.Count==3,"pairing three-step sequence");
Require(DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),4)==0x3889&&DeviceProtocol.Word(exchange.Requests[1].AsSpan(1),4)==0x23c0&&DeviceProtocol.Word(exchange.Requests[2].AsSpan(1),4)==0x23c1,"original pairing mode and command order");
exchange.Requests.Clear();await client.Connect([1,2,3,4,5,6]);
Require(exchange.Requests.Count==2&&DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),1)==0x8000,"address selection uses management envelope");
Require(DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),4)==0x4001&&DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),10)==0xd001&&DeviceProtocol.Word(exchange.Requests[1].AsSpan(1),4)==0xc001,"connect exact address order and command");
exchange.Reject=true;bool failed=false;try{await client.StartPairing();}catch(IOException){failed=true;}
Require(failed,"pairing failure is not reported as success");
string legacy=Path.Combine(Path.GetTempPath(),"easyblue-old-"+Guid.NewGuid()+".json");
try{File.WriteAllText(legacy,"{\"FontSize\":16}");Require(UserPreferences.Load(legacy).FontSize==13,"old oversized preferences migrate");}finally{File.Delete(legacy);}

var ledger=new KeyReleaseLedger();
ledger.ObserveDowns([0x2404]);ledger.ObserveDowns([0x2504]);
Require(ledger.SnapshotReleases().Contains((ushort)0x2504),"queued key-up cancellation still releases earlier down");
ledger.ObserveDowns([0x2601,0x242c,0x252c,0x2701]);
Require(ledger.SnapshotReleases().Contains((ushort)0x2701)&&ledger.SnapshotReleases().Contains((ushort)0x252c),"synthetic chord tracked for partial-send recovery");
ledger.ConfirmReleased(ledger.SnapshotReleases());
Require(ledger.SnapshotReleases().SequenceEqual(new ushort[]{0x2800}),"release sweep clears only confirmed keys");
exchange.Reject=false;exchange.Requests.Clear();await client.BeginInput();
Require(exchange.Requests.Count==5&&DeviceProtocol.Word(exchange.Requests[4].AsSpan(1),4)==0x388b,"input initialization ends with capture mode from runtime trace");
exchange.Requests.Clear();await client.SendPairingPin("1234");
Require(exchange.Requests.Count==10&&DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),4)==0x2431&&DeviceProtocol.Word(exchange.Requests[^1].AsSpan(1),4)==0x250a,"PIN uses original ASCII press/release with newline");
bool pinRejected=false;try{await client.SendPairingPin("12ab");}catch(ArgumentException){pinRejected=true;}
Require(pinRejected,"non-numeric PIN rejected without USB writes");
var addressReply=new byte[32];BinaryPrimitives.WriteUInt16LittleEndian(addressReply,0xeb01);BinaryPrimitives.WriteUInt16LittleEndian(addressReply.AsSpan(2),0x8000);BinaryPrimitives.WriteUInt16LittleEndian(addressReply.AsSpan(4),1);
new byte[]{1,2,3,4,5,6}.CopyTo(addressReply,18);
Require(DeviceProtocol.AddressBody(addressReply).SequenceEqual(new byte[]{1,2,3,4,5,6}),"address offset confirmed by original assembly at 41E324");
var stateReply=new byte[32];BinaryPrimitives.WriteUInt16LittleEndian(stateReply,0xeb01);BinaryPrimitives.WriteUInt16LittleEndian(stateReply.AsSpan(2),0xffff);BinaryPrimitives.WriteUInt16LittleEndian(stateReply.AsSpan(6),16);BinaryPrimitives.WriteUInt16LittleEndian(stateReply.AsSpan(16),2);
Require(DeviceProtocol.Word(DeviceProtocol.StatusBody(stateReply),4)==2,"status byte count and paired count offset");
BinaryPrimitives.WriteUInt16LittleEndian(stateReply.AsSpan(6),512);
bool malformed=false;try{DeviceProtocol.StatusBody(stateReply);}catch(InvalidDataException){malformed=true;}
Require(malformed,"malformed status length rejected");

var chain=new[]{new ScreenNode("monitor","monitor",true,new(0,0,20,12),1920,1080),new ScreenNode("phone1","phone1",false,new(20,2,3,6),1080,2400),new ScreenNode("phone2","phone2",false,new(23,2,3,6),1080,2400)};
var contacts=Geometry.Contacts(chain[1],chain);
Require(contacts.Count==2&&contacts.Any(x=>x.MonitorId=="monitor"&&x.Edge=="Right")&&contacts.Any(x=>x.MonitorId=="phone2"&&x.Edge=="Left"),"device chain has both return and onward routes");
Require(!contacts.Any(x=>x.MonitorId=="phone1"),"device cannot attach to itself");
Require(new UserPreferences(13,"invalid").Validate().Theme=="System","unknown theme falls back to system");
Require(new UserPreferences(13,"Dark").Validate().Theme=="Dark","explicit dark theme retained");

exchange.Requests.Clear();await client.DeletePairing([1,2,3,4,5,6]);
Require(exchange.Requests.Count==1&&DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),10)==0xd002,"delete command matches UI delete confirmation path, not connect");
exchange.Requests.Clear();await client.Connect([1,2,3,4,5,6],true);
Require(exchange.Requests.Count==3&&DeviceProtocol.Word(exchange.Requests[0].AsSpan(1),4)==0xc000&&DeviceProtocol.Word(exchange.Requests[2].AsSpan(1),4)==0xc001,"switching from another device disconnects before selecting and connecting");
// Regression: optional indicator rejection must not suppress the original pairing pulse.
exchange.Reject=false;exchange.RejectWord=0x3889;exchange.Requests.Clear();
await client.StartPairing();
Require(exchange.Requests.Count==3,"optional LED rejection still sends each pairing pulse once");
exchange.RejectWord=0x23c0;exchange.Requests.Clear();string error="";
try{await client.StartPairing();}catch(DeviceCommandRejectedException ex){error=ex.Message;}
Require(exchange.Requests.Count==2&&error.Contains("23C0"),"pair preparation rejection stops immediately with phase and no retry");
exchange.RejectWord=0x23c1;exchange.Requests.Clear();error="";
try{await client.StartPairing();}catch(DeviceCommandRejectedException ex){error=ex.Message;}
Require(exchange.Requests.Count==3&&error.Contains("23C1"),"pair start rejection is bounded and identifies failing phase");
await Task.Delay(250);
Require(exchange.Requests.Count==3,"failed client pairing never schedules background requests");
Require(ShortcutBinding.IsAllowed(new(0x7b,3)),"default PC return shortcut accepted");
Require(!ShortcutBinding.IsAllowed(new(0x41,0))&&ShortcutBinding.IsAllowed(new(0x31,3)),"ordinary typing rejected while previously reserved combinations are assignable");
var custom=(new UserPreferences{ReturnShortcut=new(0x52,5),LastDeviceShortcut=new(0x4c,3),LastDeviceId="phone"}).Validate();
Require(custom.ReturnShortcut.Matches(0x52,5)&&!custom.ReturnShortcut.Matches(0x52,3),"custom shortcut matches exact modifiers");
var invalidShortcuts=(new UserPreferences{ReturnShortcut=new(1,0),LastDeviceShortcut=new(0x7b,3)}).Validate();
Require(invalidShortcuts.ReturnShortcut==invalidShortcuts.LastDeviceShortcut,"shared return and last-device shortcut survives validation");
string customPath=Path.Combine(Path.GetTempPath(),"easyblue-custom-"+Guid.NewGuid()+".json");
try{custom.Save(customPath);var loaded=UserPreferences.Load(customPath);Require(loaded.ReturnShortcut==custom.ReturnShortcut&&loaded.LastDeviceShortcut==custom.LastDeviceShortcut&&loaded.LastDeviceId=="phone","custom shortcuts and last device persist across restarts");}finally{File.Delete(customPath);}
var coldExchange=new ScriptedExchange();var coldClient=new DeviceClient(coldExchange);
await coldClient.StartPairing();
Require(coldExchange.Requests.Count==11&&DeviceProtocol.Word(coldExchange.Requests[8].AsSpan(1),4)==0x3889&&DeviceProtocol.Word(coldExchange.Requests[9].AsSpan(1),4)==0x23c0,"cold pairing opens control session before pulse; no retained legacy state required");
await coldClient.CloseSession();
Require(!coldExchange.Enabled&&DeviceProtocol.Word(coldExchange.Requests[^1].AsSpan(1),1)==0x7fff,"closing releases control session with original handshake");
coldExchange.Requests.Clear();await coldClient.StartPairing();
Require(coldExchange.Requests.Count==10&&coldExchange.Enabled,"reopened session is initialized again before pairing");
var initFailure=new ScriptedExchange{RejectWord=0x30ff};string initError="";
try{await new DeviceClient(initFailure).StartPairing();}catch(IOException ex){initError=ex.Message;}
Require(initError.Contains("30FF")&&!initFailure.Requests.Any(f=>DeviceProtocol.Word(f.AsSpan(1),4)==0x23c0),"initialization rejection stops before pairing and identifies failed configuration");
coldExchange.Requests.Clear();await coldClient.StopPairing();
Require(coldExchange.Requests.Count==8&&DeviceProtocol.Word(coldExchange.Requests[0].AsSpan(1),1)==0x7fff&&DeviceProtocol.Word(coldExchange.Requests[1].AsSpan(1),4)==1,"pair cancellation resets session rather than only changing indicator");
Require(!coldExchange.Requests.Any(f=>DeviceProtocol.Word(f.AsSpan(1),4) is 0x23c0 or 0x23c1),"cancellation never reissues pairing pulses");
Require(DeviceProtocol.IsConnected(0x8003)&&!DeviceProtocol.IsConnected(0x8083)&&!DeviceProtocol.IsConnected(0x8001),"connected status excludes original transition bit7");
var versionReply=new byte[32];BinaryPrimitives.WriteUInt16LittleEndian(versionReply,0xeb00);BinaryPrimitives.WriteUInt16LittleEndian(versionReply.AsSpan(2),0x8000);BinaryPrimitives.WriteUInt16LittleEndian(versionReply.AsSpan(6),8);System.Text.Encoding.ASCII.GetBytes("EB V 2.1").CopyTo(versionReply,8);
Require(DeviceProtocol.InterfaceVersion(versionReply)=="EB V 2.1","interface version read independently of VID/PID");
versionReply[15]=(byte)'9';bool wrongVersion=false;try{DeviceProtocol.InterfaceVersion(versionReply);}catch(NotSupportedException){wrongVersion=true;}
Require(wrongVersion,"unknown protocol rejected before control session commands");
var notReady=new ScriptedExchange{ReadyFlags=0};bool readinessFailed=false;
try{await new DeviceClient(notReady).StartPairing();}catch(IOException){readinessFailed=true;}
Require(readinessFailed&&notReady.Requests.Count<=64&&!notReady.Requests.Any(f=>DeviceProtocol.Word(f.AsSpan(1),4) is 0x30ff or 0x23c0 or 0x23c1),"readiness polling is bounded and stops before configuration or pairing");
var incompatible=new ScriptedExchange{Version="EB V 2.9"};bool incompatibleRejected=false;
try{await new DeviceClient(incompatible).StartPairing();}catch(IOException){incompatibleRejected=true;}
Require(incompatibleRejected&&incompatible.Requests.Count==1,"unrecognized interface receives only read-only identity request");
var shared=new ShortcutBinding(0x7b,3);
Require(ShortcutRouter.Resolve(0x7b,3,false,shared,shared,new Dictionary<string,ShortcutBinding>()).Action==ShortcutAction.LastDevice,"shared shortcut connects last device from PC");
Require(ShortcutRouter.Resolve(0x7b,3,true,shared,shared,new Dictionary<string,ShortcutBinding>()).Action==ShortcutAction.ReturnPc,"shared shortcut returns PC from remote capture");
var assigned=new Dictionary<string,ShortcutBinding>{{"phoneB",new(0x42,5)}};
Require(ShortcutRouter.Resolve(0x42,5,false,shared,new(0x7a,3),assigned).DeviceId=="phoneB","device shortcut targets stable device ID rather than list index");
Require(ShortcutRouter.Resolve(0x31,3,false,shared,shared,assigned).Action==ShortcutAction.None,"old fixed shortcut is inactive unless explicitly assigned");
var customized=(new UserPreferences{ReturnShortcut=shared,LastDeviceShortcut=shared,DeviceShortcuts=assigned}).Validate();
string assignedPath=Path.Combine(Path.GetTempPath(),"easyblue-assigned-"+Guid.NewGuid()+".json");
try{customized.Save(assignedPath);var loaded=UserPreferences.Load(assignedPath);Require(loaded.ReturnShortcut==loaded.LastDeviceShortcut&&loaded.DeviceShortcuts["phoneB"]==assigned["phoneB"],"shared and per-device shortcuts persist together");}finally{File.Delete(assignedPath);}
var px=new Box(-3840,-2160,3840,2160);var inches=new Box(-24,-13.5,24,13.5);
foreach(var test in new[]{("Right",-2d,-1080d),("Left",-3838d,-1080d),("Top",-1920d,-2158d),("Bottom",-1920d,-2d)})
{
 var edgeRoute=new Attachment("phone","monitor",test.Item1,test.Item1 is "Left" or "Right"?-13.5:-24,0);
 Require(Geometry.PcEdgeAxis(edgeRoute,px,inches,test.Item2,test.Item3).HasValue,"physical high DPI negative-coordinate edge detected: "+test.Item1);
 Require(Geometry.PcEdgeAxis(edgeRoute,px,inches,-1920,-1080)==null,"interior cursor does not trigger edge: "+test.Item1);
}
Require(Geometry.PcEdgeAxis(new("phone","monitor","Right",-13.5,0),px,inches,0,-1080)==null,"neighbor monitor pixel excluded at shared boundary");
var countdown=new DwellTracker();countdown.Update("phone",1000,0.5);
Require(Math.Abs(countdown.RemainingSeconds(1300,0.5)-0.2)<0.00001,"edge countdown shows remaining 0.2 seconds");
countdown.Update(null,1320,0.5);Require(countdown.RemainingSeconds(1320,0.5)==0,"leaving edge cancels countdown");
countdown.Update("phone",1400,0.5);Require(countdown.Update("phone",1900,0.5)=="phone"&&countdown.RemainingSeconds(1900,0.5)==0,"countdown completes exactly once at dwell threshold");
Require(ShortcutBinding.IsAllowed(new(0x77,0)),"standalone F8 can be assigned as a shortcut");
var pollingDwell=new DwellTracker();pollingDwell.Update("phone",1000,0.2);
Require(pollingDwell.Update("phone",1250,0.2,false)==null&&pollingDwell.Update("phone",1260,0.2)=="phone","background status poll defers firing without losing accumulated dwell");
pollingDwell.Reset();pollingDwell.Update("phone",2000,0.2);pollingDwell.Update(null,2100,0.2,false);pollingDwell.Update("phone",2150,0.2,false);
Require(pollingDwell.Update("phone",2200,0.2)==null,"leaving and returning during a poll restarts continuous dwell");
var nameMemory=new MemoryExchange();var nameClient=new DeviceClient(nameMemory);
Require(await nameClient.DongleName()=="EasyBlue Tiny","dongle name read from validated flash metadata and EEPROM");
byte[] beforeMemory=nameMemory.Memory.ToArray();bool backedUp=false;
string renamed=await nameClient.RenameDongle("Desk Blue",backup=>{backedUp=backup.Address==60&&backup.Pages.Count==2&&backup.Pages.All(p=>p.Value.SequenceEqual(beforeMemory.AsSpan(p.Key,64).ToArray()));return Task.CompletedTask;});
Require(renamed=="Desk Blue"&&backedUp&&nameMemory.Writes==2,"cross-page dongle name backed up before two page writes");
Require(nameMemory.Memory.Take(60).SequenceEqual(beforeMemory.Take(60))&&nameMemory.Memory.Skip(92).SequenceEqual(beforeMemory.Skip(92)),"name write preserves every byte outside declared name field");
Require(await nameClient.DongleName()=="Desk Blue"&&nameMemory.Enabled,"stored dongle name reread and normal session restored");
var noBackup=new MemoryExchange();bool backupFailed=false;
try{await new DeviceClient(noBackup).RenameDongle("NoBackup",_=>throw new IOException("backup denied"));}catch(IOException){backupFailed=true;}
Require(backupFailed&&noBackup.Writes==0&&noBackup.Enabled,"failed backup prevents EEPROM writes and restores session");
var badLayout=new MemoryExchange{BadSignature=true};bool layoutFailed=false;
try{await new DeviceClient(badLayout).RenameDongle("BadLayout",_=>Task.CompletedTask);}catch(InvalidDataException){layoutFailed=true;}
Require(layoutFailed&&badLayout.Writes==0,"unrecognized storage signature prevents name writes");
var verifyFailure=new MemoryExchange{CorruptReadback=true};bool verifyFailed=false;
try{await new DeviceClient(verifyFailure).RenameDongle("Check",_=>Task.CompletedTask);}catch(IOException){verifyFailed=true;}
Require(verifyFailed,"EEPROM readback mismatch is never reported as success");
var invalidName=new MemoryExchange();bool unicodeRejected=false;
try{await new DeviceClient(invalidName).RenameDongle("한글",_=>Task.CompletedTask);}catch(ArgumentException){unicodeRejected=true;}
Require(unicodeRejected&&invalidName.Requests.Count==0,"unsupported name encoding rejected before USB access");
var ledExchange=new ScriptedExchange();var ledClient=new DeviceClient(ledExchange){LedEnabled=false};await ledClient.StartPairing();
Require(ledExchange.Requests.Any(f=>DeviceProtocol.Word(f.AsSpan(1),4)==0x3800)&&!ledExchange.Requests.Any(f=>DeviceProtocol.Word(f.AsSpan(1),4)==0x3889),"LED off uses original all-zero indicator mode during pairing");
await ledClient.BeginInput();Require(DeviceProtocol.Word(ledExchange.Requests[^1].AsSpan(1),4)==0x3800,"LED off preserved when input capture begins");

Require(DongleNames.Encode(new string('A',31),31).Length==31,"original updater name capacity31 permits31 bytes without overflowing field");
Console.WriteLine("ALL CHECKS PASSED");

sealed class ScriptedExchange : IReportExchange
{
    public List<byte[]> Requests {get;}=[];
    public bool Reject {get;set;}
    public ushort? RejectWord {get;set;}
    public bool Enabled {get;private set;}
    public ushort ReadyFlags {get;init;}=0x8000;
    public string Version {get;init;}="EB V 2.1";
    public Task<byte[]> ExchangeAsync(byte[] frame,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();Requests.Add(frame.ToArray());
        ushort command=DeviceProtocol.Word(frame.AsSpan(1),1);
        if(command==0x7fff)Enabled=DeviceProtocol.Word(frame.AsSpan(1),4)==1;
        bool reject=Reject||RejectWord==DeviceProtocol.Word(frame.AsSpan(1),4)||(command==0&&!Enabled);
        var p=new byte[32];frame.AsSpan(1,4).CopyTo(p);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4),reject?(ushort)0:(ushort)1);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),reject?(ushort)0xffff:(ushort)0);
        if(DeviceProtocol.Word(frame.AsSpan(1),0)==0xeb00&&command==0x8000){BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),8);System.Text.Encoding.ASCII.GetBytes(Version).CopyTo(p,8);}
        if(command==0x7fff){BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4),0);BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),0);}
        if(command==0xffff){BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),16);BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8),Enabled?ReadyFlags:(ushort)0);}
        return Task.FromResult(p);
    }
}

sealed class MemoryExchange : IReportExchange
{
    private readonly ScriptedExchange normal=new();
    public List<byte[]> Requests=>normal.Requests;
    public bool Enabled=>normal.Enabled;
    public byte[] Memory {get;}=Enumerable.Repeat((byte)0x5a,192).ToArray();
    public bool BadSignature {get;init;}
    public bool CorruptReadback {get;init;}
    public int Writes {get;private set;}
    private int? pending;
    public MemoryExchange(){Array.Clear(Memory,60,32);System.Text.Encoding.ASCII.GetBytes("EasyBlue Tiny").CopyTo(Memory,60);}
    private static void Text(byte[] bytes,int offset,string text)=>System.Text.Encoding.ASCII.GetBytes(text).CopyTo(bytes,offset);
    public Task<byte[]> ExchangeAsync(byte[] frame,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(pending.HasValue)
        {
            if(frame.Length!=65||frame[0]!=3)throw new Exception("raw EEPROM page expected");
            frame.AsSpan(1).CopyTo(Memory.AsSpan(pending.Value,64));pending=null;Writes++;
            var ack=new byte[16];BinaryPrimitives.WriteUInt16LittleEndian(ack,0xeb04);return Task.FromResult(ack);
        }
        ushort magic=DeviceProtocol.Word(frame.AsSpan(1),0),command=DeviceProtocol.Word(frame.AsSpan(1),1);
        if(magic==0xeb02&&command==0x8000)
        {
            int page=DeviceProtocol.Word(frame.AsSpan(1),5);byte[] data=new byte[512];
            if(page==0){Text(data,0,BadSignature?"UNKNOWN SIGNATURE":"ZMT SERIAL FLASH");BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x1f0),0x02000000);}
            else if(page==1){Text(data,0,"ZARAM-USERSAVER");Text(data,0x18,"001.1000");BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x30),0x02000000);}
            else if(page==2){Text(data,0,"ZARAM BLUETOOTHS");Text(data,0x18,"00010000");BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x42),60);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x46),32);}
            return Task.FromResult(data);
        }
        if(magic==0xeb04)
        {
            int offset=DeviceProtocol.Word(frame.AsSpan(1),2);
            if(command==0x8000){var page=Memory.AsSpan(offset,64).ToArray();if(CorruptReadback&&Writes>0)page[0]^=1;return Task.FromResult(page);}
            if(command==0)pending=offset;
            var ack=new byte[16];BinaryPrimitives.WriteUInt16LittleEndian(ack,0xeb04);BinaryPrimitives.WriteUInt16LittleEndian(ack.AsSpan(2),command);return Task.FromResult(ack);
        }
        return normal.ExchangeAsync(frame,ct);
    }
}
