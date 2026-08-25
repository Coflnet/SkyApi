#nullable enable
namespace Coflnet.Sky.Core;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

/// <summary>Marks non-nullable schema properties as required.</summary>
public class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    /// <summary>Applies required-property metadata to a schema.</summary>
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
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
                schema.Required!.Add(property.Key);
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
