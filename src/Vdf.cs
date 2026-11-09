using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexSteamBridge
{
    public sealed class VdfEntry
    {
        public byte Type;
        public string Name;
        public object Value;
        public VdfEntry(byte type, string name, object value) { Type = type; Name = name; Value = value; }
    }
    public static class Vdf
    {
        static string ReadString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            byte value;
            while ((value = reader.ReadByte()) != 0) { bytes.Add(value); if (bytes.Count > 1024 * 1024) throw new InvalidDataException("VDF string too long"); }
            return new UTF8Encoding(false, true).GetString(bytes.ToArray());
        }
        static List<VdfEntry> ReadObject(BinaryReader reader, int depth)
        {
            if (depth > 32) throw new InvalidDataException("VDF too deep");
            var result = new List<VdfEntry>();
            while (true)
            {
                byte type = reader.ReadByte();
                if (type == 8) return result;
                string name = ReadString(reader);
                object value;
                if (type == 0) value = ReadObject(reader, depth + 1);
                else if (type == 1) value = ReadString(reader);
                else if (type == 2) value = reader.ReadUInt32();
                else if (type == 3 || type == 4 || type == 6) { var data = reader.ReadBytes(4); if (data.Length != 4) throw new EndOfStreamException(); value = data; }
                else if (type == 7) value = reader.ReadUInt64();
                else throw new InvalidDataException("Unsupported VDF type " + type);
                result.Add(new VdfEntry(type, name, value));
            }
        }
        public static List<VdfEntry> Read(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            { var result = ReadObject(reader, 0); if (stream.Position != stream.Length) throw new InvalidDataException("Trailing VDF data"); return result; }
        }
        static void WriteString(BinaryWriter writer, string value) { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
        static void WriteObject(BinaryWriter writer, List<VdfEntry> entries)
        {
            foreach (var entry in entries)
            {
                writer.Write(entry.Type); WriteString(writer, entry.Name);
                if (entry.Type == 0) WriteObject(writer, (List<VdfEntry>)entry.Value);
                else if (entry.Type == 1) WriteString(writer, (string)entry.Value);
                else if (entry.Type == 2) writer.Write((uint)entry.Value);
                else if (entry.Type == 7) writer.Write((ulong)entry.Value);
                else writer.Write((byte[])entry.Value);
            }
            writer.Write((byte)8);
        }
        public static byte[] Write(List<VdfEntry> entries)
        { using (var stream = new MemoryStream()) { using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) WriteObject(writer, entries); return stream.ToArray(); } }
        public static string Text(List<VdfEntry> entries, string name)
        { var item = entries.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); return item == null ? "" : Convert.ToString(item.Value); }
        public static void Set(List<VdfEntry> entries, byte type, string name, object value)
        { var item = entries.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (item == null) entries.Add(new VdfEntry(type, name, value)); else { item.Type = type; item.Value = value; } }
        public static uint ShortcutId(string quotedExe, string name)
        {
            uint crc = 0xffffffff;
            foreach (byte value in Encoding.UTF8.GetBytes(quotedExe + name))
            { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0); }
            return (crc ^ 0xffffffff) | 0x80000000;
        }
        public static AccountConfig EnsureShortcut(List<VdfEntry> tree, string root, string accountId, string icon)
        {
            var container = tree.FirstOrDefault(x => x.Type == 0 && x.Name == "shortcuts");
            if (container == null) { container = new VdfEntry(0, "shortcuts", new List<VdfEntry>()); tree.Add(container); }
            var shortcuts = (List<VdfEntry>)container.Value;
            string executable = "\"" + Path.Combine(root, "CodexSteamBridge.exe") + "\"";
            var existing = shortcuts.Where(x => x.Type == 0).Select(x => (List<VdfEntry>)x.Value)
                .FirstOrDefault(x => string.Equals(Text(x, "Exe"), executable, StringComparison.OrdinalIgnoreCase));
            uint appId;
            if (existing == null)
            {
                appId = ShortcutId(executable, "Codex");
                if (shortcuts.Any(x => x.Type == 0 && Text((List<VdfEntry>)x.Value, "appid") == appId.ToString()))
                    throw new InvalidDataException("Steam shortcut ID collision");
                existing = new List<VdfEntry>(); int index = 0;
                while (shortcuts.Any(x => x.Name == index.ToString())) index++;
                shortcuts.Add(new VdfEntry(0, index.ToString(), existing));
                Set(existing, 2, "appid", appId); Set(existing, 1, "AppName", "Codex");
                Set(existing, 1, "Exe", executable); Set(existing, 1, "StartDir", "\"" + root + "\"");
                Set(existing, 1, "LaunchOptions", "presence"); Set(existing, 1, "icon", icon);
                foreach (string key in new[] { "IsHidden", "AllowDesktopConfig", "AllowOverlay", "OpenVR", "Devkit", "DevkitOverrideAppID", "LastPlayTime" }) Set(existing, 2, key, (uint)0);
                Set(existing, 1, "ShortcutPath", ""); Set(existing, 1, "DevkitGameID", ""); Set(existing, 1, "FlatpakAppID", "");
                Set(existing, 0, "tags", new List<VdfEntry> { new VdfEntry(1, "0", "CodexSteamBridge") });
            }
            else
            {
                if (!uint.TryParse(Text(existing, "appid"), out appId)) throw new InvalidDataException("Invalid existing shortcut ID");
                Set(existing, 1, "LaunchOptions", "presence");
                // Preserve the user's library title, icon and artwork on repair/migration.
            }
            return new AccountConfig { AccountId = accountId, AppId = appId.ToString(), GameId = (((ulong)appId << 32) | 0x02000000UL).ToString() };
        }
        public static void RemoveShortcut(List<VdfEntry> tree, string root, IEnumerable<AccountConfig> accounts)
        {
            var container = tree.FirstOrDefault(x => x.Type == 0 && x.Name == "shortcuts"); if (container == null) return;
            string executable = "\"" + Path.Combine(root, "CodexSteamBridge.exe") + "\"";
            ((List<VdfEntry>)container.Value).RemoveAll(x => x.Type == 0 &&
                string.Equals(Text((List<VdfEntry>)x.Value, "Exe"), executable, StringComparison.OrdinalIgnoreCase) &&
                accounts.Any(a => a.AppId == Text((List<VdfEntry>)x.Value, "appid")));
        }
    }
}
