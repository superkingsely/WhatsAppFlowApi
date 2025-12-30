
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Json;
using WhatsAppFlowApi;

var builder = WebApplication.CreateBuilder(args);

// Keep JSON casing as-is (important for WhatsApp Flow)
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddHttpClient();

var app = builder.Build();


// =======================================================
// 🔹 BASIC HEALTH & DEBUG
// =======================================================

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "whatsapp-flow-api" }));
app.MapGet("/healthz", () => Results.Ok("healthy"));

app.MapGet("/debug/env", () =>
{
    var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
    var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
    return Results.Ok(new { hasPem, hasB64 });
});


// =======================================================
// 🔹 FLOW ENCRYPTED ENDPOINT
// =======================================================

app.MapPost("/flows/endpoint", async (
    FlowEncryptedRequest req,
    IHttpClientFactory httpClientFactory) =>
{
    try
    {
        Console.WriteLine("🚀 /flows/endpoint HIT");

        // -----------------------------
        // Load private key
        // -----------------------------
        var privateKeyPem = GetPrivateKey();
        if (string.IsNullOrEmpty(privateKeyPem))
        {
            Console.Error.WriteLine("❌ PRIVATE_KEY_PEM missing");
            return Results.StatusCode(500);
        }

        var rsa = FlowEncryptStatic.LoadRsaFromPem(privateKeyPem);

        // -----------------------------
        // Decrypt request
        // -----------------------------
        var decryptedJson = FlowEncryptStatic.DecryptFlowRequest(
            req, rsa, out var aesKey, out var iv);

        Console.WriteLine("🔓 DECRYPTED REQUEST:");
        Console.WriteLine(decryptedJson);

        using var doc = JsonDocument.Parse(decryptedJson);
        var root = doc.RootElement;

        var action = root.TryGetProperty("action", out var act)
            ? act.GetString()
            : "unknown";

        Console.WriteLine($"👉 ACTION = {action}");

        // -----------------------------
        // Handle ping
        // -----------------------------
        if (action == "ping")
        {
            var pingResponse = new
            {
                version = "3.0",
                response = new
                {
                    screen = "INIT",
                    data = new { status = "active" }
                }
            };

            Console.WriteLine("📤 RESPONSE (ping):");
            Console.WriteLine(JsonSerializer.Serialize(pingResponse));

            var encrypted = FlowEncryptStatic.EncryptFlowResponse(
                pingResponse, aesKey, iv);

            return Results.Text(encrypted, "application/json");
        }

        // -----------------------------
        // Handle get_areas
        // -----------------------------
        if (action == "get_areas")
        {
            var areas = await FetchAreasAsync(httpClientFactory);

            Console.WriteLine("📦 FETCHED AREAS:");
            Console.WriteLine(JsonSerializer.Serialize(areas));

            var responseObj = new
            {
                version = "3.0",
                response = new
                {
                    screen = "ADDRESS",
                    data = new
                    {
                        delivery_areas = areas.Select(a => new
                        {
                            id = a.Id,
                            title = a.Title
                        })
                    }
                }
            };

            Console.WriteLine("📤 RESPONSE (areas):");
            Console.WriteLine(JsonSerializer.Serialize(responseObj));

            var encrypted = FlowEncryptStatic.EncryptFlowResponse(
                responseObj, aesKey, iv);

            return Results.Text(encrypted, "application/json");
        }

        // -----------------------------
        // Fallback
        // -----------------------------
        Console.WriteLine("⚠️ Unknown action, returning fallback");

        var fallback = new
        {
            version = "3.0",
            response = new
            {
                screen = "INIT",
                data = new { status = "active" }
            }
        };

        var fallbackEncrypted = FlowEncryptStatic.EncryptFlowResponse(
            fallback, aesKey, iv);

        return Results.Text(fallbackEncrypted, "application/json");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("🔥 FLOW ERROR");
        Console.Error.WriteLine(ex.ToString());
        return Results.StatusCode(500);
    }
});

app.Run();


// =======================================================
// 🔹 HELPERS
// =======================================================

static string GetPrivateKey()
{
    var pem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
    if (!string.IsNullOrEmpty(pem)) return pem;

    var b64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
    if (!string.IsNullOrEmpty(b64))
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    return string.Empty;
}

static async Task<List<Area>> FetchAreasAsync(IHttpClientFactory factory)
{
    var api = Environment.GetEnvironmentVariable("AREAS_API_URL");

    if (!string.IsNullOrEmpty(api))
    {
        try
        {
            var client = factory.CreateClient();
            var areas = await client.GetFromJsonAsync<List<Area>>(api);
            if (areas != null && areas.Count > 0) return areas;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ AREAS API FAILED: {ex.Message}");
        }
    }

    Console.WriteLine("⚠️ Using fallback areas");
    return new()
    {
        new Area("lekki", "Lekki Phase 1"),
        new Area("ikeja", "Ikeja GRA")
    };
}

record Area(string Id, string Title);
