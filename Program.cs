

using System;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

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
            // 🔹 BASIC HEALTH
            // ==========================
            app.MapGet("/", () => Results.Ok(new { status = "ok" }));
            app.MapGet("/healthz", () => Results.Ok("healthy"));

            // ==========================
            //🔹 FLOW ENDPOINT
            //==========================



            app.MapPost("/flows/endpoint", async (
    FlowEncryptedRequest req,
    IHttpClientFactory httpClientFactory
) =>
{
    Console.WriteLine("🚀 FLOW HIT");

    try
    {
        var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM")
            ?? throw new Exception("PRIVATE_KEY_PEM missing");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePem);

        // 🔓 Decrypt request
        var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);

        Console.WriteLine("🔓 Decrypted Payload:");
        Console.WriteLine(decryptedJson);

        using var doc = JsonDocument.Parse(decryptedJson);
        var root = doc.RootElement;

        var action = root.GetProperty("action").GetString();
        var version = root.GetProperty("version").GetString();

        Console.WriteLine($"➡ ACTION: {action}");

        object response;

        // ==================================================
        // ✅ HEALTH CHECK (PING)
        // ==================================================
        if (action == "ping")
        {
            response = new
            {
                version = "3.0",
                screen = "screen_asnlyt",
                data = new
                {
                    status = "active"
                }
            };
        }

        // ==================================================
        // ✅ INIT (FIRST SCREEN)
        // ==================================================
        else if (action == "INIT")
        {
            var client = httpClientFactory.CreateClient();
            var apiResponse = await client.GetAsync($"https://api.food-ease.io/api/v1/OrderCharge/whatsapp-list-charges?restaurantId=d91e02ba-50f3-4cd6-8607-8ffbbeeda2da&revCenterId=f97e82e5-c922-4f9c-bc9a-477641d72d11&serviceType=Delivery");

            if (!apiResponse.IsSuccessStatusCode)
                throw new Exception("Failed to fetch delivery areas");

            var rawAreas = await apiResponse.Content.ReadFromJsonAsync<List<ExternalArea>>();

            var deliveryAreas = rawAreas!.ConvertAll(a => new
            {
                id = a.id,
                title = a.title
            });

            response = new
            {
                version = "3.0",
                screen = "screen_asnlyt",
                data = new
                {
                    delivery_areas = deliveryAreas,
                    status = "active"
                }
            };
        }

        // ==================================================
        // ✅ NAVIGATE (MIRROR PAYLOAD → NEXT SCREEN)
        // ==================================================
        else if (action == "navigate")
        {
            var screen = root.GetProperty("screen").GetString();
            var data = root.GetProperty("data");

            response = new
            {
                version = "3.0",
                screen = screen,
                data = JsonSerializer.Deserialize<object>(data.GetRawText())
            };
        }

        // ==================================================
        // ✅ DATA EXCHANGE / COMPLETE
        // ==================================================
       else if (action == "data_exchange" || action == "complete")
{
    var data = root.GetProperty("data");

    response = new
    {
        version = "3.0",
        screen = "SUCCESS",
        data = new
        {
            extension_message_response = new
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    data.GetRawText()
                ) ?? new Dictionary<string, object>()
            }
        }
    };
}


        // ==================================================
        // ❌ UNKNOWN ACTION
        // ==================================================
        else
        {
            throw new Exception($"Unknown action: {action}");
        }

        // 🔍 LOG RESPONSE (PLAIN JSON)
        var flowJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.WriteLine("📦 FLOW RESPONSE (before encryption):");
        Console.WriteLine(flowJson);

        // 🔐 Encrypt response
        var encrypted = EncryptFlowResponse(response, aesKey, iv);

        Console.WriteLine("✅ FLOW RESPONSE OK");
        return Results.Text(encrypted, "application/json");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("🔥 FLOW ERROR");
        Console.Error.WriteLine(ex);
        return Results.StatusCode(500);
    }
});


                








            //      app.MapPost("/flows/endpoint", async (
            //     FlowEncryptedRequest req,
            //     IHttpClientFactory httpClientFactory
            // ) =>
            // {
            //     Console.WriteLine("🚀 FLOW HIT");

            //     try
            //     {
            //         var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM")
            //             ?? throw new Exception("PRIVATE_KEY_PEM missing");

            //         using var rsa = RSA.Create();
            //         rsa.ImportFromPem(privatePem);

            //         // 🔓 Decrypt request
            //         var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);
            //         Console.WriteLine("🔓 Decrypted Payload:");
            //         // Console.WriteLine(decryptedJson);

            //         // ==========================
            //         // 🔹 FETCH EXTERNAL API DATA
            //         // ==========================
            //         var client = httpClientFactory.CreateClient();
            //         var apiResponse = await client.GetAsync("https://cjendpoint.onrender.com/api/areas");

            //         if (!apiResponse.IsSuccessStatusCode)
            //             throw new Exception("Failed to fetch delivery areas");

            //         var rawAreas = await apiResponse.Content.ReadFromJsonAsync<List<ExternalArea>>();

            //         // Map to WhatsApp-required format
            //         var deliveryAreas = rawAreas!.ConvertAll(a => new
            //         {
            //             id = a.id,
            //             title = a.title   // 👈 change ONLY if API field name differs
            //         });

            //         Console.WriteLine("🧪 MAPPED DELIVERY AREAS:");
            //         Console.WriteLine(JsonSerializer.Serialize(deliveryAreas, new JsonSerializerOptions
            //         {
            //             WriteIndented = true
            //         }));





            //         // ==========================
            //         // 🔹 FLOW RESPONSE (CORRECT FORMAT)
            //         // ==========================
            //         var response = new
            //         {
            //             version = "3.0",
            //             screen="screen_asnlyt",
            //             data = new
            //             {
            //                 delivery_areas = deliveryAreas,
            //                 status = "active"
            //             }
            //         };

            //         // 🔍 LOG EXACT FLOW JSON (WHAT WHATSAPP SEES)
            //         var flowJson = JsonSerializer.Serialize(
            //             response,
            //             new JsonSerializerOptions { WriteIndented = true }
            //         );

            //         Console.WriteLine("📦 FLOW JSON SENT TO WHATSAPP (before encryption):");
            //         Console.WriteLine(flowJson);




            //         // 🔐 Encrypt response
            //         var encrypted = EncryptFlowResponse(response, aesKey, iv);

            //         Console.WriteLine("✅ FLOW RESPONSE OK");
            //         return Results.Text(encrypted, "application/json");
            //     }
            //     catch (Exception ex)
            //     {
            //         Console.Error.WriteLine("🔥 FLOW ERROR");
            //         Console.Error.WriteLine(ex);
            //         return Results.StatusCode(500);
            //     }
            // });

            app.Run();
        }



        // ==========================
        // 🔹 DTOs
        // ==========================
        public sealed record FlowEncryptedRequest(
            string encrypted_flow_data,
            string encrypted_aes_key,
            string initial_vector
        );

       public sealed class ExternalArea
        {
            public string id { get; set; } = default!;
            public string title { get; set; } = default!;
        }

        // ==========================
        // 🔹 CRYPTO HELPERS
        // ==========================
        private static byte[] FlipIv(byte[] iv)
        {
            var flipped = (byte[])iv.Clone();
            for (int i = 0; i < flipped.Length; i++)
                flipped[i] ^= 0xFF;
            return flipped;
        }

        private static string DecryptFlowRequest(
            FlowEncryptedRequest req,
            RSA rsa,
            out byte[] aesKey,
            out byte[] requestIv)
        {
            requestIv = Convert.FromBase64String(req.initial_vector);

            var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
            aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

            var encData = Convert.FromBase64String(req.encrypted_flow_data);

            var cipher = new GcmBlockCipher(new AesEngine());
            var param = new AeadParameters(new KeyParameter(aesKey), 128, requestIv);
            cipher.Init(false, param);

            byte[] plain = new byte[cipher.GetOutputSize(encData.Length)];
            int len = cipher.ProcessBytes(encData, 0, encData.Length, plain, 0);
            len += cipher.DoFinal(plain, len);

            return Encoding.UTF8.GetString(plain, 0, len);
        }

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
// using System.Text;
// using System.Text.Json;
// using System.Net.Http.Json;
// using System.Security.Cryptography;
// using Microsoft.AspNetCore.Http.Json;
// using Org.BouncyCastle.Crypto.Modes;
// using Org.BouncyCastle.Crypto.Engines;
// using Org.BouncyCastle.Crypto.Parameters;
// using Microsoft.AspNetCore.Builder;
// using Microsoft.Extensions.DependencyInjection;

