namespace EasyBlue.Core;
public static class StartupShortcutSpec
{
    public const string Arguments="--background";
    public const string FileName="EasyBlue Next.lnk";
    private static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Matches(string executable,string target,string arguments,string directory)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(target)||string.IsNullOrWhiteSpace(directory))return false;
            return string.Equals(Normalize(executable),Normalize(target),StringComparison.OrdinalIgnoreCase)
                &&arguments.Trim()==Arguments
                &&string.Equals(Normalize(Path.GetDirectoryName(executable)!),Normalize(directory),StringComparison.OrdinalIgnoreCase);
        }
        catch(ArgumentException){return false;}catch(NotSupportedException){return false;}
    }
}
