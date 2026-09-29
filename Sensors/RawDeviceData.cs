using System;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// A BLE characteristic value captured before it is interpreted by a parser.
    /// </summary>
    public sealed record RawDeviceData(
        DateTimeOffset TimestampUtc,
        string Source,
        string DeviceName,
        string DeliveryMethod,
        Guid ServiceUuid,
        Guid CharacteristicUuid,
        byte[] Payload);
}
