using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>
    /// Operation filter that only adds authorization requirements to endpoints with [Authorize] attribute
    /// </summary>
    public class AuthorizeCheckOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Check if the endpoint has [Authorize] attribute
            var hasAuthorize = context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() == true
                             || context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();

            if (hasAuthorize)
            {
                // Add a detailed 401 response body explaining how to provide tokens
                var instructionText = "Missing or invalid token. Provide a valid token in one of the headers:\n" +
                                      "- Authorization: Bearer {token}\n" +
                                      "- GoogleToken: {token}\n\n" +
                                      "See https://sky.coflnet.com/api for documentation.";

                operation.Responses.TryAdd("401", new OpenApiResponse
                {
                    Description = "Unauthorized",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Example = JsonNode.Parse("{\"message\": \"" + instructionText.Replace("\n", "\\n") + "\"}")
                        },
                        ["text/plain"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            Example = JsonValue.Create(instructionText)
                        }
                    }
                });

                operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

                operation.Security = new List<OpenApiSecurityRequirement>
                {
                    new OpenApiSecurityRequirement
                    {
                        [
                            new OpenApiSecuritySchemeReference("Bearer", null)
                        ] = new List<string> { "" }
                    },
                    new OpenApiSecurityRequirement
                    {
                        [
                            new OpenApiSecuritySchemeReference("GoogleToken", null)
                        ] = new List<string> { "" }
                    }
                };
            }
        }
    }
}
