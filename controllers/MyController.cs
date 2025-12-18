
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class MyController : ControllerBase
{
    [HttpGet("test")]
    public IActionResult Test() => Ok("It works!");
}
