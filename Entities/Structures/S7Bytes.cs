using System.Buffers.Binary;

namespace Entities.Structures
{
    // S7 big-endian bayt dizileri için okuma/yazma yardımcıları
    public static class S7Bytes
    {
        // Verilen adresteki Int (işaretli 16 bit) değeri okur.
        public static short GetInt(byte[] buffer, int offset)
        {
            return BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset, 2));
        }

        // Verilen adresteki DInt (işaretli 32 bit) değeri okur.
        public static int GetDInt(byte[] buffer, int offset)
        {
            return BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
        }

        // Verilen adresteki Real değeri okur; NaN ya da sonsuz gelirse 0 döner.
        public static float GetReal(byte[] buffer, int offset)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(buffer.AsSpan(offset, 4));
            return float.IsFinite(value) ? value : 0f;
        }

        // Verilen adresten başlayarak art arda duran Int dizisini okur.
        public static short[] GetIntArray(byte[] buffer, int offset, int count)
        {
            var result = new short[count];
            for (int i = 0; i < count; i++)
                result[i] = GetInt(buffer, offset + i * 2);
            return result;
        }

        // Verilen adresten başlayarak art arda duran Real dizisini okur.
        public static float[] GetRealArray(byte[] buffer, int offset, int count)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++)
                result[i] = GetReal(buffer, offset + i * 4);
            return result;
        }

        // Verilen adrese Int (işaretli 16 bit) değeri yazar.
        public static void SetInt(byte[] buffer, int offset, short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(offset, 2), value);
        }

        // Bit paketlenmiş Int içindeki bitin dolu olup olmadığını döner (bit 0 en düşük değerli bit).
        public static bool GetBit(short word, int bit)
        {
            return (word & (1 << bit)) != 0;
        }
    }
}