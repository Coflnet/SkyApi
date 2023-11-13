using AutoMapper;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Trade.Client.Model;

namespace Coflnet.Sky.Api.Services;
public class OrganizationProfile : Profile
{
    public OrganizationProfile()
    {
        CreateMap<TradeRequest, TradeRequestDTO>()
            .ForMember(d => d.Id, o => o.Ignore());
        CreateMap<PlayerState.Client.Model.Item, Item>();
        CreateMap<Models.WantedItem, Trade.Client.Model.WantedItem>()
            .ForMember(d => d.Id, o => o.Ignore());

        CreateMap<TradeRequestDTO, TradeRequest>();
        CreateMap<Item, PlayerState.Client.Model.Item>();
        CreateMap<Trade.Client.Model.WantedItem, Models.WantedItem>();
    }
}