using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints for best pet to put into kat
    /// </summary>
    [ApiController]
    [Route("api/kat")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class KatController : ControllerBase
    {
        KatService katService;
        /// <summary>
        /// Creates a new instance of <see cref="KatController"/>
        /// </summary>
        /// <param name="katService"></param>
        public KatController(KatService katService)
        {
            this.katService = katService;
        }

        /// <summary>
        /// Kat flips
        /// </summary>
        /// <returns></returns>
        [Route("profit")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<KatFlip>> GetProfitable()
        {
            return await katService.GetProfitable();
        }
        /// <summary>
        /// Raw data of upgrade cost
        /// </summary>
        /// <returns></returns>
        [Route("data")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<KatUpgradeCost>> GetData()
        {
            return await katService.GetRawData();
        }
    }
}