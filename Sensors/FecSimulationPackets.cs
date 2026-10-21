using System;

namespace ErgTrainer.Sensors;

// FE-C data pages 50, 51 and 55 inside the Tacx FE-C-over-BLE ANT frame.
internal static class FecSimulationPackets
{
    public static byte[] UserConfiguration(decimal riderKg, decimal bikeKg, int wheelCircumferenceMm)
    {
        if (riderKg <= 0 || riderKg > 655.34m) throw new ArgumentOutOfRangeException(nameof(riderKg));
        if (bikeKg <= 0 || bikeKg > 50m) throw new ArgumentOutOfRangeException(nameof(bikeKg));
        if (wheelCircumferenceMm < 500 || wheelCircumferenceMm > 4000)
            throw new ArgumentOutOfRangeException(nameof(wheelCircumferenceMm));

        // FE-C page 55 encodes wheel diameter in centimetres plus a millimetre offset.
        int diameterMm = (int)Math.Round(wheelCircumferenceMm / Math.PI);
        int bikeWeightUnits = (int)Math.Round(bikeKg * 20m);
        int riderWeightUnits = (int)Math.Round(riderKg * 100m);
        byte[] data = { 0x37, 0, 0, 0xFF, 0, 0, 0, 0 };
        data[1] = (byte)riderWeightUnits;
        data[2] = (byte)(riderWeightUnits >> 8);
        data[4] = (byte)((diameterMm % 10) | ((bikeWeightUnits & 0x0F) << 4));
        data[5] = (byte)(bikeWeightUnits >> 4);
        data[6] = (byte)(diameterMm / 10);
        // The manual test uses a fixed 2:1 gear ratio (nearest FE-C step is 1.98).
        data[7] = 66;
        return Frame(data);
    }

    public static byte[] NeutralWind() => Frame(new byte[] { 0x32, 0xFF, 0xFF, 0xFF, 0xFF, 0, 127, 100 });

    // FE-C common page 70 asks the trainer to transmit command-status page 71.
    public static byte[] RequestCommandStatus() =>
        Frame(new byte[] { 0x46, 0xFF, 0xFF, 0xFF, 0xFF, 0x80, 0x47, 0x01 });

    public static byte[] TrackResistance(decimal gradientPercent)
    {
        if (gradientPercent < -20 || gradientPercent > 20)
            throw new ArgumentOutOfRangeException(nameof(gradientPercent));
        int gradeUnits = (int)Math.Round((gradientPercent + 200m) * 100m);
        // Rolling resistance coefficient 0.004 / 0.00005 = 80.
        return Frame(new byte[] { 0x33, 0xFF, 0xFF, 0xFF, 0xFF,
            (byte)gradeUnits, (byte)(gradeUnits >> 8), 80 });
    }

    public static bool TryReadCommandStatus(byte[]? frame, byte expectedPage, out byte status)
    {
        status = 0;
        if (frame == null || frame.Length != 13 || frame[0] != 0xA4 || frame[1] != 0x09 ||
            frame[3] != 0x05 || frame[4] != 0x47 || frame[5] != expectedPage)
            return false;
        byte checksum = 0;
        foreach (byte value in frame) checksum ^= value;
        if (checksum != 0) return false;
        status = frame[7];
        return true;
    }

    private static byte[] Frame(byte[] data)
    {
        byte[] frame = new byte[13];
        frame[0] = 0xA4;
        frame[1] = 0x09;
        frame[2] = 0x4F;
        frame[3] = 0x05;
        Array.Copy(data, 0, frame, 4, 8);
        for (int i = 0; i < 12; i++) frame[12] ^= frame[i];
        return frame;
    }
}
