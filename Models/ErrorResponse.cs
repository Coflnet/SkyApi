using Swashbuckle.AspNetCore.SwaggerGen;

namespace Coflnet.Sky.Api.Models
{
    public class ErrorResponse
    {
        public string Slug;
        public string Message;
        public string Trace;
    }
}