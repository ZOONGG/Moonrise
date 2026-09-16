using System.Text;

namespace Moonrise.Services;

internal static class LegacyWeaveBridgeClasses
{
    public const string CommandBusEntryName = "moonrise/compat/LegacyCommandBus.class";

    public static byte[] CreateCommandBus()
    {
        using var stream = new MemoryStream();
        WriteU4(stream, 0xCAFEBABE);
        WriteU2(stream, 0);
        WriteU2(stream, 52);
        WriteU2(stream, 21);

        WriteUtf8(stream, "moonrise/compat/LegacyCommandBus"); // 1
        WriteClass(stream, 1); // 2
        WriteUtf8(stream, "java/lang/Object"); // 3
        WriteClass(stream, 3); // 4
        WriteUtf8(stream, "<init>"); // 5
        WriteUtf8(stream, "()V"); // 6
        WriteNameAndType(stream, 5, 6); // 7
        WriteMethodRef(stream, 4, 7); // 8
        WriteUtf8(stream, "Code"); // 9
        WriteUtf8(stream, "register"); // 10
        WriteUtf8(stream, "(Lnet/weavemc/api/command/Command;)V"); // 11
        WriteUtf8(stream, "net/weavemc/api/command/Command"); // 12
        WriteClass(stream, 12); // 13
        WriteUtf8(stream, "net/weavemc/api/command/CommandBus"); // 14
        WriteClass(stream, 14); // 15
        WriteUtf8(stream, "([Lnet/weavemc/api/command/Command;)V"); // 16
        WriteNameAndType(stream, 10, 16); // 17
        WriteMethodRef(stream, 15, 17); // 18
        WriteUtf8(stream, "SourceFile"); // 19
        WriteUtf8(stream, "LegacyCommandBus.java"); // 20

        WriteU2(stream, 0x0031);
        WriteU2(stream, 2);
        WriteU2(stream, 4);
        WriteU2(stream, 0);
        WriteU2(stream, 0);
        WriteU2(stream, 2);

        WriteMethod(stream, 0x0001, 5, 6, 1, 1,
            [0x2A, 0xB7, 0x00, 0x08, 0xB1]);
        WriteMethod(stream, 0x0009, 10, 11, 4, 1,
            [0x04, 0xBD, 0x00, 0x0D, 0x59, 0x03, 0x2A, 0x53, 0xB8, 0x00, 0x12, 0xB1]);

        WriteU2(stream, 1);
        WriteU2(stream, 19);
        WriteU4(stream, 2);
        WriteU2(stream, 20);
        return stream.ToArray();
    }

    private static void WriteMethod(
        Stream stream,
        ushort access,
        ushort name,
        ushort descriptor,
        ushort maxStack,
        ushort maxLocals,
        byte[] code)
    {
        WriteU2(stream, access);
        WriteU2(stream, name);
        WriteU2(stream, descriptor);
        WriteU2(stream, 1);
        WriteU2(stream, 9);
        WriteU4(stream, (uint)(12 + code.Length));
        WriteU2(stream, maxStack);
        WriteU2(stream, maxLocals);
        WriteU4(stream, (uint)code.Length);
        stream.Write(code);
        WriteU2(stream, 0);
        WriteU2(stream, 0);
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte(1);
        WriteU2(stream, checked((ushort)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteClass(Stream stream, ushort name)
    {
        stream.WriteByte(7);
        WriteU2(stream, name);
    }

    private static void WriteNameAndType(Stream stream, ushort name, ushort descriptor)
    {
        stream.WriteByte(12);
        WriteU2(stream, name);
        WriteU2(stream, descriptor);
    }

    private static void WriteMethodRef(Stream stream, ushort owner, ushort nameAndType)
    {
        stream.WriteByte(10);
        WriteU2(stream, owner);
        WriteU2(stream, nameAndType);
    }

    private static void WriteU2(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteU4(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
