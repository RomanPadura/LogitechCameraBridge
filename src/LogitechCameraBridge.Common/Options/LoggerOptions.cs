namespace LogitechCameraBridge.Common.Options;

public class LoggerOptions
{
    public const string SectionName = "Logger";

    public bool LogToConsole { get; set; }

    /// <summary>Maximum retained file entries. Zero or less disables file logging.</summary>
    public int MaxTextFileLines { get; set; } = 1000;
}
