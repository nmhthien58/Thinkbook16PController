using System.Management;

namespace Tb16p.Core.Lenovo;

public readonly record struct SensorReading(int? CpuTempC, int? Fan1Rpm, int? Fan2Rpm);

/// <summary>
/// Read-only sensors from Lenovo's GameZone WMI interface (ACPI\PNP0C14\GMZN).
/// On this ThinkBook the GameZone setters are ignored, but the feature getters for
/// temperature and fan speed return live values. Requires administrator rights.
/// </summary>
public sealed class GameZoneSensors : IDisposable
{
    private const uint CpuTemperature = 0x05040000;
    private const uint Fan1Speed = 0x04030001;
    private const uint Fan2Speed = 0x04030002;

    private readonly ManagementObject _method;

    private GameZoneSensors(ManagementObject method) => _method = method;

    public static GameZoneSensors? TryOpen()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
            var method = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
            return method is null ? null : new GameZoneSensors(method);
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    public SensorReading Read() => new(Get(CpuTemperature), Get(Fan1Speed), Get(Fan2Speed));

    private int? Get(uint id)
    {
        try
        {
            using var input = _method.GetMethodParameters("GetFeatureValue");
            input["IDs"] = id;
            using var output = _method.InvokeMethod("GetFeatureValue", input, null);
            var value = Convert.ToInt32(output["value"]);
            return value > 0 ? value : null;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    public void Dispose() => _method.Dispose();
}
