// SPDX-License-Identifier: MIT
// Public algorithm references are recorded in NOTICE.md.
using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GpStudio;

internal static class CryptoUtilities
{
    private static uint Rotate(uint x, int n) => BitOperations.RotateLeft(x, n);
    private static uint P0(uint x) => x ^ Rotate(x, 9) ^ Rotate(x, 17);
    private static uint P1(uint x) => x ^ Rotate(x, 15) ^ Rotate(x, 23);
    internal static byte[] Sm3(byte[] input)
    {
        if (input.Length > 1024 * 1024 + 16384) throw new ArgumentException("Input exceeds utility limit.");
        byte[] padded = new byte[((input.Length + 9 + 63) / 64) * 64]; input.CopyTo(padded, 0); padded[input.Length] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length - 8), (ulong)input.Length * 8);
        uint[] state = [0x7380166f, 0x4914b2b9, 0x172442d7, 0xda8a0600, 0xa96f30bc, 0x163138aa, 0xe38dee4d, 0xb0fb0e4e];
        uint[] schedule = new uint[68];
        for (int offset = 0; offset < padded.Length; offset += 64)
        {
            for (int i = 0; i < 16; i++) schedule[i] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(offset + i * 4));
            for (int i = 16; i < 68; i++) schedule[i] = P1(schedule[i - 16] ^ schedule[i - 9] ^ Rotate(schedule[i - 3], 15)) ^ Rotate(schedule[i - 13], 7) ^ schedule[i - 6];
            uint a = state[0], b = state[1], c = state[2], d = state[3], e = state[4], f = state[5], g = state[6], h = state[7];
            for (int j = 0; j < 64; j++)
            {
                uint ss1 = Rotate(unchecked(Rotate(a, 12) + e + Rotate(j < 16 ? 0x79cc4519u : 0x7a879d8au, j)), 7);
                uint ff = j < 16 ? a ^ b ^ c : (a & b) | (a & c) | (b & c);
                uint gg = j < 16 ? e ^ f ^ g : (e & f) | (~e & g);
                uint t1 = unchecked(ff + d + (ss1 ^ Rotate(a, 12)) + (schedule[j] ^ schedule[j + 4]));
                uint t2 = unchecked(gg + h + ss1 + schedule[j]);
                d = c; c = Rotate(b, 9); b = a; a = t1;
                h = g; g = Rotate(f, 19); f = e; e = P0(t2);
            }
            state[0] ^= a; state[1] ^= b; state[2] ^= c; state[3] ^= d; state[4] ^= e; state[5] ^= f; state[6] ^= g; state[7] ^= h;
        }
        byte[] result = new byte[32]; for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), state[i]);
        CryptographicOperations.ZeroMemory(padded); return result;
    }
    private const string Prime = "FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF";
    private const string A = "FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFC";
    private const string B = "28E9FA9E9D9F5E344D5A9E4BCF6509A7F39789F515AB8F92DDBCBD414D940E93";
    private const string Gx = "32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7";
    private const string Gy = "BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0";
    private static BigInteger Number(byte[] data) => new(data, true, true);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] X, byte[] Y)> PublicKeys = new();
    internal static (byte[] X, byte[] Y) Sm2PublicKey(string scalarHex) => PublicKeys.GetOrAdd(scalarHex, text => {
        byte[] bytes = RequestBuilder.Hex(text, "SM2 private key", 32);
        BigInteger k = Number(bytes), p = Number(Convert.FromHexString(Prime));
        BigInteger order = Number(Convert.FromHexString("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFF7203DF6B21C6052B53BBF40939D54123"));
        if (bytes.Length != 32 || k <= 0 || k >= order) throw new ArgumentException("SM2 private key must be a 32-byte scalar in [1, n-1].");
        BigInteger Mod(BigInteger n) => (n % p + p) % p;
        BigInteger gx = Number(Convert.FromHexString(Gx)), gy = Number(Convert.FromHexString(Gy));
        BigInteger x = 0, y = 1, z = 0;
        void Double() {
            if (z == 0 || y == 0) { x = 0; y = 1; z = 0; return; }
            BigInteger yy = Mod(y*y), zz = Mod(z*z), m = Mod(3*(x*x-zz*zz)), t = Mod(4*x*yy);
            BigInteger nx = Mod(m*m-2*t), ny = Mod(m*(t-nx)-8*yy*yy);
            z = Mod(2*y*z); x = nx; y = ny;
        }
        for (int bit = 255; bit >= 0; --bit) {
            Double();
            if (((k >> bit) & 1) == 0) continue;
            if (z == 0) { x = gx; y = gy; z = 1; continue; }
            BigInteger zz = Mod(z*z), h = Mod(gx*zz-x), r = Mod(gy*zz*z-y);
            if (h == 0) { if (r == 0) Double(); else { x = 0; y = 1; z = 0; } continue; }
            BigInteger hh = Mod(h*h), hhh = Mod(h*hh), v = Mod(x*hh), nx = Mod(r*r-hhh-2*v);
            BigInteger ny = Mod(r*(v-nx)-y*hhh); z = Mod(z*h); x = nx; y = ny;
        }
        BigInteger inverse = BigInteger.ModPow(z, p-2, p); x = Mod(x*inverse*inverse); y = Mod(y*inverse*inverse*inverse);
        byte[] Encode(BigInteger n) { byte[] raw = n.ToByteArray(true, true), result = new byte[32]; raw.CopyTo(result, 32-raw.Length); return result; }
        return (Encode(x), Encode(y));
    });
    internal static byte[] Za(byte[] id, byte[] x, byte[] y)
    {
        if (id.Length > 8191 || x.Length != 32 || y.Length != 32) throw new ArgumentException("ID: 0..8191 bytes; X and Y: 32 bytes each.");
        BigInteger p = Number(Convert.FromHexString(Prime)), px = Number(x), py = Number(y);
        if (px >= p || py >= p || (py * py - px * px * px - Number(Convert.FromHexString(A)) * px - Number(Convert.FromHexString(B))) % p != 0)
            throw new ArgumentException("Public key is not on the standard SM2 curve.");
        byte[] body = new byte[2 + id.Length + 192]; BinaryPrimitives.WriteUInt16BigEndian(body, (ushort)(id.Length * 8)); id.CopyTo(body, 2);
        Convert.FromHexString(A + B + Gx + Gy).CopyTo(body, 2 + id.Length); x.CopyTo(body, 130 + id.Length); y.CopyTo(body, 162 + id.Length);
        return Sm3(body);
    }
    internal static string CArray(string name, byte[] value) => "/* SPDX-License-Identifier: MIT */\n#pragma once\n#include <stddef.h>\n#include <stdint.h>\n\nstatic const uint8_t " + name + "[] = {\n" +
        string.Join("\n", value.Chunk(12).Select(row => "    " + string.Join(", ", row.Select(b => "0x" + b.ToString("x2"))) + ",")) + "\n};\nstatic const size_t " + name + "_size = sizeof(" + name + ");\n";
    internal static CertificateInfo Certificate(byte[] input)
    {
        if (input.Length > 1024 * 1024) throw new ArgumentException("Certificate exceeds 1 MiB.");
        byte[] der = input;
        string candidate = Encoding.ASCII.GetString(input);
        if (candidate.Contains("-----BEGIN"))
        {
            var pem = PemEncoding.Find(candidate); if (candidate[pem.Label] != "CERTIFICATE" || candidate[..pem.Location.Start.Value].Trim().Length != 0 || candidate[pem.Location.End.Value..].Trim().Length != 0) throw new ArgumentException("Provide exactly one CERTIFICATE PEM block.");
            der = Convert.FromBase64String(candidate[pem.Base64Data]);
        }
        var reader = new AsnReader(der, AsnEncodingRules.DER); var certificate = reader.ReadSequence(); reader.ThrowIfNotEmpty(); var tbs = certificate.ReadSequence();
        if (tbs.PeekTag().TagClass == TagClass.ContextSpecific && tbs.PeekTag().TagValue == 0) tbs.ReadEncodedValue();
        for (int i = 0; i < 5; i++) tbs.ReadEncodedValue();
        byte[] spki = tbs.ReadEncodedValue().ToArray(); var key = new AsnReader(spki, AsnEncodingRules.DER).ReadSequence(); var algorithm = key.ReadSequence(); string oid = algorithm.ReadObjectIdentifier();
        string? curve = algorithm.HasData && algorithm.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier) ? algorithm.ReadObjectIdentifier() : null;
        byte[] bits = key.ReadBitString(out int unused); if (unused != 0) throw new ArgumentException("Invalid public key bit string."); key.ThrowIfNotEmpty();
        using var parsed = X509CertificateLoader.LoadCertificate(der);
        return new(der, spki, bits, oid, curve, parsed.Subject, parsed.Issuer, parsed.SerialNumber, parsed.NotBefore.ToUniversalTime(), parsed.NotAfter.ToUniversalTime(), parsed.SignatureAlgorithm.Value ?? "", string.Join("\n", parsed.Extensions.Select(e => (e.Oid?.Value ?? "") + (e.Critical ? " [critical]" : "") + ": " + e.Format(false))));
    }
}
internal sealed record CertificateInfo(byte[] Der, byte[] Spki, byte[] PublicKey, string Oid, string? Curve, string Subject, string Issuer, string Serial, DateTime Start, DateTime End, string Signature, string Extensions)
{
    public bool IsSm2 => Oid == "1.2.840.10045.2.1" && Curve == "1.2.156.10197.1.301" && PublicKey.Length == 65 && PublicKey[0] == 4;
}
