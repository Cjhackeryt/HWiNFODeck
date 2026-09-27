namespace HWiNFODeck;

public sealed record HWiNFOSensor(
    uint SensorId,
    string SensorName,
    uint ReadingId,
    string ReadingName,
    string Unit,
    double Value,
    string SensorType = "",
    string HardwareType = "")
{
    public string VariableId => $"sensor_{SensorId}_{ReadingId}";
}
