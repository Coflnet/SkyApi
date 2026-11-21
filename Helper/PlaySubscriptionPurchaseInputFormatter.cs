using Coflnet.Sky.Api.Controller;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coflnet.Sky.Api.Helper;

// The default Newtonsoft formatter logs parse exceptions, which can include purchase tokens.
internal sealed class PlaySubscriptionPurchaseInputFormatter() : SystemTextJsonInputFormatter(
    new JsonOptions { AllowInputFormatterExceptionMessages = false }, NullLogger<SystemTextJsonInputFormatter>.Instance)
{
    protected override bool CanReadType(Type type) => type == typeof(PremiumController.PlaySubscriptionPurchase);
}
