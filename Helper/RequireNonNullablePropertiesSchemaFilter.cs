#nullable enable
namespace Coflnet.Sky.Core;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

public class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties == null)
            return;

        var nullableProperties = context.Type.GetProperties()
            .Where(x => IsNullable(x.PropertyType))
            .Select(x => x.Name.ToCamelCase())
            .ToHashSet();

        foreach (var property in schema.Properties)
        {
            if (!nullableProperties.Contains(property.Key))
            {
                schema.Required.Add(property.Key);
            }
        }
    }

    private static bool IsNullable(Type type)
    {
        return Nullable.GetUnderlyingType(type) != null || 
               (!type.IsValueType && !type.IsGenericParameter);
    }
}
#nullable restore