using System.Buffers.Binary;
using System.Text;

namespace Moonrise.Services;

internal static class JavaClassUtf8Rewriter
{
    private static readonly byte[] LegacyHookMarker = Encoding.ASCII.GetBytes("net/weavemc/loader/api/Hook");
    private static readonly byte[] LegacyInitializerMarker = Encoding.ASCII.GetBytes("net/weavemc/loader/api/ModInitializer");
    private static readonly byte[] LegacyEventBusMarker = Encoding.ASCII.GetBytes("net/weavemc/loader/api/event/EventBus");
    private static readonly byte[] LegacyCommandMarker = Encoding.ASCII.GetBytes("net/weavemc/loader/api/command/Command");
    private static readonly byte[] LegacyCommandBusMarker = Encoding.ASCII.GetBytes("net/weavemc/loader/api/command/CommandBus");
    private static readonly byte[] LegacyPreInitName = Encoding.ASCII.GetBytes("preInit");
    private static readonly byte[] CurrentInitName = Encoding.ASCII.GetBytes("init");
    private static readonly byte[] LegacyCallEventName = Encoding.ASCII.GetBytes("callEvent");
    private static readonly byte[] CurrentPostEventName = Encoding.ASCII.GetBytes("postEvent");
    private static readonly byte[] LegacyCommandHandleName = Encoding.ASCII.GetBytes("handle");
    private static readonly byte[] CurrentCommandExecuteName = Encoding.ASCII.GetBytes("execute");

    private static readonly (byte[] From, byte[] To)[] Replacements =
    [
        Pair("net/weavemc/loader/api/event/ChatReceivedEvent", "net/weavemc/api/event/ChatEvent$Received"),
        Pair("net/weavemc/loader/api/event/ChatSentEvent", "net/weavemc/api/event/ChatEvent$Sent"),
        Pair("net.weavemc.loader.api.event.ChatReceivedEvent", "net.weavemc.api.event.ChatEvent$Received"),
        Pair("net.weavemc.loader.api.event.ChatSentEvent", "net.weavemc.api.event.ChatEvent$Sent"),
        Pair("net/weavemc/loader/api/util/InsnBuilder", "net/weavemc/internals/InsnBuilder"),
        Pair("net/weavemc/loader/api/util/InsnDslKt", "net/weavemc/internals/InsnDslKt"),
        Pair("net.weavemc.loader.api.util.InsnBuilder", "net.weavemc.internals.InsnBuilder"),
        Pair("net.weavemc.loader.api.util.InsnDslKt", "net.weavemc.internals.InsnDslKt"),
        Pair("net/weavemc/loader/api/", "net/weavemc/api/"),
        Pair("net.weavemc.loader.api.", "net.weavemc.api.")
    ];

    private static readonly (byte[] From, byte[] To)[] LegacyHookAsmReplacements =
    [
        Pair("org/objectweb/asm/", "net/weavemc/loader/impl/shaded/asm/"),
        Pair("org.objectweb.asm.", "net.weavemc.loader.impl.shaded.asm.")
    ];

    private static readonly (byte[] From, byte[] To)[] LegacyCommandBusReplacements =
    [
        Pair("net/weavemc/loader/api/command/CommandBus", "moonrise/compat/LegacyCommandBus"),
        Pair("net.weavemc.loader.api.command.CommandBus", "moonrise.compat.LegacyCommandBus")
    ];

