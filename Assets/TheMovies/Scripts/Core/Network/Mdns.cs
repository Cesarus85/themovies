using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TheMovies.Core.Network
{
    public sealed class MdnsService
    {
        /// <summary>Instance name, e.g. "DiskStation".</summary>
        public string Name;
        public string HostName;
        public string Address;
        public int Port;
    }

    /// <summary>
    /// Finds services announced via Bonjour/mDNS (NAS devices announce "_smb._tcp"). Uses a one-shot query
    /// from an ordinary port, which responders answer by unicast, so no multicast lock is needed.
    /// </summary>
    public static class Mdns
    {
        const ushort TypeA = 1;
        const ushort TypePtr = 12;
        const ushort TypeSrv = 33;

        public static List<MdnsService> Discover(string service, int timeoutMs)
        {
            var records = new Records();
            ushort id = (ushort)new Random().Next(1, 65535);
            byte[] query = BuildQuery(service, id);
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.ReceiveTimeout = 300;
                var target = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                int sent = 0;
                var nextSend = DateTime.UtcNow;
                while (DateTime.UtcNow < deadline)
                {
                    if (sent < 3 && DateTime.UtcNow >= nextSend)
                    {
                        try { udp.Send(query, query.Length, target); }
                        catch (SocketException) { }
                        sent++;
                        nextSend = DateTime.UtcNow.AddMilliseconds(700);
                    }
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref from);
                        Parse(data, records, from.Address.ToString());
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
            return records.Resolve(service);
        }

        public static byte[] BuildQuery(string name, ushort id)
        {
            var w = new List<byte>
            {
                (byte)(id >> 8), (byte)id,
                0, 0, // standard query
                0, 1, // one question
                0, 0, 0, 0, 0, 0,
            };
            foreach (string label in name.TrimEnd('.').Split('.'))
            {
                byte[] b = Encoding.UTF8.GetBytes(label);
                w.Add((byte)b.Length);
                w.AddRange(b);
            }
            w.Add(0);
            w.Add(0); w.Add((byte)TypePtr);
            w.Add(0); w.Add(1); // class IN
            return w.ToArray();
        }

        /// <summary>Everything learned from the answers so far.</summary>
        public sealed class Records
        {
            public readonly Dictionary<string, List<string>> Pointers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, (string target, int port)> Services = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> SenderOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public List<MdnsService> Resolve(string service)
            {
                var result = new List<MdnsService>();
                if (!Pointers.TryGetValue(service.TrimEnd('.'), out var instances)) return result;
                foreach (string instance in instances)
                {
                    string name = instance;
                    string suffix = "." + service.TrimEnd('.');
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);
                    var s = new MdnsService { Name = name, Port = Smb2Client.DefaultPort };
                    if (Services.TryGetValue(instance, out var srv))
                    {
                        s.HostName = srv.target;
                        s.Port = srv.port;
                        Addresses.TryGetValue(srv.target, out s.Address);
                    }
                    if (s.Address == null) SenderOf.TryGetValue(instance, out s.Address);
                    if (s.Address != null) result.Add(s);
                }
                return result;
            }
        }

        public static void Parse(byte[] p, Records records, string sender)
        {
            if (p.Length < 12) return;
            int qd = (p[4] << 8) | p[5];
            int records1 = ((p[6] << 8) | p[7]) + ((p[8] << 8) | p[9]) + ((p[10] << 8) | p[11]);
            int pos = 12;
            try
            {
                for (int i = 0; i < qd; i++)
                {
                    ReadName(p, ref pos);
                    pos += 4;
                }
                for (int i = 0; i < records1 && pos + 10 <= p.Length; i++)
                {
                    string name = ReadName(p, ref pos);
                    int type = (p[pos] << 8) | p[pos + 1];
                    int length = (p[pos + 8] << 8) | p[pos + 9];
                    pos += 10;
                    int dataStart = pos;
                    if (dataStart + length > p.Length) return;
                    switch (type)
                    {
                        case TypePtr:
                        {
                            int r = dataStart;
                            string target = ReadName(p, ref r);
                            if (!records.Pointers.TryGetValue(name, out var list))
                                records.Pointers[name] = list = new List<string>();
                            if (!list.Contains(target)) list.Add(target);
                            if (sender != null && !records.SenderOf.ContainsKey(target)) records.SenderOf[target] = sender;
                            break;
                        }
                        case TypeSrv:
                        {
                            if (length < 7) break;
                            int port = (p[dataStart + 4] << 8) | p[dataStart + 5];
                            int r = dataStart + 6;
                            records.Services[name] = (ReadName(p, ref r), port);
                            break;
                        }
                        case TypeA:
                            if (length == 4)
                                records.Addresses[name] = $"{p[dataStart]}.{p[dataStart + 1]}.{p[dataStart + 2]}.{p[dataStart + 3]}";
                            break;
                    }
                    pos = dataStart + length;
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Truncated packet: keep what was parsed
            }
        }

        static string ReadName(byte[] p, ref int pos)
        {
            var labels = new List<string>();
            int jumps = 0;
            int cursor = pos;
            bool jumped = false;
            while (true)
            {
                int len = p[cursor];
                if (len == 0)
                {
                    cursor++;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    int target = ((len & 0x3F) << 8) | p[cursor + 1];
                    if (!jumped) pos = cursor + 2;
                    jumped = true;
                    cursor = target;
                    if (++jumps > 32) throw new IndexOutOfRangeException();
                    continue;
                }
                labels.Add(Encoding.UTF8.GetString(p, cursor + 1, len));
                cursor += 1 + len;
            }
            if (!jumped) pos = cursor;
            return string.Join(".", labels);
        }
    }
}
