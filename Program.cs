
// var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddHttpClient(); // Needed for sending messages

// var app = builder.Build();

// app.MapGet("/", () => Results.Ok(new { status = "ok" }));
// app.MapGet("/healthz", () => Results.Ok("Healthy"));

// // // ✅ Verify webhook with Meta

// app.MapGet("/webhook", (HttpRequest request) =>
// {
//     Console.WriteLine("Received webhook verification request2");

//     var mode = request.Query["hub.mode"].ToString();
//     var token = request.Query["hub.verify_token"].ToString();
//     var challenge = request.Query["hub.challenge"].ToString();












using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Json;
using System.Security.Cryptography;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using WhatsAppFlowApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
	options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddHttpClient();

var app = builder.Build();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
// app.Urls.Add($"http://*:{port}");

app.MapGet("/", () => Results.Ok(new { status = "ok whatsap flow" }));
app.MapGet("/healthz", () => Results.Ok("healthy"));

// Simple debug endpoint to check if PRIVATE_KEY_PEM is available
app.MapGet("/debug/env", () =>
{
	var hasPem = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM"));
	var hasB64 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64"));
	return Results.Ok(new { keyPresent = hasPem || hasB64, hasPem, hasB64 });
});

// Endpoint to get the public key for uploading to WhatsApp
app.MapGet("/public_key", () => Results.Text(GetPublicKey(), "text/plain"));

// Endpoint to upload the public key to WhatsApp (requires WHATSAPP_ACCESS_TOKEN env var)
app.MapPost("/upload_public_key", async (IHttpClientFactory httpClientFactory) =>
{
	var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
	if (string.IsNullOrEmpty(token))
		return Results.BadRequest(new { error = "WHATSAPP_ACCESS_TOKEN environment variable not set" });

	var publicKey = GetPublicKey();
	var phoneId = "834043419801889"; // from learn.txt

	var url = $"https://graph.facebook.com/v22.0/{phoneId}/whatsapp_business_encryption";
	var payload = new { business_public_key = publicKey.Replace("\n", "\\n").Replace("\r", "") };

	try
	{
		var client = httpClientFactory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
		request.Content = JsonContent.Create(payload);

		var response = await client.SendAsync(request);
		var responseBody = await response.Content.ReadAsStringAsync();

		if (response.IsSuccessStatusCode)
			return Results.Ok(new { status = "success", response = responseBody });
		else
			return Results.BadRequest(new { error = "Upload failed", statusCode = (int)response.StatusCode, response = responseBody });
	}
	catch (Exception ex)
	{
		return Results.Json(new { error = ex.Message }, statusCode: 500);
	}
});

//// Returns dummy areas or proxies to an external API if AREAS_API_URL is set
app.MapGet("/areas", async (IHttpClientFactory httpClientFactory) =>
{
	var api = Environment.GetEnvironmentVariable("AREAS_API_URL");
	if (!string.IsNullOrEmpty(api))
	{
		try
		{
			var client = httpClientFactory.CreateClient();
			var list = await client.GetFromJsonAsync<List<Area>>(api);
			return Results.Ok(list ?? GetDefaultAreas());
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed fetching external areas: {ex.Message}");
			return Results.Ok(GetDefaultAreas());
		}
	}

	return Results.Ok(GetDefaultAreas());
});

// Encrypted flow endpoint expected by your Flow implementation.
// Supports a simple `action` value of "get_areas" (or "areas") to return the areas payload.
app.MapPost("/flows/endpoint", async (FlowEncryptedRequest req, IHttpClientFactory httpClientFactory) =>
{
	try
	{
		var privateKeyPem = GetPrivateKey();

		if (string.IsNullOrEmpty(privateKeyPem))
			return Results.BadRequest(new { error = "PRIVATE_KEY_PEM not set" });

		var rsa = FlowEncryptStatic.LoadRsaFromPem(privateKeyPem);

		Console.WriteLine("Request: encrypted_flow_data=" + req.encrypted_flow_data?.Substring(0, Math.Min(50, req.encrypted_flow_data?.Length ?? 0)) + "...");
		Console.WriteLine("Request: encrypted_aes_key=" + req.encrypted_aes_key);
		Console.WriteLine("Request: initial_vector=" + req.initial_vector);

		var decryptedJson = FlowEncryptStatic.DecryptFlowRequest(req, rsa, out var aesKey, out var iv);

		Console.WriteLine("Fetched data: " + decryptedJson);

		using var doc = JsonDocument.Parse(decryptedJson);
		var root = doc.RootElement;
		var action = root.TryGetProperty("action", out var act) ? act.GetString() : null;

		if (!string.IsNullOrEmpty(action) && (action == "get_areas" || action == "areas" || action == "fetch_areas"))
		{
			var areas = await FetchAreasAsync(httpClientFactory);

			var responseObj = new
			{
				version = "3.0",
				response = new
				{
					screen = "AREAS",
					data = new
					{
						delivery_areas = areas.Select(a => new { id = a.Id, title = a.Title, fee = a.Fee })
					}
				}
			};

			Console.WriteLine("Response format: " + JsonSerializer.Serialize(responseObj));

			var encrypted = FlowEncryptStatic.EncryptFlowResponse(responseObj, aesKey, iv);
			return Results.Text(encrypted, "application/json");
		}

		// ping fallback
		if (action == "ping")
		{
			var responseObj = new { version = "3.0", response = new { screen = "INIT", data = new { status = "active" } } };
			Console.WriteLine("Response format: " + JsonSerializer.Serialize(responseObj));
			var encrypted = FlowEncryptStatic.EncryptFlowResponse(responseObj, aesKey, iv);
			return Results.Text(encrypted, "application/json");
		}

		var fallbackObj = new { version = "3.0", response = new { screen = "INIT", data = new { status = "active" } } };
		Console.WriteLine("Response format: " + JsonSerializer.Serialize(fallbackObj));
		var fallback = FlowEncryptStatic.EncryptFlowResponse(fallbackObj, aesKey, iv);
		return Results.Text(fallback, "application/json");
	}
	catch (Exception ex)
	{
		Console.Error.WriteLine(ex);
		return Results.Json(new { error = ex.Message, details = ex.ToString() }, statusCode: 500);
	}
});

