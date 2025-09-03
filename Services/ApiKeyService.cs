using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Client.Api;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.Api.Services;

public class ApiKeyService
{
    private IApiKeyApi keyApi;
    public ApiKeyService(IApiKeyApi keyApi)
    {
        this.keyApi = keyApi;
    }
    public async Task<ModCommands.Client.Model.ApiKeyInfoResponse> GetKeyInfo(ControllerBase controller)
    {
        var Request = controller.Request;
        var apiKey = Request.Headers.TryGetValue("X-Api-Key", out Microsoft.Extensions.Primitives.StringValues value) ? value.ToString() :
                                 Request.Headers.TryGetValue("x-api-key", out value) ? value.ToString() :
                                 Request.Query.ContainsKey("apiKey") ? Request.Query["apiKey"].ToString() :
                                 Request.Query.ContainsKey("apikey") ? Request.Query["apikey"].ToString() :
                                 Request.Query.ContainsKey("key") ? Request.Query["key"].ToString() :
                                 null;

        if (string.IsNullOrEmpty(apiKey))
            throw new CoflnetException("unauthorized", "API key not provided add x-api-key header or apiKey GET parameter containing api key avalable from `/cofl api new` in game");

        // validate key (throws/returns error when invalid depending on implementation)
        var keyResponse = await keyApi.ApiKeyValidateKeyGetAsync(apiKey);
        if (!keyResponse.TryOk(out var keyInfo))
            throw new CoflnetException("unauthorized", "Invalid API key");
        return keyInfo;
    }
}