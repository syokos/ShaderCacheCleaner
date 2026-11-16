namespace ShaderCacheCleaner.Helpers;

public static class ByteFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double value = bytes;
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit switch
        {
            0 => $"{bytes} {units[unit]}",
            1 => $"{value:0} {units[unit]}",
            _ => $"{value:0.##} {units[unit]}"
        };
    }

    public static string FormatFr(long bytes) => Format(bytes).Replace('.', ',');
}
