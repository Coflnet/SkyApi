using System.Threading.Tasks;
using Coflnet.Sky.Api;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Hypixel.Controller;
/// <summary>
/// Endpoints for collecting data
/// </summary>
[ApiController]
[Route("api/data")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class DataController : ControllerBase
{

    /// <summary>
    /// Creates a new instance of <see cref="DataController"/>
    /// </summary>
    public DataController()
    {
    }

    /// <summary>
    /// Endpoint to upload proxied data
    /// </summary>
    /// <returns></returns>
    [Route("proxy")]
    [HttpPost]
    public string UploadProxied()
    {
        Request.Headers.TryGetValue("X-Request-Id", out var id);
        return "received " + id;
    }
}
