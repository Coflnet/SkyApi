using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.DiscordBot.Client.Api;
using Mscc.GenerativeAI;
using Newtonsoft.Json;
using System.Linq;
using Coflnet.Sky.Api.Client.Api;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Providing general info about the project
/// </summary>
[ApiController]
[Route("api/data")]
[ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.None, NoStore = true)]
public class InfoController : ControllerBase
{
    [HttpGet("updates/{year}/{month}")]
    public async Task<IEnumerable<DiscordBot.Client.Model.DiscordMessage>> GetUpdates(int year, int month, [FromServices] IMessageApi sniperApi)
    {
        if (year < 2022 || year > DateTime.UtcNow.Year)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2022 and the current year.");
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        var dateTime = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return await sniperApi.GetMessagesAsync("devlog", dateTime);
    }

    [HttpGet("ai")]
    [ServiceFilter(typeof(Coflnet.Sky.Api.Helper.AiRateLimitFilter))]
    public async Task<string> GetAiInfo([FromServices] GoogleAI googleAI, [FromServices] IPricesApi pricesApi, [FromServices] ISearchApi searchApi, string prompt)
    {

        var priceRetrieverDelegate = new Func<string, Dictionary<string,string>, Task<string>>(async (itemTag, filters) =>
        {
            // Simulate an asynchronous operation to get the price of the item
            filters.Remove("item"); // handle common halucination
            var price = await pricesApi.ApiItemPriceItemTagGetAsync(itemTag, filters);
            Console.WriteLine($"Retrieving price for item: {itemTag} with filters: {JsonConvert.SerializeObject(filters)}\nResult: {JsonConvert.SerializeObject(price)}");
            return $"Price summary of {itemTag} is " + JsonConvert.SerializeObject(price); // Replace with actual price retrieval logic
        });
        var searchItem = new Func<string, Task<string>>(async (itemName) =>
        {
            var result = await searchApi.ApiItemSearchSearchValGetAsync(itemName);
            Console.WriteLine($"Searching for item: {itemName}\nResult: {JsonConvert.SerializeObject(result)}");
            return $"Found following result " + JsonConvert.SerializeObject(result);
        });
        var filterRetrieverDelegate = new Func<string, Task<List<Client.Model.FilterOptions>>>(async (itemTag) =>
        {
            var filters = await pricesApi.ApiFilterOptionsGetAsync(itemTag);
            return filters;
        });
        var tools = new Tools();
        tools.AddFunction("searchItem", "Searches for an item by name. Extract the item name from user's query and pass it as 'itemName' parameter.", searchItem);
        tools.AddFunction("getPrice", "Gets price for an item using its tag. Pass the tag as 'itemTag' parameter. Pass filter keyvalue pairs if they are found in the getFilters result eg {\"sharpness\":\"7\"}. These prices are from sales of the last 48 hours", priceRetrieverDelegate);
        tools.AddFunction("getFilters", "Gets filters for an item using its tag. Pass the tag as 'itemTag' parameter.", filterRetrieverDelegate);
        
        var model = googleAI.GenerativeModel(Model.Gemini25Flash, tools: tools);
        var systemPrompt = @"You are a Hypixel SkyBlock expert assistant. You have extensive knowledge about SkyBlock items, prices, strategies, game mechanics, and updates. 

When users ask about item prices, follow this workflow:
1. Extract the item name from the user's query (e.g., 'Hyperion', 'Superior Dragon Armor', 'Aspect of the Dragons')
2. Call searchItem with parameter: {""itemName"": ""extracted_item_name""}
3. Use the returned item tag to call getFilters with parameter: {""itemTag"": ""returned_tag""}
4. Use the item tag and appropriate filters to call getPrice with parameters: {""itemTag"": ""returned_tag"", ""filters"": {}}

IMPORTANT: Always extract the actual item name from the user's question and pass it correctly as a parameter.

Examples:
- User: 'What is the price for a hyperion' → Call searchItem with {""itemName"": ""Hyperion""}
- User: 'How much does superior dragon armor cost' → Call searchItem with {""itemName"": ""Superior Dragon Armor""}
- User: 'AOTD price' → Call searchItem with {""itemName"": ""aotd""} but note that AOTD is a common abbreviation for Aspect of the Dragons, so the search result mcontains the full name instead still use it.";
        
        var chat = model.StartChat();
        var response = await chat.SendMessage(systemPrompt + "\n\nUser: " + prompt);
        
        // Handle function calls automatically
        int maxIterations = 5; // Prevent infinite loops
        int currentIteration = 0;
        
        while (response.FunctionCalls?.Any() == true && currentIteration < maxIterations)
        {
            currentIteration++;
            var functionResponses = new List<FunctionResponse>();
            
            foreach (var functionCall in response.FunctionCalls)
            {
                string result = "";
                Console.WriteLine($"Executing function: {functionCall.Name}");
                Console.WriteLine($"Raw Args: {JsonConvert.SerializeObject(functionCall.Args)}");
                Console.WriteLine($"Args Type: {functionCall.Args?.GetType()?.Name}");
                
                try
                {
                    if (functionCall.Name == "getPrice")
                    {
                        var args = ParseFunctionArgs(functionCall.Args);
                        // Try both camelCase and snake_case parameter names
                        var itemTag = args.GetValueOrDefault("itemTag")?.ToString() ?? 
                                    args.GetValueOrDefault("item_tag")?.ToString() ?? "";
                        var filtersObj = args.GetValueOrDefault("filters");
                        var filters = filtersObj != null ?
                            JsonConvert.DeserializeObject<Dictionary<string, string>>(filtersObj.ToString() ?? "{}") ??
                            new Dictionary<string, string>() : new Dictionary<string, string>();
                        Console.WriteLine($"Calling getPrice with itemTag: '{itemTag}'");
                        Console.WriteLine($"Available args keys: {string.Join(", ", args.Keys)}");
                        result = await priceRetrieverDelegate(itemTag, filters);
                    }
                    else if (functionCall.Name == "getFilters")
                    {
                        var args = ParseFunctionArgs(functionCall.Args);
                        // Try both camelCase and snake_case parameter names
                        var itemTag = args.GetValueOrDefault("itemTag")?.ToString() ?? 
                                    args.GetValueOrDefault("item_tag")?.ToString() ?? "";
                        Console.WriteLine($"Calling getFilters with itemTag: '{itemTag}'");
                        Console.WriteLine($"Available args keys: {string.Join(", ", args.Keys)}");
                        var filters = await filterRetrieverDelegate(itemTag);
                        result = JsonConvert.SerializeObject(filters);
                    }
                    else if (functionCall.Name == "searchItem")
                    {
                        var args = ParseFunctionArgs(functionCall.Args);
                        // Try both camelCase and snake_case parameter names
                        var itemName = args.GetValueOrDefault("itemName")?.ToString() ?? 
                                     args.GetValueOrDefault("item_name")?.ToString() ?? "";
                        Console.WriteLine($"Calling searchItem with itemName: '{itemName}'");
                        Console.WriteLine($"Available args keys: {string.Join(", ", args.Keys)}");
                        if (string.IsNullOrEmpty(itemName))
                        {
                            result = "Error: itemName parameter is required but was empty or null";
                        }
                        else
                        {
                            result = await searchItem(itemName);
                        }
                    }
                    else
                    {
                        result = $"Unknown function: {functionCall.Name}";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing function {functionCall.Name}: {ex}");
                    result = $"Error executing function {functionCall.Name}: {ex.Message}";
                }
                
                var functionResponse = new FunctionResponse
                {
                    Name = functionCall.Name,
                    Response = result
                };
                functionResponses.Add(functionResponse);
            }
            
            // Create a text message with function results
            var functionResultsText = string.Join("\n", functionResponses.Select(fr => 
                $"Function {fr.Name} result: {fr.Response}"));
            
            response = await chat.SendMessage(functionResultsText);
        }
        
        Console.WriteLine("AI Response: " + JsonConvert.SerializeObject(response) + "\nfor prompt: " + prompt);
        return response.Text;
    }

    private static Dictionary<string, object> ParseFunctionArgs(object args)
    {
        if (args == null)
            return new Dictionary<string, object>();

        // Handle JsonElement specifically
        if (args is System.Text.Json.JsonElement jsonElement)
        {
            try
            {
                // Convert JsonElement to string and then deserialize with Newtonsoft.Json
                var rawJson = jsonElement.GetRawText();
                Console.WriteLine($"JsonElement raw text: {rawJson}");
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson) ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse JsonElement: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        // Try to convert to dictionary directly
        if (args is Dictionary<string, object> dict)
            return dict;

        // Try to deserialize as JSON if it's a string
        if (args is string jsonString)
        {
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString) ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        // Try to serialize and deserialize to normalize the object
        try
        {
            var json = JsonConvert.SerializeObject(args);
            Console.WriteLine($"Serialized args to: {json}");
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to serialize/deserialize args: {ex.Message}");
            return new Dictionary<string, object>();
        }
    }
}
