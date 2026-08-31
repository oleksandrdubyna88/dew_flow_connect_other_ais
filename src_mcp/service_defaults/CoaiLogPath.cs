namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// The one place the log path shape lives: <c>logs/{yyyy-MM-dd}/{app}-{HH-mm-ss}-{pid}.log</c>.
/// </summary>
/// <remarks>
/// <para>A folder per day, a file per RUN — never a rolling sink, which appends every run into one
/// file when the question being asked is almost always "what did <i>that</i> run do". The pid
/// disambiguates two hosts started in the same second.</para>
/// <para><b>Everything is UTC</b> — the folder and the file name here, the line timestamps in
/// <see cref="CoaiTextFormatter"/>. One clock everywhere, so logs from different hosts of the
/// family land in the same day folder (.claude/rules/shared/common/logging-serilog.md).</para>
/// <para>Pure, so the shape is a unit test rather than something discovered on disk.</para>
/// </remarks>
public static class CoaiLogPath
{
    public static string For(string logsRoot, string appName, DateTime utcNow, int pid) =>
        Path.Combine(
            logsRoot,
            utcNow.ToString("yyyy-MM-dd"),
            $"{appName}-{utcNow:HH-mm-ss}-{pid}.log");
}
