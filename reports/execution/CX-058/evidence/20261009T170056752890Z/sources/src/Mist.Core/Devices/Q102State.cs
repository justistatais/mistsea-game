using Mist.Core.Exploration;

namespace Mist.Core.Devices;

public enum WorkshopMode { Active, Focused, Isolated, Disabled }
public sealed record WorkshopState(WorkshopMode Mode, bool LatchOpen, bool LocalReserveAvailable = true)
{
    public bool SideOpen => Mode != WorkshopMode.Active;
    public bool FrontSafe => Mode is WorkshopMode.Isolated or WorkshopMode.Disabled;
    public WorkshopState Change(WorkshopMode mode) => Mode == WorkshopMode.Disabled && mode != WorkshopMode.Disabled ? throw new InvalidOperationException("Switching mode cannot repair a disabled unit.") : this with { Mode = mode };
}
public sealed record FerryState(bool Docked, bool BracketInstalled, bool BatteryConnected, bool BatteryCharged, long ConfigurationRevision, long? CertificateRevision, bool MainGeneratorRunning)
{
    public bool Ready => Docked && BracketInstalled && BatteryConnected && BatteryCharged && !MainGeneratorRunning && CertificateRevision == ConfigurationRevision;
    public FerryState Configure(bool? bracket = null, bool? connected = null) => this with { BracketInstalled = bracket ?? BracketInstalled, BatteryConnected = connected ?? BatteryConnected, ConfigurationRevision = checked(ConfigurationRevision + 1), CertificateRevision = null };
    public FerryState Test() => BracketInstalled && BatteryConnected && BatteryCharged && !MainGeneratorRunning ? this with { CertificateRevision = ConfigurationRevision } : throw new InvalidOperationException("安装或供电未完成，未签发测试证书。");
}
public sealed record ActorState(string Scene, Point2 Position, long ActionClock);
