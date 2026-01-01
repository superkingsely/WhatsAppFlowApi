



using System;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Json;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppFlowApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<JsonOptions>(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
            });

            builder.Services.AddHttpClient();
            var app = builder.Build();

            // ==========================
            // 🔹 BASIC HEALTH &  DEBUG
            // ==========================
            app.MapGet("/", () => Results.Ok(new { status = "ok", service = "whatsapp-flow-api" }));
            app.MapGet("/healthz", () => Results.Ok("healthy"));

            app.MapGet("/debug/env", () =>
            {
                var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
                var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
                return Results.Ok(new { hasPem, hasB64 });
            });

            // ==========================
            // 🔹 UPLOAD PUBLIC KEY
            // ==========================
            app.MapPost("/upload_public_key", async (IHttpClientFactory httpClientFactory) =>
            {
                Console.WriteLine("🚀 /upload_public_key HIT");

                var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
                var phoneId = Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID");

                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneId))
                    return Results.BadRequest("Missing WHATSAPP_ACCESS_TOKEN or PHONE_NUMBER_ID");

                var privateKeyPem = GetPrivateKey();
                if (string.IsNullOrEmpty(privateKeyPem)) return Results.StatusCode(500);

                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

                var payload = new { business_public_key = publicKeyPem };

                var url = $"https://graph.facebook.com/v24.0/{phoneId}/whatsapp_business_encryption";
                var client = httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) return Results.BadRequest(body);
                return Results.Ok(new { success = true, response = body });
            });

            // ==========================
            // 🔹 FLOW ENCRYPTED ENDPOINT
            // ==========================
            app.MapPost("/flows/endpoint", (FlowEncryptedRequest req) =>
            {
                Console.WriteLine("🚀 FLOW HEALTH CHECK HIT");

                try
                {
                    var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM")
                        ?? throw new Exception("PRIVATE_KEY_PEM missing");

                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(privatePem);

                    var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);
                    Console.WriteLine("🔓 Decrypted payload:");
                    Console.WriteLine(decryptedJson);

                    var response = new
                    {
                        version = "3.0",
                        data = new
                        {
                           status = "active"
                        }
                    };

                    var encrypted = EncryptFlowResponse(response, aesKey, iv);
                    Console.WriteLine("✅ FLOW HEALTH OK");

                    return Results.Text(encrypted, "application/json");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("🔥 FLOW HEALTH ERROR");
                    Console.Error.WriteLine(ex.ToString());
                    return Results.StatusCode(500);
                }
            });

            app.Run();
        }

        // ==========================
        // 🔹 TYPES / DTO
        // ==========================
        public sealed record FlowEncryptedRequest(
            string encrypted_flow_data,
            string encrypted_aes_key,
            string initial_vector
        );

        // ==========================
        // 🔹 HELPERS
        // ==========================
        private static string GetPrivateKey()
        {
            var pem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
            if (!string.IsNullOrEmpty(pem)) return pem;

            var b64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
            if (!string.IsNullOrEmpty(b64))
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));

            return string.Empty;
        }

        private static byte[] FlipIv(byte[] iv)
        {
            byte[] flipped = (byte[])iv.Clone();
            for (int i = 0; i < flipped.Length; i++) flipped[i] ^= 0xFF;
            return flipped;
        }

        // Decrypt WhatsApp flow request correctly


