

using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Json;
using WhatsAppFlowApi;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options (needed for Flow JSON size)
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// Add controllers (required for any API controllers)
builder.Services.AddControllers();

var app = builder.Build();

// Bind to the port from environment variable (Render requires this)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://*:{port}");

// Map controllers (for API controllers)
app.MapControllers();

// Simple health check endpoint
app.MapGet("/healthz", () => Results.Ok("Healthy"));

// Root endpoint (optional)
app.MapGet("/", () => Results.Ok(new { status = "ok" }));

// Safe debug endpoint to check whether the key env var is present.
// This DOES NOT return the key material.
app.MapGet("/debug/env", () =>
{
    var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
    var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
    return Results.Ok(new { keyPresent = hasPem || hasB64, hasPem, hasB64 });
});


app.MapPost("/flows/endpoint", async (FlowEncryptedRequest req) =>
{
    try
    {
        // Load private key from environment variable. If the raw PEM is not set,
        // support a base64-encoded PEM in `PRIVATE_KEY_PEM_B64` (useful for env UIs)
        var privateKeyPem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
        if (string.IsNullOrEmpty(privateKeyPem))
        {
            var privateKeyPemB64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
            if (!string.IsNullOrEmpty(privateKeyPemB64))
            {
                try
                {
                    privateKeyPem = Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyPemB64));
                }
                catch
                {
                    return Results.BadRequest(new { error = "PRIVATE_KEY_PEM_B64 invalid base64" });
                }
            }
        }

        if (string.IsNullOrEmpty(privateKeyPem))
        {
            return Results.BadRequest(new { error = "PRIVATE_KEY_PEM environment variable not set" });
        }

        var rsa = FlowEncryptStatic.LoadRsaFromPem(privateKeyPem);

        // 1) decrypt
        var decryptedJson = FlowEncryptStatic.DecryptFlowRequest(req, rsa, out var aesKey, out var iv);

    using var doc = JsonDocument.Parse(decryptedJson);
    var action = doc.RootElement.GetProperty("action").GetString();

    // 2) handle ping
    if (action == "ping") // action can be init/back/data_exchange/ping :contentReference[oaicite:5]{index=5}
    {
        var responseObj = new
        {
            version = "3.0",
            data = new { status = "active" }
        };

        var encryptedResponse = FlowEncryptStatic.EncryptFlowResponse(responseObj, aesKey, iv);
        return Results.Text(encryptedResponse, "application/json");
    }

    // For now, just return “active” on other actions too (to pass health check quickly)
        var fallback = FlowEncryptStatic.EncryptFlowResponse(new { version = "3.0", data = new { status = "active" } }, aesKey, iv);
        return Results.Text(fallback, "application/json");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error in /flows/endpoint: {ex}");
        return Results.StatusCode(500);
    }
});


// app.MapPost("/flow", async (HttpRequest request) =>
// {
//     using var reader = new StreamReader(request.Body);
//     var body = await reader.ReadToEndAsync();

//     var json = System.Text.Json.JsonDocument.Parse(body);

//     // 1️⃣ Handle verification challenge
//     if (json.RootElement.TryGetProperty("challenge", out var challenge))
//     {
//         return Results.Ok(new
//         {
//             challenge = challenge.GetString()
//         });
//     }

//     // 2️⃣ Handle real flow submission later
//     Console.WriteLine("FLOW DATA:");
//     Console.WriteLine(body);

//     return Results.Ok(new
//     {
//         status = "success"
//     });
// });



// Flow POST endpoint
// app.MapPost("/flow", async (HttpRequest request) =>
// {
//     using var reader = new StreamReader(request.Body);
//     var body = await reader.ReadToEndAsync();

//     Console.WriteLine("FLOW DATA RECEIVED:");
//     Console.WriteLine(body);

//     // Required response
//     return Results.Ok(new
//     {
//         status = "success"
//     });
// });


app.Run();





// var builder = WebApplication.CreateBuilder(args);

// // Add services to the container.
// // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// builder.Services.AddOpenApi();

// var app = builder.Build();

// // Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
//     app.MapOpenApi();
// }

// app.UseHttpsRedirection();

// var summaries = new[]
// {
//     "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
// };

// app.MapGet("/weatherforecast", () =>
// {
//     var forecast =  Enumerable.Range(1, 5).Select(index =>
//         new WeatherForecast
//         (
//             DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//             Random.Shared.Next(-20, 55),
//             summaries[Random.Shared.Next(summaries.Length)]
//         ))
//         .ToArray();
//     return forecast;
// })
// .WithName("GetWeatherForecast");

// app.Run();

// record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
// {
//     public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
// }
