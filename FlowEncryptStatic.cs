using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace WhatsAppFlowApi;

public static class FlowEncryptStatic
{

public static byte[] FlipIv(byte[] iv)
{
    // "invert" each byte: b ^ 0xFF (used by multiple implementations)
    byte[] flipped = (byte[])iv.Clone();
    for (int i = 0; i < flipped.Length; i++) flipped[i] ^= 0xFF;
    return flipped;
}

public static RSA LoadRsaFromPem(string privateKeyPem)
{
    var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    return rsa;
}

public static string DecryptFlowRequest(FlowEncryptedRequest req, RSA rsa, out byte[] aesKey, out byte[] iv)
{
    iv = Convert.FromBase64String(req.initial_vector);

    var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
    aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

    var enc = Convert.FromBase64String(req.encrypted_flow_data);

    // last 16 bytes = GCM tag
    byte[] tag = enc[^16..];
    byte[] cipherText = enc[..^16];

    // Use BouncyCastle GCM which supports 16-byte nonces (WhatsApp standard)
    var cipher = new GcmBlockCipher(new AesEngine());
    var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
    cipher.Init(false, param);
    
    byte[] plain = new byte[cipherText.Length];
    int len = cipher.ProcessBytes(cipherText, 0, cipherText.Length, plain, 0);
    cipher.DoFinal(plain, len);

    return Encoding.UTF8.GetString(plain);
}

public static string EncryptFlowResponse(object responseObj, byte[] aesKey, byte[] requestIv)
{
    var json = JsonSerializer.Serialize(responseObj);
    byte[] plain = Encoding.UTF8.GetBytes(json);

    byte[] iv = FlipIv(requestIv); // encrypt response with inverted IV

    // Use BouncyCastle GCM which supports 16-byte nonces (WhatsApp standard)
    var cipher = new GcmBlockCipher(new AesEngine());
    var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
    cipher.Init(true, param);
    
    byte[] cipherText = new byte[cipher.GetOutputSize(plain.Length)];
    int len = cipher.ProcessBytes(plain, 0, plain.Length, cipherText, 0);
    cipher.DoFinal(cipherText, len);

    // WhatsApp expects base64(ciphertext || tag)
    return Convert.ToBase64String(cipherText);
}

}
