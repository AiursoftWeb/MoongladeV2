namespace Aiursoft.MoongladeV2.Configuration;

public sealed class ViewCountOptions
{
    public TimeSpan ArchivePeriod { get; set; } = TimeSpan.FromMinutes(1);
}