// namespace WhatsAppFlowApi
// {
//     public class Program
//     {
//         public static void Main(string[] args)
//         {
//             var builder = WebApplication.CreateBuilder(args);

//             builder.Services.Configure<JsonOptions>(options =>
//             {
//                 options.SerializerOptions.PropertyNamingPolicy = null;
//             });

//             builder.Services.AddHttpClient();
//             var app = builder.Build();

//             // ==========================
//             // 🔹 BASIC HEALTH &  DEBUG
//             // ==========================
//             app.MapGet("/", () => Results.Ok(new { status = "ok", service = "whatsapp-flow-api" }));
//             app.MapGet("/healthz", () => Results.Ok("healthy"));

//             app.MapGet("/debug/env", () =>
//             {
//                 var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
//                 var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
//                 return Results.Ok(new { hasPem, hasB64 });
//             });

//             // ==========================
//             // 🔹 UPLOAD PUBLIC KEY
//             // ==========================
//             app.MapPost("/upload_public_key", async (IHttpClientFactory httpClientFactory) =>
//             {
//                 Console.WriteLine("🚀 /upload_public_key HIT");

//                 var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
//                 var phoneId = Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID");

//                 if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneId))
//                     return Results.BadRequest("Missing WHATSAPP_ACCESS_TOKEN or PHONE_NUMBER_ID");

