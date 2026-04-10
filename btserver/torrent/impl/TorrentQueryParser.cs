namespace btserver.torrent.impl;

using System;
using System.Collections.Generic;

public static class TorrentQueryParser
{
    public static byte[] ExtractInfoHashFromRawUrl(string rawUrl)
    {
        if (string.IsNullOrEmpty(rawUrl))
            throw new ArgumentException("RawUrl is null or empty", nameof(rawUrl));

        int questionMark = rawUrl.IndexOf('?');
        if (questionMark < 0 || questionMark == rawUrl.Length - 1)
            throw new InvalidOperationException("No query string found in RawUrl.");

        string query = rawUrl[(questionMark + 1)..];
        string? encodedInfoHash = GetRawQueryParameter(query, "info_hash");

        if (encodedInfoHash is null)
            throw new InvalidOperationException("info_hash parameter missing.");

        byte[] bytes = PercentDecodeToBytes(encodedInfoHash);

        if (bytes.Length != 20)
            throw new InvalidOperationException($"info_hash must be 20 bytes, got {bytes.Length} bytes.");

        return bytes;
    }

    private static string? GetRawQueryParameter(string query, string key)
    {
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0)
                continue;

            string currentKey = part[..eq];
            if (string.Equals(currentKey, key, StringComparison.Ordinal))
                return part[(eq + 1)..];
        }

        return null;
    }

    private static byte[] PercentDecodeToBytes(string value)
    {
        var bytes = new List<byte>(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '%')
            {
                if (i + 2 >= value.Length)
                    throw new FormatException("Incomplete percent-encoding in query parameter.");

                string hex = value.Substring(i + 1, 2);
                bytes.Add(Convert.ToByte(hex, 16));
                i += 2;
            }
            else
            {
                // Query string raw ASCII byte
                bytes.Add((byte)c);
            }
        }

        return bytes.ToArray();
    }
}