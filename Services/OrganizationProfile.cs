using AutoMapper;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Trade.Client.Model;
using HashidsNet;

namespace Coflnet.Sky.Api.Services;
public class OrganizationProfile : Profile
{
    public OrganizationProfile()
    {
        Hashids hashids = new Hashids("CoflnetSkyTrades", 8);
        CreateMap<TradeRequest, TradeRequestDTO>()
            .ForMember(d => d.Id, o => o.Ignore());
        CreateMap<PlayerState.Client.Model.Item, Item>();
        CreateMap<Models.WantedItem, Trade.Client.Model.WantedItem>()
            .ForMember(d => d.Id, o => o.Ignore());

        CreateMap<TradeRequestDTO, TradeRequest>()
            .ForMember(d => d.PlayerName, o => o.Ignore())
            .ForMember(d => d.Id, o => o.MapFrom(s => hashids.Encode((int)(s.Id ?? 0))));
        CreateMap<Item, PlayerState.Client.Model.Item>();
        CreateMap<Trade.Client.Model.WantedItem, Models.WantedItem>()
            .ForMember(d => d.ItemName, o => o.Ignore());

        CreateMap<Coflnet.Sky.Api.Models.Notifications.NotificationTarget, Coflnet.Sky.EventBroker.Client.Model.NotificationTarget>()
            .ReverseMap();
    }
}