    public static bool TryRewrite(byte[] classFile, out byte[] rewritten)
    {
        rewritten = classFile;
        if (classFile.Length < 10 || BinaryPrimitives.ReadUInt32BigEndian(classFile) != 0xCAFEBABE)
            return false;

        var constantPoolCount = BinaryPrimitives.ReadUInt16BigEndian(classFile.AsSpan(8, 2));
        var adaptLegacyHookAsm = classFile.AsSpan().IndexOf(LegacyHookMarker) >= 0;
        var adaptLegacyInitializer = classFile.AsSpan().IndexOf(LegacyInitializerMarker) >= 0;
        var adaptLegacyEventBus = classFile.AsSpan().IndexOf(LegacyEventBusMarker) >= 0;
        var adaptLegacyCommand = classFile.AsSpan().IndexOf(LegacyCommandMarker) >= 0;
        var adaptLegacyCommandBus = classFile.AsSpan().IndexOf(LegacyCommandBusMarker) >= 0;
        var inputOffset = 10;
        using var output = new MemoryStream(classFile.Length);
        output.Write(classFile, 0, inputOffset);
        var changed = false;
        var encodedLength = new byte[2];

        for (var index = 1; index < constantPoolCount; index++)
        {
            EnsureAvailable(classFile, inputOffset, 1);
            var tag = classFile[inputOffset++];
            output.WriteByte(tag);

            if (tag == 1)
            {
                EnsureAvailable(classFile, inputOffset, 2);
                var length = BinaryPrimitives.ReadUInt16BigEndian(classFile.AsSpan(inputOffset, 2));
                inputOffset += 2;
                EnsureAvailable(classFile, inputOffset, length);

                var value = classFile.AsSpan(inputOffset, length).ToArray();
                if (adaptLegacyInitializer && value.AsSpan().SequenceEqual(LegacyPreInitName))
                {
                    value = CurrentInitName;
                    changed = true;
                }
                if (adaptLegacyEventBus && value.AsSpan().SequenceEqual(LegacyCallEventName))
                {
                    value = CurrentPostEventName;
                    changed = true;
                }
                if (adaptLegacyCommand && value.AsSpan().SequenceEqual(LegacyCommandHandleName))
                {
                    value = CurrentCommandExecuteName;
                    changed = true;
                }
                if (adaptLegacyCommandBus)
                    foreach (var (from, to) in LegacyCommandBusReplacements)
                        value = Replace(value, from, to, ref changed);
                foreach (var (from, to) in Replacements)
                    value = Replace(value, from, to, ref changed);
                if (adaptLegacyHookAsm)
                    foreach (var (from, to) in LegacyHookAsmReplacements)
                        value = Replace(value, from, to, ref changed);

                if (value.Length > ushort.MaxValue)
                    throw new InvalidDataException("A rewritten Java constant exceeds the class-file UTF-8 limit.");

                BinaryPrimitives.WriteUInt16BigEndian(encodedLength, (ushort)value.Length);
                output.Write(encodedLength);
                output.Write(value);
                inputOffset += length;
                continue;
            }

            var payloadLength = tag switch
            {
                3 or 4 or 9 or 10 or 11 or 12 or 17 or 18 => 4,
                5 or 6 => 8,
                7 or 8 or 16 or 19 or 20 => 2,
                15 => 3,
                _ => throw new InvalidDataException($"Unsupported Java constant-pool tag {tag}.")
            };
            EnsureAvailable(classFile, inputOffset, payloadLength);
            output.Write(classFile, inputOffset, payloadLength);
            inputOffset += payloadLength;
            if (tag is 5 or 6) index++;
        }

        EnsureAvailable(classFile, inputOffset, 0);
        output.Write(classFile, inputOffset, classFile.Length - inputOffset);
        if (changed) rewritten = output.ToArray();
        return changed;
    }

    private static byte[] Replace(byte[] value, byte[] from, byte[] to, ref bool changed)
    {
        var first = value.AsSpan().IndexOf(from);
        if (first < 0) return value;

        using var output = new MemoryStream(value.Length);
        var offset = 0;
        while (first >= 0)
        {
            output.Write(value, offset, first);
            output.Write(to);
            changed = true;
            offset += first + from.Length;
            first = value.AsSpan(offset).IndexOf(from);
        }
        output.Write(value, offset, value.Length - offset);
        return output.ToArray();
    }

    private static void EnsureAvailable(byte[] value, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > value.Length - length)
            throw new InvalidDataException("The Java class file is truncated.");
    }

    private static (byte[] From, byte[] To) Pair(string from, string to) =>
        (Encoding.ASCII.GetBytes(from), Encoding.ASCII.GetBytes(to));
}
