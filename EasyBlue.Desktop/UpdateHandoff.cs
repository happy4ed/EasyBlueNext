using System.Diagnostics;
using System.IO;
namespace EasyBlue.Desktop;
internal static class UpdateHandoff
{
    public const string QuitEvent=@"Local\EasyBlueNext.Quit";
    // Exit 3 means the old version does not implement safe quit IPC. Never force a new version mid-write.
    public static int StopExisting(string executable,bool forceLegacy)
    {
        executable=Path.GetFullPath(executable);
        using var current=Process.GetCurrentProcess();
        var targets=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            var matching=targets.Where(p=>p.Id!=current.Id&&Matches(p,executable)).ToArray();
            if(matching.Length==0)return 0;
            bool graceful=EventWaitHandle.TryOpenExisting(QuitEvent,out var quit);
            using(quit){if(graceful)quit!.Set();}
            if(!graceful&&!forceLegacy)return 3;
            if(!graceful)
            {
                foreach(var process in matching)if(!process.HasExited)process.Kill();
            }
            var deadline=Stopwatch.StartNew();
            foreach(var process in matching)
            {
                int remaining=30000-(int)deadline.ElapsedMilliseconds;
                if(remaining<=0||!process.WaitForExit(Math.Max(1,remaining)))return 2;
            }
            return 0;
        }
        finally{foreach(var process in targets)process.Dispose();}
    }
    private static bool Matches(Process process,string executable)
    {
        try{return string.Equals(process.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase);}
        catch(InvalidOperationException){return false;}
        // Access denied is an error, not proof that the target is safe to overwrite.
    }
}