//                 var privateKeyPem = GetPrivateKey();
//                 if (string.IsNullOrEmpty(privateKeyPem)) return Results.StatusCode(500);

//                 using var rsa = RSA.Create();
//                 rsa.ImportFromPem(privateKeyPem);
//                 var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

//                 var payload = new { business_public_key = publicKeyPem };

//                 var url = $"https://graph.facebook.com/v24.0/{phoneId}/whatsapp_business_encryption";
//                 var client = httpClientFactory.CreateClient();
//                 var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
//                 {
//                     Content = JsonContent.Create(payload)
//                 };
//                 request.Headers.Authorization =
//                     new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

//                 var response = await client.SendAsync(request);
//                 var body = await response.Content.ReadAsStringAsync();

//                 if (!response.IsSuccessStatusCode) return Results.BadRequest(body);
//                 return Results.Ok(new { success = true, response = body });
//             });

//             // ==========================
//             // 🔹 FLOW ENCRYPTED ENDPOINT
//             // ==========================
//             app.MapPost("/flows/endpoint", (FlowEncryptedRequest req) =>
//             {
//                 Console.WriteLine("🚀 FLOW HEALTH CHECK HIT");

//                 try
//                 {
//                     var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM")
//                         ?? throw new Exception("PRIVATE_KEY_PEM missing");

//                     using var rsa = RSA.Create();
//                     rsa.ImportFromPem(privatePem);

//                     var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);
//                     Console.WriteLine("🔓 Decrypted payload:");
//                     Console.WriteLine(decryptedJson);

//                     var response = new
//                     {
//                         version = "3.0",
//                         data = new
//                         {
//                            status = "active"
//                         }
//                     };

//                     var encrypted = EncryptFlowResponse(response, aesKey, iv);
//                     Console.WriteLine("✅ FLOW HEALTH OK");

