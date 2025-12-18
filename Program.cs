

using Microsoft.AspNetCore.Http.Json;

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


// Load private key once (store in env/secret manager in real deployments)
var privateKeyPem = File.ReadAllText("private_key.pem");
var rsa = LoadRsaFromPem(privateKeyPem);

app.MapPost("/flows/endpoint", async (FlowEncryptedRequest req) =>
{
    // 1) decrypt
    var decryptedJson = DecryptFlowRequest(req, rsa, out var aesKey, out var iv);

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

        var encryptedResponse = EncryptFlowResponse(responseObj, aesKey, iv);
        return Results.Text(encryptedResponse, "application/json");
    }

    // For now, just return “active” on other actions too (to pass health check quickly)
    var fallback = EncryptFlowResponse(new { version = "3.0", data = new { status = "active" } }, aesKey, iv);
    return Results.Text(fallback, "application/json");
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
