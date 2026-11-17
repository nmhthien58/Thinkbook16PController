using System.Management;

namespace Tb16p.Core.Lenovo;

/// <summary>
/// One sample of the EC sensors. Sensor numbers follow the BIOS fan tables
/// (LENOVO_FAN_TABLE_DATA Sensor_ID 1, 4, 5); sensor 4 tracks the CPU.
/// </summary>
public readonly record struct SensorReading(int? Sensor1C, int? CpuC, int? Sensor5C, int? Fan1Rpm, int? Fan2Rpm);

/// <summary>
/// Read-only sensors from Lenovo's GameZone WMI interface (ACPI\PNP0C14\GMZN). The DSDT
/// maps these feature ids straight to EC registers; requires administrator rights.
/// </summary>
public sealed class GameZoneSensors : IDisposable
{
    private const uint Sensor1Temperature = 0x05010000;
    private const uint CpuTemperature = 0x05040000;
    private const uint Sensor5Temperature = 0x05050000;
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

    public SensorReading Read() => new(
        Get(Sensor1Temperature), Get(CpuTemperature), Get(Sensor5Temperature), Get(Fan1Speed), Get(Fan2Speed));

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