//                     return Results.Text(encrypted, "application/json");
//                 }
//                 catch (Exception ex)
//                 {
//                     Console.Error.WriteLine("🔥 FLOW HEALTH ERROR");
//                     Console.Error.WriteLine(ex.ToString());
//                     return Results.StatusCode(500);
//                 }
//             });

//             app.Run();
//         }

//         // ==========================
//         // 🔹 TYPES / DTO
//         // ==========================
//         public sealed record FlowEncryptedRequest(
//             string encrypted_flow_data,
//             string encrypted_aes_key,
//             string initial_vector
//         );

//         // ==========================
//         // 🔹 HELPERS
//         // ==========================
//         private static string GetPrivateKey()
//         {
//             var pem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
//             if (!string.IsNullOrEmpty(pem)) return pem;

//             var b64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
//             if (!string.IsNullOrEmpty(b64))
//                 return Encoding.UTF8.GetString(Convert.FromBase64String(b64));

//             return string.Empty;
//         }

//         private static byte[] FlipIv(byte[] iv)
//         {
//             byte[] flipped = (byte[])iv.Clone();
//             for (int i = 0; i < flipped.Length; i++) flipped[i] ^= 0xFF;
//             return flipped;
//         }

//         // Decrypt WhatsApp flow request correctly


// private static string DecryptFlowRequest(
//     FlowEncryptedRequest req,
//     RSA rsa,
//     out byte[] aesKey,
//     out byte[] requestIv)
// {
//     requestIv = Convert.FromBase64String(req.initial_vector); // ✅ keep as-is

//     var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
//     aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

//     var encData = Convert.FromBase64String(req.encrypted_flow_data); // usually ciphertext||tag

//     var cipher = new GcmBlockCipher(new AesEngine());
//     var param = new AeadParameters(new KeyParameter(aesKey), 128, requestIv);
//     cipher.Init(false, param);

//     byte[] plain = new byte[cipher.GetOutputSize(encData.Length)];
//     int len = cipher.ProcessBytes(encData, 0, encData.Length, plain, 0);
//     len += cipher.DoFinal(plain, len);

//     return Encoding.UTF8.GetString(plain, 0, len);
// }






//         // private static string DecryptFlowRequest(FlowEncryptedRequest req, RSA rsa, out byte[] aesKey, out byte[] iv)
//         // {
//         //     iv = Convert.FromBase64String(req.initial_vector);
//         //     iv = FlipIv(iv);

//         //     var encAesKey = Convert.FromBase64String(req.encrypted_aes_key);
//         //     aesKey = rsa.Decrypt(encAesKey, RSAEncryptionPadding.OaepSHA256);

//         //     var encData = Convert.FromBase64String(req.encrypted_flow_data);

//         //     var cipher = new GcmBlockCipher(new AesEngine());
//         //     var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
//         //     cipher.Init(false, param);

//         //     byte[] plain = new byte[cipher.GetOutputSize(encData.Length)];
//         //     int len = cipher.ProcessBytes(encData, 0, encData.Length, plain, 0);
//         //     int finalLen = cipher.DoFinal(plain, len);

//         //     return Encoding.UTF8.GetString(plain, 0, len + finalLen);
//         // }

//         // Encrypt WhatsApp flow response correctly
//         private static string EncryptFlowResponse(object responseObj, byte[] aesKey, byte[] requestIv)
//         {
//             var json = JsonSerializer.Serialize(responseObj);
//             var plain = Encoding.UTF8.GetBytes(json);

//             var iv = FlipIv(requestIv);

//             var cipher = new GcmBlockCipher(new AesEngine());
//             var param = new AeadParameters(new KeyParameter(aesKey), 128, iv);
//             cipher.Init(true, param);

//             byte[] cipherText = new byte[cipher.GetOutputSize(plain.Length)];
//             int len = cipher.ProcessBytes(plain, 0, plain.Length, cipherText, 0);
//             cipher.DoFinal(cipherText, len);

//             return Convert.ToBase64String(cipherText);
//         }
//     }
// }



