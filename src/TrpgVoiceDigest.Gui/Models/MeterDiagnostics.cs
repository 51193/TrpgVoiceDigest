namespace TrpgVoiceDigest.Gui.Models;

public sealed record MeterDiagnostics(
    string EffectiveInputDevice,
    string MeterStrategy,
    double LastRms,
    int MeterSuccessCount,
    int MeterErrorCount,
    double OnThreshold,
    double OffThreshold,
    string LastMeterAt);