private static string DecryptFlowRequest(
    FlowEncryptedRequest req,
    RSA rsa,
    out byte[] aesKey,
    out byte[] requestIv)
{
    requestIv = Convert.FromBase64String(req.initial_vector); // ✅ keep as-is

    var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
    aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

    var encData = Convert.FromBase64String(req.encrypted_flow_data); // usually ciphertext||tag

    var cipher = new GcmBlockCipher(new AesEngine());
    var param = new AeadParameters(new KeyParameter(aesKey), 128, requestIv);
    cipher.Init(false, param);

    byte[] plain = new byte[cipher.GetOutputSize(encData.Length)];
    int len = cipher.ProcessBytes(encData, 0, encData.Length, plain, 0);
    len += cipher.DoFinal(plain, len);

    return Encoding.UTF8.GetString(plain, 0, len);
}






        // private static string DecryptFlowRequest(FlowEncryptedRequest req, RSA rsa, out byte[] aesKey, out byte[] iv)
        // {
        //     iv = Convert.FromBase64String(req.initial_vector);
        //     iv = FlipIv(iv);

        //     var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
        //     aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

        //     var encData = Convert.FromBase64String(req.encrypted_flow_data);

        //     var cipher = new GcmBlockCipher(new AesEngine());
        //     var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
        //     cipher.Init(false, param);

        //     byte[] plain = new byte[cipher.GetOutputSize(encData.Length)];
        //     int len = cipher.ProcessBytes(encData, 0, encData.Length, plain, 0);
        //     int finalLen = cipher.DoFinal(plain, len);

        //     return Encoding.UTF8.GetString(plain, 0, len + finalLen);
        // }

        // Encrypt WhatsApp flow response correctly
        private static string EncryptFlowResponse(object responseObj, byte[] aesKey, byte[] requestIv)
        {
            var json = JsonSerializer.Serialize(responseObj);
            var plain = Encoding.UTF8.GetBytes(json);

            var iv = FlipIv(requestIv);

            var cipher = new GcmBlockCipher(new AesEngine());
            var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
            cipher.Init(true, param);

            byte[] cipherText = new byte[cipher.GetOutputSize(plain.Length)];
            int len = cipher.ProcessBytes(plain, 0, plain.Length, cipherText, 0);
            cipher.DoFinal(cipherText, len);

            return Convert.ToBase64String(cipherText);
        }
    }
}




















// using System;
// using System.Security.Cryptography;
// using System.Text;
// using System.Text.Json;
// using System.Net.Http.Json;
// using Microsoft.AspNetCore.Http.Json;
// using Org.BouncyCastle.Crypto.Modes;
// using Org.BouncyCastle.Crypto.Engines;
// using Org.BouncyCastle.Crypto.Parameters;

// var builder = WebApplication.CreateBuilder(args);

// // Keep JSON casing as-is (important for WhatsApp Flow)
// builder.Services.Configure<JsonOptions>(options =>
// {
//     options.SerializerOptions.PropertyNamingPolicy = null;
// });

// builder.Services.AddHttpClient();

// var app = builder.Build();

// // =======================================================
// // 🔹 BASIC HEALTH & DEBUG ENDPOINTS
// // =======================================================
// app.MapGet("/", () => Results.Ok(new { status = "ok", service = "whatsapp-flow-api" }));
// app.MapGet("/healthz", () => Results.Ok("healthy"));
// app.MapGet("/debug/env", () =>
// {
//     var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
//     var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
//     return Results.Ok(new { hasPem, hasB64 });
// });

// // =======================================================
// // 🔹 UPLOAD PUBLIC KEY
// // =======================================================
// app.MapPost("/upload_public_key", async (IHttpClientFactory httpClientFactory) =>
// {
//     return await WhatsAppFlowHelpers.UploadPublicKey(httpClientFactory);
// });

// // =======================================================
// // 🔹 FLOW ENCRYPTED ENDPOINT
// // =======================================================
// app.MapPost("/flows/endpoint", (WhatsAppFlowHelpers.FlowEncryptedRequest req) =>
// {
//     return WhatsAppFlowHelpers.HandleFlow(req);
// });

// app.Run();

// // =======================================================
// // 🔹 STATIC CLASS FOR METHODS & RECORDS
// // =======================================================
// public static class WhatsAppFlowHelpers
// {
//     // DTO for encrypted flow request
//     public sealed record FlowEncryptedRequest(string encrypted_flow_data, string encrypted_aes_key, string initial_vector);

//     // Main flow handler
//     public static IResult HandleFlow(FlowEncryptedRequest req)
//     {
//         Console.WriteLine("🚀 FLOW HEALTH CHECK HIT");
//         try
//         {
//             var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM")
//                 ?? throw new Exception("PRIVATE_KEY_PEM missing");

//             using var rsa = RSA.Create();
//             rsa.ImportFromPem(privatePem);

//             var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);

//             Console.WriteLine("🔓 Decrypted payload:");
//             Console.WriteLine(decryptedJson);

//             var response = new
//             {
//                 version = "3.0",
//                 response = new
//                 {
//                     screen = "INIT",
//                     data = new { status = "active" }
//                 }
//             };

//             var encrypted = EncryptFlowResponse(response, aesKey, iv);

//             Console.WriteLine("✅ FLOW HEALTH OK");
//             return Results.Text(encrypted, "application/json");
//         }
//         catch (Exception ex)
//         {
//             Console.Error.WriteLine("🔥 FLOW HEALTH ERROR");
//             Console.Error.WriteLine(ex.ToString());
//             return Results.StatusCode(500);
//         }
//     }

