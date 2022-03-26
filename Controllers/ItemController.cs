using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints about items
    /// </summary>
    [ApiController]
    [Route("api/items")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ItemController : ControllerBase
    {
        private IConfiguration config;
        private HypixelContext db;

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="context"></param>
        public ItemController(IConfiguration config, HypixelContext context)
        {
            this.config = config;
            this.db = context;
        }

        /// <summary>
        /// A list of item tags (hypixel ids) that are tradeable on bazaar
        /// This gets updated once every hour
        /// </summary>
        /// <returns></returns>
        [Route("bazaar/tags")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<string>> GetFlipTime()
        {
            return await db.Items.Where(i => i.IsBazaar).Select(i => i.Tag).ToListAsync();
        }
    }
}