static string GetPrivateKey()
{
	var pem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");
	if (!string.IsNullOrEmpty(pem)) return pem;

	var file = "private_key.pem";
	if (File.Exists(file))
	{
		return File.ReadAllText(file);
	}

	// generate
	var rsa = RSA.Create(2048);
	var pemKey = rsa.ExportRSAPrivateKeyPem();
	File.WriteAllText(file, pemKey);

	// also export public key
	var publicPem = rsa.ExportRSAPublicKeyPem();
	File.WriteAllText("public_key.pem", publicPem);

	return pemKey;
}

static string GetPublicKey()
{
	var file = "public_key.pem";
	if (File.Exists(file))
	{
		return File.ReadAllText(file);
	}

	// generate if not exists
	GetPrivateKey();
	return File.ReadAllText(file);
}

app.Run();

// helpers
static List<Area> GetDefaultAreas() => new()
{
	new Area("lekki","Lekki Phase 1",1500),
	new Area("ikeja","Ikeja GRA",1000)
};

static async Task<List<Area>> FetchAreasAsync(IHttpClientFactory httpClientFactory)
{
	var api = Environment.GetEnvironmentVariable("AREAS_API_URL");
	if (!string.IsNullOrEmpty(api))
	{
		try
		{
			var client = httpClientFactory.CreateClient();
			var list = await client.GetFromJsonAsync<List<Area>>(api);
			if (list != null && list.Count > 0) return list;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"FetchAreasAsync failed: {ex.Message}");
		}
	}

	return GetDefaultAreas();
}



















record Area(string Id, string Title, int Fee);
//         var privateKeyPem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM");

//         if (string.IsNullOrEmpty(privateKeyPem))
//         {
//             var privateKeyPemB64 = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM_B64");
//             if (!string.IsNullOrEmpty(privateKeyPemB64))
//             {
//                 privateKeyPem = Encoding.UTF8.GetString(
//                     Convert.FromBase64String(privateKeyPemB64));
//             }
//         }

//         if (string.IsNullOrEmpty(privateKeyPem))
//         {
//             return Results.BadRequest(new { error = "PRIVATE_KEY_PEM not set" });
//         }

//         var rsa = FlowEncryptStatic.LoadRsaFromPem(privateKeyPem);

//         // 1️⃣ Decrypt
//         var decryptedJson = FlowEncryptStatic.DecryptFlowRequest(
//             req, rsa, out var aesKey, out var iv);

//         using var doc = JsonDocument.Parse(decryptedJson);
//         var action = doc.RootElement.GetProperty("action").GetString();

//         // 2️⃣ Ping handler
//         if (action == "ping")
//         {
//             var responseObj = new
//             {
//                 version = "3.0",
//                 data = new { status = "active" }
//             };

//             var encrypted = FlowEncryptStatic.EncryptFlowResponse(
//                 responseObj, aesKey, iv);

//             return Results.Text(encrypted, "application/json");
//         }

//         // 3️⃣ Default response
//         var fallback = FlowEncryptStatic.EncryptFlowResponse(
//             new { version = "3.0", data = new { status = "active" } },
//             aesKey, iv);

//         return Results.Text(fallback, "application/json");
//     }
//     catch (Exception ex)
//     {
//         Console.Error.WriteLine(ex);
//         return Results.StatusCode(500);
//     }
// });

// app.Run();


// =======================================================
// 🔹 HELPERS
// =======================================================
// static async Task SendFlowAsync(
//     string to,
//     IHttpClientFactory httpClientFactory)
// {
//     var phoneNumberId = Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID");
//     var accessToken = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
//     var flowId = Environment.GetEnvironmentVariable("WHATSAPP_FLOW_ID");

//     var url =
//         $"https://graph.facebook.com/v19.0/{phoneNumberId}/messages";

//     var payload = new
//     {
//         messaging_product = "whatsapp",
//         to,
//         type = "interactive",
//         interactive = new
//         {
//             type = "flow",
//             flow = new
//             {
//                 name = "ADDRESS_FLOW",
//                 flow_id = flowId,
//                 flow_cta = "Order Now"
//             }
//         }
//     };

//     var client = httpClientFactory.CreateClient();

//     var request = new HttpRequestMessage(HttpMethod.Post, url)
//     {
//         Content = JsonContent.Create(payload)
//     };

//     request.Headers.Authorization =
//         new AuthenticationHeaderValue("Bearer", accessToken);

//     await client.SendAsync(request);
// }

// record AreaDto(string Code, string Name);






















































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


// app.Run();





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
