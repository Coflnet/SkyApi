using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Mayor.Client.Api;
using Coflnet.Sky.Mayor.Client.Model;
using System;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Endpoints for mayor history data
/// </summary>
[ApiController]
[Route("api/mayor")]
[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
public class MayorController : ControllerBase
{
    IElectionPeriodsApi mayorService;
    /// <summary>
    /// Creates a new instance of <see cref="KatController"/>
    /// </summary>
    /// <param name="mayorService"></param>
    public MayorController(IElectionPeriodsApi mayorService)
    {
        this.mayorService = mayorService;
    }

    /// <summary>
    /// Return Election results for a specific year
    /// </summary>
    /// <returns></returns>
    [Route("{year}")]
    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<ModelElectionPeriod> GetYear(int year)
    {
        return await mayorService.ElectionPeriodYearGetAsync(year);
    }

    /// <summary>
    /// Gets election data between two Timestamps
    /// </summary>
    /// <param name="from">Start ISO 8601</param>
    /// <param name="to">End eg. 2022-09-22T20:03:10.937Z</param>
    /// <returns></returns>
    [Route("")]
    [HttpGet]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "from", "to" })]
    public async Task<IEnumerable<ModelElectionPeriod>> GetMultiple(DateTime from, DateTime to = default(DateTime))
    {
        if(default(DateTime) == to)
            to = DateTime.Now;
        return await mayorService.ElectionPeriodRangeGetAsync(from.ToUnix() * 1000, to.ToUnix() * 1000);
    }
}


