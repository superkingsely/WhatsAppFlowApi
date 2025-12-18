using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WhatsAppFlowApi;

public sealed record FlowEncryptedRequest(
    string encrypted_flow_data,
    string encrypted_aes_key,
    string initial_vector
);


static byte[] FlipIv(byte[] iv)
{
    // “invert” each byte: b ^ 0xFF (used by multiple implementations)
    for (int i = 0; i < iv.Length; i++) iv[i] ^= 0xFF;
    return iv;
}

static RSA LoadRsaFromPem(string privateKeyPem)
{
    var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    return rsa;
}

static string DecryptFlowRequest(FlowEncryptedRequest req, RSA rsa, out byte[] aesKey, out byte[] iv)
{
    iv = Convert.FromBase64String(req.initial_vector);

    var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
    aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

    var enc = Convert.FromBase64String(req.encrypted_flow_data);

    // last 16 bytes = GCM tag
    var tag = enc[^16..];
    var cipherText = enc[..^16];

    var plain = new byte[cipherText.Length];

    using var gcm = new AesGcm(aesKey);
    gcm.Decrypt(iv, cipherText, tag, plain);

    return Encoding.UTF8.GetString(plain);
}

static string EncryptFlowResponse(object responseObj, byte[] aesKey, byte[] requestIv)
{
    var json = JsonSerializer.Serialize(responseObj);
    var plain = Encoding.UTF8.GetBytes(json);

    var iv = FlipIv((byte[])requestIv.Clone()); // encrypt response with inverted IV :contentReference[oaicite:4]{index=4}

    var cipher = new byte[plain.Length];
    var tag = new byte[16];

    using var gcm = new AesGcm(aesKey);
    gcm.Encrypt(iv, plain, cipher, tag);

    // WhatsApp expects base64(cipher || tag)
    var combined = new byte[cipher.Length + tag.Length];
    Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
    Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);

    return Convert.ToBase64String(combined);
}