//     // Upload public key endpoint
//     public static async Task<IResult> UploadPublicKey(IHttpClientFactory httpClientFactory)
//     {
//         Console.WriteLine("🚀 /upload_public_key HIT");

//         var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
//         var phoneId = Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID");

//         if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneId))
//         {
//             Console.Error.WriteLine("❌ Missing token or phone ID");
//             return Results.BadRequest("Missing WHATSAPP_ACCESS_TOKEN or PHONE_NUMBER_ID");
//         }

//         var privateKeyPem = GetPrivateKey();
//         if (string.IsNullOrEmpty(privateKeyPem))
//         {
//             Console.Error.WriteLine("❌ PRIVATE KEY NOT FOUND");
//             return Results.StatusCode(500);
//         }

//         using var rsa = RSA.Create();
//         rsa.ImportFromPem(privateKeyPem);
//         var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

//         Console.WriteLine($"🔑 Public key length: {publicKeyPem.Length}");
//         Console.WriteLine("🔑 Public key preview:");
//         Console.WriteLine(publicKeyPem[..Math.Min(120, publicKeyPem.Length)] + "...");

//         var payload = new { business_public_key = publicKeyPem };
//         Console.WriteLine("📤 Payload being sent:");
//         Console.WriteLine(JsonSerializer.Serialize(payload));

//         var url = $"https://graph.facebook.com/v24.0/{phoneId}/whatsapp_business_encryption";
//         var client = httpClientFactory.CreateClient();
//         var request = new HttpRequestMessage(HttpMethod.Post, url)
//         {
//             Content = JsonContent.Create(payload)
//         };
//         request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

//         var response = await client.SendAsync(request);
//         var body = await response.Content.ReadAsStringAsync();

//         Console.WriteLine($"📥 Meta Status: {(int)response.StatusCode}");
//         Console.WriteLine("📥 Meta Response:");
//         Console.WriteLine(body);

//         if (!response.IsSuccessStatusCode) return Results.BadRequest(body);

//         return Results.Ok(new { success = true, response = body });
//     }

//     // =======================================================
//     // 🔹 HELPER METHODS
//     // =======================================================
//     public static string GetPrivateKey()
//     {
//         var pem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
//         if (!string.IsNullOrEmpty(pem)) return pem;

//         var b64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
//         if (!string.IsNullOrEmpty(b64))
//             return Encoding.UTF8.GetString(Convert.FromBase64String(b64));

//         return string.Empty;
//     }

//     public static byte[] FlipIv(byte[] iv)
//     {
//         byte[] flipped = (byte[])iv.Clone();
//         for (int i = 0; i < flipped.Length; i++) flipped[i] ^= 0xFF;
//         return flipped;
//     }

//     public static string DecryptFlowRequest(FlowEncryptedRequest req, RSA rsa, out byte[] aesKey, out byte[] iv)
//     {
//         iv = Convert.FromBase64String(req.initial_vector);
//         iv = FlipIv(iv);

//         var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
//         aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

//         var enc = Convert.FromBase64String(req.encrypted_flow_data);

//         var cipher = new GcmBlockCipher(new AesEngine());
//         var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
//         cipher.Init(false, param);

//         byte[] plain = new byte[cipher.GetOutputSize(enc.Length)];
//         int len = cipher.ProcessBytes(enc, 0, enc.Length, plain, 0);
//         int finalLen = cipher.DoFinal(plain, len);

//         return Encoding.UTF8.GetString(plain, 0, len + finalLen);
//     }

//     public static string EncryptFlowResponse(object responseObj, byte[] aesKey, byte[] requestIv)
//     {
//         var json = JsonSerializer.Serialize(responseObj);
//         byte[] plain = Encoding.UTF8.GetBytes(json);

//         byte[] iv = FlipIv(requestIv);

//         var cipher = new GcmBlockCipher(new AesEngine());
//         var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
//         cipher.Init(true, param);

//         byte[] cipherText = new byte[cipher.GetOutputSize(plain.Length)];
//         int len = cipher.ProcessBytes(plain, 0, plain.Length, cipherText, 0);
//         cipher.DoFinal(cipherText, len);

//         return Convert.ToBase64String(cipherText);
//     }
// }
