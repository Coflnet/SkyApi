using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Coflnet.Hypixel.Controller
{
    public class ApiControllerTests
    {
        [Test]
        public void MinResultCount()
        {
            Assert.IsTrue(ApiController.EnoughResults("xx", 5));
            Assert.IsTrue(ApiController.EnoughResults("xxxxx", 2));
            Assert.IsTrue(ApiController.EnoughResults("xxxxxx", 1));
            Assert.IsFalse(ApiController.EnoughResults("x", 9));
            Assert.IsFalse(ApiController.EnoughResults("xxxx", 1));
            Assert.IsFalse(ApiController.EnoughResults("xxxxxxxxxxxxxxx", 0), "no matter how long minimum is one");
        }
    }
}

