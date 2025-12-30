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
    iv = FlipIv(iv);

    var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
    aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

    var enc = Convert.FromBase64String(req.encrypted_flow_data);

    // BouncyCastle GCM expects full encrypted data (ciphertext || tag) and validates tag internally
    var cipher = new GcmBlockCipher(new AesEngine());
    var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
    cipher.Init(false, param);
    
    byte[] plain = new byte[cipher.GetOutputSize(enc.Length)];
    int len = cipher.ProcessBytes(enc, 0, enc.Length, plain, 0);
    int finalLen = cipher.DoFinal(plain, len);
    
    // Combine lengths: ProcessBytes wrote 'len' bytes, DoFinal wrote 'finalLen' more bytes
    return Encoding.UTF8.GetString(plain, 0, len + finalLen);
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
